using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using BackendApi.Data;
using BackendApi.Dtos;
using BackendApi.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace BackendApi.Services;

public interface IAuthService
{
    Task<AuthResponse> RegisterAsync(RegisterRequest request);
    Task<AuthResponse> LoginAsync(LoginRequest request);
}

public class AuthService(AppDbContext db, IConfiguration config) : IAuthService
{
    public async Task<AuthResponse> RegisterAsync(RegisterRequest request)
    {
        var role = await db.Roles.FirstOrDefaultAsync(r => r.Name == request.Role)
            ?? throw new InvalidOperationException($"Unknown role '{request.Role}'.");

        if (await db.Users.AnyAsync(u => u.Email == request.Email.Trim().ToLowerInvariant()))
            throw new InvalidOperationException("Email already registered.");

        if (request.Role is "Advisor" or "Evaluator" && request.DomainId is null)
            throw new InvalidOperationException("Domain is required for Advisor/Evaluator.");

        var user = new User
        {
            Email = request.Email.Trim().ToLowerInvariant(),
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            FullName = request.FullName.Trim(),
            RoleId = role.Id,
            DomainId = request.DomainId
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        await db.Entry(user).Reference(u => u.Domain).LoadAsync();
        user.Role = role;
        return CreateToken(user);
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var user = await db.Users.Include(u => u.Role).Include(u => u.Domain)
            .FirstOrDefaultAsync(u => u.Email == email && u.IsActive)
            ?? throw new UnauthorizedAccessException("Invalid credentials.");

        if (!BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
            throw new UnauthorizedAccessException("Invalid credentials.");

        return CreateToken(user);
    }

    private AuthResponse CreateToken(User user)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(config["Jwt:Key"]!));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(ClaimTypes.Name, user.FullName),
            new Claim(ClaimTypes.Role, user.Role.Name)
        };

        var token = new JwtSecurityToken(
            issuer: config["Jwt:Issuer"],
            audience: config["Jwt:Audience"],
            claims: claims,
            expires: DateTime.UtcNow.AddHours(12),
            signingCredentials: creds);

        return new AuthResponse(
            new JwtSecurityTokenHandler().WriteToken(token),
            user.Id, user.Email, user.FullName, user.Role.Name,
            user.DomainId, user.Domain?.Name);
    }
}

public interface IEmailService
{
    Task QueueAsync(string to, string subject, string body);
}

public class FakeEmailService(AppDbContext db, ILogger<FakeEmailService> log) : IEmailService
{
    public async Task QueueAsync(string to, string subject, string body)
    {
        db.EmailOutbox.Add(new EmailOutbox { ToEmail = to, Subject = subject, Body = body });
        await db.SaveChangesAsync();
        log.LogInformation("FAKE EMAIL to {To}: {Subject}", to, subject);
    }
}

public interface INotificationService
{
    Task NotifyAsync(int userId, string title, string message);
    Task NotifyManyAsync(IEnumerable<int> userIds, string title, string message);
}

public class NotificationService(AppDbContext db) : INotificationService
{
    public async Task NotifyAsync(int userId, string title, string message)
    {
        db.Notifications.Add(new Notification { UserId = userId, Title = title, Message = message });
        await db.SaveChangesAsync();
    }

    public async Task NotifyManyAsync(IEnumerable<int> userIds, string title, string message)
    {
        foreach (var id in userIds.Distinct())
            db.Notifications.Add(new Notification { UserId = id, Title = title, Message = message });
        await db.SaveChangesAsync();
    }
}

public interface ISchedulingService
{
    Task RebuildTimeslotsAsync(int eventId);
    Task EnsureNoEvaluatorOverlapAsync(int evaluatorId, DateTime start, DateTime end, int? excludeSubmissionId = null);
}

public class SchedulingService(AppDbContext db) : ISchedulingService
{
    public async Task EnsureNoEvaluatorOverlapAsync(int evaluatorId, DateTime start, DateTime end, int? excludeSubmissionId = null)
    {
        var conflict = await db.ProjectSubmissions.AnyAsync(s =>
            s.EvaluatorUserId == evaluatorId &&
            s.EvaluationStart != null && s.EvaluationEnd != null &&
            (excludeSubmissionId == null || s.Id != excludeSubmissionId) &&
            s.EvaluationStart < end && s.EvaluationEnd > start);

        if (conflict)
            throw new InvalidOperationException("Evaluator already has an overlapping timeslot.");
    }

    public async Task RebuildTimeslotsAsync(int eventId)
    {
        var ev = await db.Events.FindAsync(eventId)
            ?? throw new InvalidOperationException("Event not found.");
        if (ev.IsFinalized)
            throw new InvalidOperationException("Event is finalized; timeslots are locked.");

        var duration = TimeSpan.FromMinutes(ev.SlotDurationMinutes);
        var submissions = await db.ProjectSubmissions
            .Where(s => s.EventId == eventId && s.AssignedRoomId != null && s.Status != "AdminRejected")
            .OrderBy(s => s.AssignedRoomId).ThenBy(s => s.Id)
            .ToListAsync();

        foreach (var s in submissions)
        {
            s.EvaluationStart = null;
            s.EvaluationEnd = null;
            if (s.Status == "Scheduled") s.Status = "RoomAssigned";
        }
        await db.SaveChangesAsync();

        foreach (var roomGroup in submissions.GroupBy(s => s.AssignedRoomId!.Value))
        {
            var cursor = ev.StartDate;
            foreach (var s in roomGroup)
            {
                if (s.EvaluatorUserId is null)
                    continue;

                // find next free slot for this evaluator within event window
                var start = cursor;
                var end = start + duration;
                while (end <= ev.EndDate)
                {
                    var overlap = submissions.Any(o =>
                        o.Id != s.Id &&
                        o.EvaluatorUserId == s.EvaluatorUserId &&
                        o.EvaluationStart != null &&
                        o.EvaluationStart < end &&
                        o.EvaluationEnd > start);

                    if (!overlap)
                        break;

                    start += duration;
                    end = start + duration;
                }

                if (end > ev.EndDate)
                    throw new InvalidOperationException(
                        $"Cannot place submission {s.Id} without evaluator overlap inside the event window.");

                s.EvaluationStart = start;
                s.EvaluationEnd = end;
                s.Status = "Scheduled";
                cursor = end; // next group in same room follows
            }
        }

        await db.SaveChangesAsync();
    }
}

public interface IRoomAssignmentService
{
    Task<IReadOnlyList<RoomSuggestionDto>> SuggestRoomsAsync(int eventId);
    Task AssignRoomAsync(int submissionId, int roomId);
}

public class RoomAssignmentService(AppDbContext db) : IRoomAssignmentService
{
    public async Task<IReadOnlyList<RoomSuggestionDto>> SuggestRoomsAsync(int eventId)
    {
        var floorIds = await db.EventFloors.Where(f => f.EventId == eventId).Select(f => f.FloorId).ToListAsync();
        var rooms = await db.Rooms
            .Include(r => r.Floor).ThenInclude(f => f.Building)
            .Where(r => floorIds.Contains(r.FloorId) && r.IsActive)
            .ToListAsync();
        var caps = await db.EventRoomCaps.Where(c => c.EventId == eventId).ToDictionaryAsync(c => c.RoomId, c => c.MaxGroups);
        var assigned = await db.ProjectSubmissions
            .Where(s => s.EventId == eventId && s.AssignedRoomId != null && s.Status != "AdminRejected")
            .GroupBy(s => s.AssignedRoomId!.Value)
            .Select(g => new { RoomId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.RoomId, x => x.Count);

        return rooms
            .Select(r =>
            {
                var max = caps.TryGetValue(r.Id, out var m) ? m : r.CapacityGroupsDefault;
                var count = assigned.TryGetValue(r.Id, out var c) ? c : 0;
                var buildingName = r.Floor?.Building?.Name ?? "Unknown Building";
                var floorName = r.Floor?.Name ?? "Unknown Floor";
                return new RoomSuggestionDto(r.Id, r.Code, buildingName, floorName, max, count, Math.Max(0, max - count));
            })
            .Where(x => x.Remaining > 0)
            .OrderByDescending(x => x.Remaining)
            .ThenBy(x => x.Code)
            .ToList();
    }

    public async Task AssignRoomAsync(int submissionId, int roomId)
    {
        var submission = await db.ProjectSubmissions.Include(s => s.Event)
            .FirstOrDefaultAsync(s => s.Id == submissionId)
            ?? throw new InvalidOperationException("Submission not found.");

        if (submission.Event.IsFinalized)
            throw new InvalidOperationException("Event is finalized.");

        if (submission.Status is not ("Accepted" or "RoomAssigned" or "Scheduled"))
            throw new InvalidOperationException("Submission must be accepted before room assignment.");

        var suggestions = await SuggestRoomsAsync(submission.EventId);
        var room = suggestions.FirstOrDefault(r => r.RoomId == roomId)
            ?? throw new InvalidOperationException("Room is not available for this event or is at capacity.");

        if (room.Remaining <= 0 && submission.AssignedRoomId != roomId)
            throw new InvalidOperationException("Room is at hard capacity.");

        // if reassigning, ensure capacity on new room
        if (submission.AssignedRoomId != roomId)
        {
            var currentCount = await db.ProjectSubmissions.CountAsync(s =>
                s.EventId == submission.EventId && s.AssignedRoomId == roomId && s.Status != "AdminRejected");
            if (currentCount >= room.MaxGroups)
                throw new InvalidOperationException("Room is at hard capacity.");
        }

        submission.AssignedRoomId = roomId;
        submission.Status = "RoomAssigned";
        submission.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }
}
