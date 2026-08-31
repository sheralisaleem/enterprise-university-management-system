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

namespace BackendApi.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class EventsController(
    AppDbContext db,
    IHubContext<DashboardHub> hub) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<EventDto>>> List()
    {
        var events = await db.Events.OrderByDescending(e => e.StartDate).ToListAsync();
        return Ok(events.Select(Map));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<EventDto>> Get(int id)
    {
        var ev = await db.Events.FindAsync(id);
        return ev is null ? NotFound() : Ok(Map(ev));
    }

    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<EventDto>> Create(CreateEventRequest request)
    {
        if (request.EndDate <= request.StartDate)
            return BadRequest(new { message = "Invalid event window." });
        if (!request.FloorIds.Any())
            return BadRequest(new { message = "Assign at least one floor." });

        var adminId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var floors = await db.Floors.Include(f => f.Rooms)
            .Where(f => request.FloorIds.Contains(f.Id)).ToListAsync();
        if (floors.Count != request.FloorIds.Distinct().Count())
            return BadRequest(new { message = "One or more floors are invalid." });

        var overrides = (request.RoomCapOverrides ?? []).ToDictionary(x => x.RoomId, x => x.MaxGroups);
        var ev = new Event
        {
            Name = request.Name,
            Description = request.Description,
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            SlotDurationMinutes = request.SlotDurationMinutes <= 0 ? 15 : request.SlotDurationMinutes,
            Status = "Open",
            CreatedByUserId = adminId
        };

        foreach (var floor in floors)
        {
            ev.EventFloors.Add(new EventFloor { FloorId = floor.Id });
            foreach (var room in floor.Rooms.Where(r => r.IsActive))
            {
                var max = overrides.TryGetValue(room.Id, out var m) ? m : room.CapacityGroupsDefault;
                ev.RoomCaps.Add(new EventRoomCap { RoomId = room.Id, MaxGroups = max });
            }
        }

        db.Events.Add(ev);
        await db.SaveChangesAsync();
        await hub.Clients.All.SendAsync("EventUpdated", Map(ev));
        return Ok(Map(ev));
    }

    [HttpPost("{id:int}/finalize")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Finalize(int id,
        [FromServices] ISchedulingService scheduling,
        [FromServices] INotificationService notifications)
    {
        var ev = await db.Events.FindAsync(id);
        if (ev is null) return NotFound();
        if (ev.IsFinalized) return BadRequest(new { message = "Already finalized." });

        try { await scheduling.RebuildTimeslotsAsync(id); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }

        var missing = await db.ProjectSubmissions.Where(s =>
            s.EventId == id &&
            (s.Status == "Accepted" || s.Status == "RoomAssigned" || s.Status == "Scheduled") &&
            (s.AssignedRoomId == null || s.EvaluatorUserId == null || s.EvaluationStart == null))
            .Select(s => s.Id).ToListAsync();
        if (missing.Count > 0)
            return BadRequest(new { message = $"Submissions missing room/evaluator/slot: {string.Join(',', missing)}" });

        ev.IsFinalized = true;
        ev.FinalizedAt = DateTime.UtcNow;
        ev.Status = "Finalized";
        await db.SaveChangesAsync();

        var evaluatorIds = await db.ProjectSubmissions
            .Where(s => s.EventId == id && s.EvaluatorUserId != null)
            .Select(s => s.EvaluatorUserId!.Value)
            .Distinct()
            .ToListAsync();

        await notifications.NotifyManyAsync(evaluatorIds,
            "Event finalized",
            $"Event '{ev.Name}' is locked. Your evaluation timeslots are confirmed.");

        await hub.Clients.Group(DashboardHub.RoleGroup("Evaluator"))
            .SendAsync("EventFinalized", new { eventId = id, name = ev.Name });
        await hub.Clients.Group(DashboardHub.EventGroup(id))
            .SendAsync("EventFinalized", new { eventId = id });

        return Ok(Map(ev));
    }

    private static EventDto Map(Event e) =>
        new(e.Id, e.Name, e.Description, e.StartDate, e.EndDate, e.SlotDurationMinutes, e.Status, e.IsFinalized);
}
