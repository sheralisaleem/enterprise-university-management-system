using System.Security.Claims;
using BackendApi.Data;
using BackendApi.Dtos;
using BackendApi.Hubs;
using BackendApi.Models;
using BackendApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.IO.Compression;

namespace BackendApi.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class GroupsController(AppDbContext db, IEmailService email) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<GroupDto>>> List()
    {
        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var role = User.FindFirstValue(ClaimTypes.Role);
        var q = db.ProjectGroups.Include(g => g.Domain).Include(g => g.AdvisorUser).Include(g => g.Members).AsQueryable();
        q = role switch
        {
            "Student" => q.Where(g => g.LeaderUserId == userId),
            "Advisor" => q.Where(g => g.AdvisorUserId == userId),
            _ => q
        };
        var groups = await q.OrderByDescending(g => g.CreatedAt).ToListAsync();
        return Ok(groups.Select(Map));
    }

    [HttpPost]
    [Authorize(Roles = "Student,Admin")]
    public async Task<ActionResult<GroupDto>> Create(CreateGroupRequest request)
    {
        var leaderId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        if (!await db.Domains.AnyAsync(d => d.Id == request.DomainId && d.IsActive))
            return BadRequest(new { message = "Invalid domain." });
        var advisor = await db.Users.Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.Id == request.AdvisorUserId && u.Role.Name == "Advisor");
        if (advisor is null) return BadRequest(new { message = "Invalid advisor." });

        var members = request.Members.ToList();
        if (members.Count is < 1 or > 5)
            return BadRequest(new { message = "Provide 1–5 members (typically 3)." });

        var leader = await db.Users.FindAsync(leaderId);
        var group = new ProjectGroup
        {
            Title = request.Title,
            DomainId = request.DomainId,
            LeaderUserId = leaderId,
            AdvisorUserId = request.AdvisorUserId
        };
        db.ProjectGroups.Add(group);
        await db.SaveChangesAsync();

        // ensure leader is first member
        var leaderEmail = leader!.Email;
        var hasLeader = members.Any(m => m.Email.Equals(leaderEmail, StringComparison.OrdinalIgnoreCase));
        if (!hasLeader)
        {
            members.Insert(0, new MemberInput(leader.FullName, leader.Email, null));
        }

        foreach (var m in members)
        {
            var isLeader = m.Email.Equals(leaderEmail, StringComparison.OrdinalIgnoreCase);
            db.GroupMembers.Add(new GroupMember
            {
                ProjectGroupId = group.Id,
                FullName = m.FullName,
                Email = m.Email.Trim().ToLowerInvariant(),
                StudentId = m.StudentId,
                IsLeader = isLeader
            });
        }
        await db.SaveChangesAsync();
        group = await db.ProjectGroups.Include(g => g.Domain).Include(g => g.AdvisorUser).Include(g => g.Members)
            .FirstAsync(g => g.Id == group.Id);
        return Ok(Map(group));
    }

    [HttpGet("advisors")]
    [Authorize(Roles = "Student,Admin")]
    public async Task<IActionResult> Advisors()
    {
        var advisors = await db.Users.Include(u => u.Role).Include(u => u.Domain)
            .Where(u => u.Role.Name == "Advisor" && u.IsActive)
            .Select(u => new { u.Id, u.FullName, u.Email, Domain = u.Domain!.Name })
            .ToListAsync();
        return Ok(advisors);
    }

    private static GroupDto Map(ProjectGroup g) => new(
        g.Id, g.Title, g.DomainId, g.Domain?.Name ?? "", g.AdvisorUserId, g.AdvisorUser?.FullName ?? "",
        g.Members.Select(m => new MemberDto(m.Id, m.FullName, m.Email, m.StudentId, m.IsLeader)));
}

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class SubmissionsController(
    AppDbContext db,
    IRoomAssignmentService rooms,
    ISchedulingService scheduling,
    INotificationService notifications,
    IHubContext<DashboardHub> hub) : ControllerBase
{
    private int UserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet]
    public async Task<ActionResult<IEnumerable<SubmissionDto>>> List([FromQuery] int? eventId)
    {
        var role = User.FindFirstValue(ClaimTypes.Role)!;
        var q = db.ProjectSubmissions
            .Include(s => s.Event)
            .Include(s => s.ProjectGroup).ThenInclude(g => g.Domain)
            .Include(s => s.ProjectGroup).ThenInclude(g => g.AdvisorUser)
            .Include(s => s.ProjectGroup).ThenInclude(g => g.LeaderUser)
            .Include(s => s.ProjectGroup).ThenInclude(g => g.Members)
            .Include(s => s.AssignedRoom)
            .Include(s => s.EvaluatorUser)
            .Include(s => s.Files)
            .AsQueryable();

        if (eventId is not null) q = q.Where(s => s.EventId == eventId);

        q = role switch
        {
            "Student" => q.Where(s => s.ProjectGroup.LeaderUserId == UserId),
            "Advisor" => q.Where(s => s.ProjectGroup.AdvisorUserId == UserId),
            "Evaluator" => q.Where(s => s.EvaluatorUserId == UserId),
            _ => q
        };

        var list = await q.OrderByDescending(s => s.UpdatedAt).ToListAsync();
        return Ok(list.Select(Map));
    }

    [HttpPost("event/{eventId:int}")]
    [Authorize(Roles = "Student")]
    public async Task<ActionResult<SubmissionDto>> Submit(int eventId, SubmitToEventRequest request)
    {
        var ev = await db.Events.FindAsync(eventId);
        if (ev is null || ev.Status is not ("Open" or "Draft") || ev.IsFinalized)
            return BadRequest(new { message = "Event is not open for submissions." });

        var group = await db.ProjectGroups.Include(g => g.Members).Include(g => g.AdvisorUser)
            .FirstOrDefaultAsync(g => g.Id == request.ProjectGroupId && g.LeaderUserId == UserId);
        if (group is null) return NotFound(new { message = "Group not found." });

        var existing = await db.ProjectSubmissions
            .FirstOrDefaultAsync(s => s.EventId == eventId && s.ProjectGroupId == group.Id);

        if (existing is not null)
        {
            if (existing.Status is "AdvisorRejected" or "AdminRejected")
            {
                if (!existing.StudentAcknowledgedRejection)
                    return BadRequest(new { message = "Acknowledge the rejection before resubmitting." });
                existing.Status = "Submitted";
                existing.AdvisorRejectReason = null;
                existing.AdminRejectReason = null;
                existing.StudentAcknowledgedRejection = false;
                existing.SubmittedAt = DateTime.UtcNow;
                existing.UpdatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync();
                await NotifyDashboard(existing.EventId);
                return Ok(await LoadDto(existing.Id));
            }
            return Conflict(new { message = "Already submitted for this event." });
        }

        var submission = new ProjectSubmission
        {
            EventId = eventId,
            ProjectGroupId = group.Id,
            Status = "Submitted"
        };
        db.ProjectSubmissions.Add(submission);
        await db.SaveChangesAsync();

        await notifications.NotifyAsync(group.AdvisorUserId, "New submission",
            $"'{group.Title}' submitted for event review.");
        await NotifyDashboard(eventId);
        return Ok(await LoadDto(submission.Id));
    }

    [HttpPost("{id:int}/acknowledge-rejection")]
    [Authorize(Roles = "Student")]
    public async Task<IActionResult> Acknowledge(int id)
    {
        var s = await db.ProjectSubmissions.Include(x => x.ProjectGroup)
            .FirstOrDefaultAsync(x => x.Id == id && x.ProjectGroup.LeaderUserId == UserId);
        if (s is null) return NotFound();
        if (s.Status is not ("AdvisorRejected" or "AdminRejected"))
            return BadRequest(new { message = "No rejection to acknowledge." });
        s.StudentAcknowledgedRejection = true;
        s.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Ok(new { message = "Acknowledged. You may resubmit." });
    }

    [HttpPost("{id:int}/files")]
    [Authorize(Roles = "Student")]
    [RequestSizeLimit(100_000_000)]
    public async Task<IActionResult> Upload(int id, IFormFile file, [FromForm] string fileType)
    {
        var allowedTypes = new[] { "Document", "Banner", "Source", "Presentation" };
        if (!allowedTypes.Contains(fileType))
            return BadRequest(new { message = "fileType must be Document|Banner|Source|Presentation." });

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        var ok = fileType switch
        {
            "Document" => ext is ".pdf" or ".doc" or ".docx",
            "Presentation" => ext is ".ppt" or ".pptx" or ".pdf",
            "Banner" => ext is ".png" or ".jpg" or ".jpeg" or ".webp" or ".gif",
            "Source" => ext is ".zip" or ".rar",
            _ => false
        };
        if (!ok) return BadRequest(new { message = $"Invalid extension '{ext}' for {fileType}." });

        var s = await db.ProjectSubmissions.Include(x => x.ProjectGroup)
            .FirstOrDefaultAsync(x => x.Id == id && x.ProjectGroup.LeaderUserId == UserId);
        if (s is null) return NotFound();
        if (s.Status is "AdvisorApproved" or "Accepted" or "RoomAssigned" or "Scheduled")
            return BadRequest(new { message = "Cannot change files after approval." });

        var dir = Path.Combine(Directory.GetCurrentDirectory(), "uploads", id.ToString());
        Directory.CreateDirectory(dir);
        var stored = $"{Guid.NewGuid():N}{ext}";
        var path = Path.Combine(dir, stored);
        await using (var stream = System.IO.File.Create(path))
            await file.CopyToAsync(stream);

        db.SubmissionFiles.Add(new SubmissionFile
        {
            ProjectSubmissionId = id,
            FileName = file.FileName,
            FilePath = path,
            FileType = fileType,
            ContentType = file.ContentType,
            SizeBytes = file.Length
        });
        s.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Ok(new { message = "Uploaded.", file.FileName, fileType });
    }

    [HttpPost("{id:int}/advisor-approve")]
    [Authorize(Roles = "Advisor")]
    public async Task<IActionResult> AdvisorApprove(int id)
    {
        var s = await LoadForAdvisor(id);
        if (s is null) return NotFound();
        if (s.Status != "Submitted") return BadRequest(new { message = "Only submitted items can be approved." });
        s.Status = "AdvisorApproved";
        s.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        await notifications.NotifyAsync(s.ProjectGroup.LeaderUserId, "Advisor approved",
            $"'{s.ProjectGroup.Title}' was approved by your advisor and awaits SuperAdmin.");
        var adminIds = await db.Users.Include(u => u.Role).Where(u => u.Role.Name == "Admin" && u.IsActive).Select(u => u.Id).ToListAsync();
        await notifications.NotifyManyAsync(adminIds, "Advisor approved",
            $"'{s.ProjectGroup.Title}' awaits SuperAdmin review.");
        await NotifyDashboard(s.EventId);
        return Ok(await LoadDto(id));
    }

    [HttpPost("{id:int}/advisor-reject")]
    [Authorize(Roles = "Advisor")]
    public async Task<IActionResult> AdvisorReject(int id, RejectRequest request)
    {
        var s = await LoadForAdvisor(id);
        if (s is null) return NotFound();
        if (s.Status != "Submitted") return BadRequest(new { message = "Only submitted items can be rejected." });
        s.Status = "AdvisorRejected";
        s.AdvisorRejectReason = request.Reason;
        s.StudentAcknowledgedRejection = false;
        s.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        await notifications.NotifyAsync(s.ProjectGroup.LeaderUserId, "Advisor rejected",
            $"Reason: {request.Reason}. Acknowledge in the app before resubmitting.");
        await NotifyDashboard(s.EventId);
        return Ok(await LoadDto(id));
    }

    [HttpPost("{id:int}/admin-accept")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> AdminAccept(int id)
    {
        var s = await db.ProjectSubmissions.Include(x => x.ProjectGroup).FirstOrDefaultAsync(x => x.Id == id);
        if (s is null) return NotFound();
        if (s.Status != "AdvisorApproved")
            return BadRequest(new { message = "Must be advisor-approved first." });
        s.Status = "Accepted";
        s.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        await notifications.NotifyAsync(s.ProjectGroup.LeaderUserId, "Accepted by SuperAdmin",
            $"'{s.ProjectGroup.Title}' was accepted for the showcase event.");
        await notifications.NotifyAsync(s.ProjectGroup.AdvisorUserId, "Accepted by SuperAdmin",
            $"'{s.ProjectGroup.Title}' was accepted.");
        await NotifyDashboard(s.EventId);
        return Ok(await LoadDto(id));
    }

    [HttpPost("{id:int}/admin-reject")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> AdminReject(int id, RejectRequest request)
    {
        var s = await db.ProjectSubmissions.Include(x => x.ProjectGroup).FirstOrDefaultAsync(x => x.Id == id);
        if (s is null) return NotFound();
        if (s.Status != "AdvisorApproved")
            return BadRequest(new { message = "Must be advisor-approved first." });
        s.Status = "AdminRejected";
        s.AdminRejectReason = request.Reason;
        s.StudentAcknowledgedRejection = false;
        s.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        await notifications.NotifyAsync(s.ProjectGroup.LeaderUserId, "Rejected by SuperAdmin",
            $"Reason: {request.Reason}. Acknowledge before resubmitting.");
        await NotifyDashboard(s.EventId);
        return Ok(await LoadDto(id));
    }

    [HttpGet("event/{eventId:int}/room-suggestions")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<IEnumerable<RoomSuggestionDto>>> RoomSuggestions(int eventId) =>
        Ok(await rooms.SuggestRoomsAsync(eventId));

    [HttpPost("{id:int}/assign-room")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> AssignRoom(int id, AssignRoomRequest request)
    {
        try
        {
            await rooms.AssignRoomAsync(id, request.RoomId);
            var s = await db.ProjectSubmissions.Include(x => x.ProjectGroup).FirstAsync(x => x.Id == id);
            await notifications.NotifyAsync(s.ProjectGroup.LeaderUserId, "Room assigned",
                $"Your showcase room has been assigned.");
            await notifications.NotifyAsync(s.ProjectGroup.AdvisorUserId, "Room assigned",
                $"Room assigned for '{s.ProjectGroup.Title}'.");
            try { await scheduling.RebuildTimeslotsAsync(s.EventId); } catch { /* evaluator may be missing */ }
            await NotifyDashboard(s.EventId);
            return Ok(await LoadDto(id));
        }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpGet("{id:int}/evaluator-recommendations")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<IEnumerable<EvaluatorRecommendationDto>>> Recommendations(int id)
    {
        var s = await db.ProjectSubmissions.Include(x => x.ProjectGroup)
            .FirstOrDefaultAsync(x => x.Id == id);
        if (s is null) return NotFound();

        var counts = await db.ProjectSubmissions
            .Where(x => x.EventId == s.EventId && x.EvaluatorUserId != null)
            .GroupBy(x => x.EvaluatorUserId!.Value)
            .Select(g => new { Id = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Id, x => x.Count);

        var evaluators = await db.Users.Include(u => u.Role).Include(u => u.Domain)
            .Where(u => u.Role.Name == "Evaluator" && u.IsActive)
            .ToListAsync();

        var result = evaluators
            .Select(u => new EvaluatorRecommendationDto(
                u.Id, u.FullName, u.Email, u.Domain?.Name,
                u.DomainId == s.ProjectGroup.DomainId,
                counts.TryGetValue(u.Id, out var c) ? c : 0))
            .OrderByDescending(x => x.DomainMatch)
            .ThenBy(x => x.CurrentAssignments)
            .ThenBy(x => x.FullName)
            .ToList();
        return Ok(result);
    }

    [HttpPost("{id:int}/assign-evaluator")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> AssignEvaluator(int id, AssignEvaluatorRequest request)
    {
        var s = await db.ProjectSubmissions.Include(x => x.Event).Include(x => x.ProjectGroup)
            .FirstOrDefaultAsync(x => x.Id == id);
        if (s is null) return NotFound();
        if (s.Event.IsFinalized) return BadRequest(new { message = "Event finalized." });
        if (s.Status is not ("Accepted" or "RoomAssigned" or "Scheduled"))
            return BadRequest(new { message = "Accept the project first." });

        var evaluator = await db.Users.Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.Id == request.EvaluatorUserId && u.Role.Name == "Evaluator");
        if (evaluator is null) return BadRequest(new { message = "Invalid evaluator." });

        s.EvaluatorUserId = evaluator.Id;
        s.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        try { await scheduling.RebuildTimeslotsAsync(s.EventId); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }

        // realtime dashboard update; notification email/push only on finalize
        await hub.Clients.Group(DashboardHub.RoleGroup("Evaluator"))
            .SendAsync("AssignmentUpdated", new { submissionId = id, eventId = s.EventId });
        await NotifyDashboard(s.EventId);
        return Ok(await LoadDto(id));
    }

    [HttpGet("{id:int}/download-all")]
    [AllowAnonymous]
    public async Task<IActionResult> DownloadAllFiles(int id)
    {
        var s = await db.ProjectSubmissions.Include(x => x.Files).Include(x => x.ProjectGroup)
            .FirstOrDefaultAsync(x => x.Id == id);
        if (s is null || !s.Files.Any()) return NotFound(new { message = "No files found." });

        var memoryStream = new MemoryStream();
        using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, true))
        {
            foreach (var f in s.Files)
            {
                if (!System.IO.File.Exists(f.FilePath)) continue;
                var entry = archive.CreateEntry(f.FileName);
                using var entryStream = entry.Open();
                using var fileStream = System.IO.File.OpenRead(f.FilePath);
                await fileStream.CopyToAsync(entryStream);
            }
        }
        memoryStream.Position = 0;
        var safeGroupName = string.Join("_", s.ProjectGroup.Title.Split(Path.GetInvalidFileNameChars()));
        return File(memoryStream, "application/zip", $"{safeGroupName}_Files.zip");
    }

    private async Task<ProjectSubmission?> LoadForAdvisor(int id) =>
        await db.ProjectSubmissions.Include(x => x.ProjectGroup)
            .FirstOrDefaultAsync(x => x.Id == id && x.ProjectGroup.AdvisorUserId == UserId);

    private async Task NotifyDashboard(int eventId) =>
        await hub.Clients.Group(DashboardHub.EventGroup(eventId)).SendAsync("SubmissionsChanged", eventId);

    private async Task<SubmissionDto> LoadDto(int id)
    {
        var s = await db.ProjectSubmissions
            .Include(x => x.Event)
            .Include(x => x.ProjectGroup).ThenInclude(g => g.Domain)
            .Include(x => x.ProjectGroup).ThenInclude(g => g.AdvisorUser)
            .Include(x => x.ProjectGroup).ThenInclude(g => g.LeaderUser)
            .Include(x => x.ProjectGroup).ThenInclude(g => g.Members)
            .Include(x => x.AssignedRoom)
            .Include(x => x.EvaluatorUser)
            .Include(x => x.Files)
            .FirstAsync(x => x.Id == id);
        return Map(s);
    }

    private static SubmissionDto Map(ProjectSubmission s) => new(
        s.Id, s.EventId, s.Event?.Name ?? "", s.ProjectGroupId, s.ProjectGroup?.Title ?? "",
        s.ProjectGroup?.Domain?.Name ?? "", s.Status, s.AdvisorRejectReason, s.AdminRejectReason,
        s.StudentAcknowledgedRejection, s.AssignedRoomId, s.AssignedRoom?.Code,
        s.EvaluatorUserId, s.EvaluatorUser?.FullName, s.EvaluationStart, s.EvaluationEnd,
        s.ProjectGroup?.AdvisorUser?.FullName ?? "", s.ProjectGroup?.LeaderUser?.FullName ?? "",
        s.ProjectGroup?.Members.Select(m => new MemberDto(m.Id, m.FullName, m.Email, m.StudentId, m.IsLeader)) ?? Array.Empty<MemberDto>(),
        s.Files?.Select(f => new FileDto(f.Id, f.FileName, f.FileType, f.SizeBytes)) ?? Array.Empty<FileDto>());
}