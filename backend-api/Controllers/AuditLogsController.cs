using BackendApi.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin")]
public class AuditLogsController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetLogs()
    {
        var logs = await db.AuditLogs
            .Include(l => l.User)
            .OrderByDescending(l => l.Timestamp)
            .Take(1000) // Limit to latest 1000 logs for performance
            .Select(l => new
            {
                l.Id,
                l.Action,
                l.TableName,
                l.PrimaryKey,
                l.OldValues,
                l.NewValues,
                l.Timestamp,
                UserName = l.User != null ? l.User.FullName : "System",
                UserEmail = l.User != null ? l.User.Email : ""
            })
            .ToListAsync();
            
        return Ok(logs);
    }
}
