using System.Security.Claims;
using BackendApi.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class DashboardController : ControllerBase
{
    private readonly AppDbContext db;

    public DashboardController(AppDbContext db) => this.db = db;

    private int UserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private string Role => User.FindFirstValue(ClaimTypes.Role)!;

    [HttpGet("summary")]
    public async Task<IActionResult> GetSummary()
    {
        var role = Role;
        var userId = UserId;
        
        var summary = new Dictionary<string, int>();

        if (role == "Admin")
        {
            summary["ActiveEvents"] = await db.Events.CountAsync(e => !e.IsFinalized);
            summary["TotalGroups"] = await db.ProjectGroups.CountAsync();
            summary["TotalSubmissions"] = await db.ProjectSubmissions.CountAsync();
            summary["UnassignedGroups"] = await db.ProjectSubmissions.CountAsync(s => s.Status != "Submitted" && s.Status != "AdvisorRejected" && s.Status != "AdminRejected" && (s.AssignedRoomId == null || s.EvaluatorUserId == null));
            summary["ActiveDomains"] = await db.Domains.CountAsync(d => d.IsActive);
        }
        else if (role == "Advisor")
        {
            summary["GroupsAdvising"] = await db.ProjectGroups.CountAsync(g => g.AdvisorUserId == userId);
            summary["PendingApprovals"] = await db.ProjectSubmissions.CountAsync(s => s.ProjectGroup.AdvisorUserId == userId && s.Status == "Submitted");
        }
        else if (role == "Evaluator")
        {
            summary["AssignedSubmissions"] = await db.ProjectSubmissions.CountAsync(s => s.EvaluatorUserId == userId);
            summary["PendingEvaluations"] = await db.ProjectSubmissions.CountAsync(s => s.EvaluatorUserId == userId && s.EvaluationStart != null && s.EvaluationEnd == null);
        }
        else if (role == "Student")
        {
            var studentEmail = await db.Users.Where(u => u.Id == userId).Select(u => u.Email).FirstOrDefaultAsync() ?? "";
            summary["ActiveGroups"] = await db.ProjectGroups.CountAsync(g => g.LeaderUserId == userId || g.Members.Any(m => m.StudentId == studentEmail)); // Fallback, let's just count LeaderUserId for simplicity, or Groups they are in
            summary["MyGroups"] = await db.ProjectGroups.CountAsync(g => g.LeaderUserId == userId);
            summary["MySubmissions"] = await db.ProjectSubmissions.CountAsync(s => s.ProjectGroup.LeaderUserId == userId);
        }

        return Ok(summary);
    }
}
