using System.Security.Claims;
using BackendApi.Data;
using BackendApi.Dtos;
using BackendApi.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class EvaluationsController(AppDbContext db) : ControllerBase
{
    private int UserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpPost("submission/{submissionId:int}")]
    [Authorize(Roles = "Evaluator")]
    public async Task<ActionResult<EvaluationDto>> Submit(int submissionId, SubmitEvaluationRequest request)
    {
        var s = await db.ProjectSubmissions.Include(x => x.Event).Include(x => x.ProjectGroup)
            .FirstOrDefaultAsync(x => x.Id == submissionId);
        if (s is null) return NotFound();
        if (!s.Event.IsFinalized)
            return BadRequest(new { message = "Event must be finalized before scoring." });
        if (s.EvaluatorUserId != UserId)
            return Forbid();

        var existing = await db.Evaluations.FirstOrDefaultAsync(e => e.ProjectSubmissionId == submissionId);
        if (existing is null)
        {
            existing = new Evaluation
            {
                ProjectSubmissionId = submissionId,
                EvaluatorUserId = UserId
            };
            db.Evaluations.Add(existing);
        }

        existing.TechnicalDemo = request.TechnicalDemo;
        existing.Presentation = request.Presentation;
        existing.Innovation = request.Innovation;
        existing.Completeness = request.Completeness;
        existing.Qa = request.Qa;
        existing.Thoughts = request.Thoughts;
        existing.SubmittedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        return Ok(new EvaluationDto(
            submissionId, s.ProjectGroup.Title, existing.TechnicalDemo, existing.Presentation,
            existing.Innovation, existing.Completeness, existing.Qa, existing.AverageScore,
            existing.Thoughts, User.FindFirstValue(ClaimTypes.Name) ?? "", existing.SubmittedAt));
    }

    [HttpGet]
    [Authorize(Roles = "Admin,Advisor")]
    public async Task<ActionResult<IEnumerable<EvaluationDto>>> List([FromQuery] int? eventId)
    {
        var role = User.FindFirstValue(ClaimTypes.Role);
        var userId = UserId;
        var q = db.Evaluations
            .Include(e => e.ProjectSubmission).ThenInclude(s => s.ProjectGroup)
            .Include(e => e.EvaluatorUser)
            .AsQueryable();

        if (eventId is not null)
            q = q.Where(e => e.ProjectSubmission.EventId == eventId);

        if (role == "Advisor")
            q = q.Where(e => e.ProjectSubmission.ProjectGroup.AdvisorUserId == userId);

        var list = await q.OrderByDescending(e => e.SubmittedAt).ToListAsync();
        return Ok(list.Select(e => new EvaluationDto(
            e.ProjectSubmissionId, e.ProjectSubmission.ProjectGroup.Title,
            e.TechnicalDemo, e.Presentation, e.Innovation, e.Completeness, e.Qa,
            e.AverageScore, e.Thoughts, e.EvaluatorUser.FullName, e.SubmittedAt)));
    }
}

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class NotificationsController(AppDbContext db) : ControllerBase
{
    private int UserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet]
    public async Task<ActionResult<IEnumerable<NotificationDto>>> Mine()
    {
        var list = await db.Notifications.Where(n => n.UserId == UserId)
            .OrderByDescending(n => n.CreatedAt).Take(50).ToListAsync();
        return Ok(list.Select(n => new NotificationDto(n.Id, n.Title, n.Message, n.IsRead, n.CreatedAt)));
    }

    [HttpPost("{id:int}/read")]
    public async Task<IActionResult> MarkRead(int id)
    {
        var n = await db.Notifications.FirstOrDefaultAsync(x => x.Id == id && x.UserId == UserId);
        if (n is null) return NotFound();
        n.IsRead = true;
        await db.SaveChangesAsync();
        return Ok();
    }

    [HttpPost("read-all")]
    public async Task<IActionResult> MarkAllRead()
    {
        await db.Notifications.Where(n => n.UserId == UserId && !n.IsRead)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.IsRead, true));
        return Ok();
    }

    [HttpDelete("clear")]
    public async Task<IActionResult> ClearAll()
    {
        await db.Notifications.Where(n => n.UserId == UserId)
            .ExecuteDeleteAsync();
        return Ok();
    }

    [HttpGet("email-outbox")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<IEnumerable<EmailOutboxDto>>> Outbox()
    {
        var list = await db.EmailOutbox.OrderByDescending(e => e.CreatedAt).Take(100).ToListAsync();
        return Ok(list.Select(e => new EmailOutboxDto(e.Id, e.ToEmail, e.Subject, e.Body, e.CreatedAt)));
    }
}
