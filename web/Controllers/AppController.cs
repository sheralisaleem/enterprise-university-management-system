using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

namespace WebDashboard.Controllers;

public class AppController(IHttpClientFactory httpClientFactory) : Controller
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    [HttpGet]
    public async Task<IActionResult> DownloadAllFiles(int submissionId)
    {
        if (!LoggedIn) return RedirectToAction(nameof(Login));
        var res = await Api().GetAsync($"api/submissions/{submissionId}/download-all");
        if (res.IsSuccessStatusCode)
        {
            var stream = await res.Content.ReadAsStreamAsync();
            var fileName = res.Content.Headers.ContentDisposition?.FileNameStar ?? res.Content.Headers.ContentDisposition?.FileName ?? "Files.zip";
            fileName = fileName.Trim('"');
            return File(stream, "application/zip", fileName);
        }
        TempData["Error"] = "Failed to download files. " + await res.Content.ReadAsStringAsync();
        return RedirectToAction(nameof(Submissions));
    }

    private HttpClient Api()
    {
        var client = httpClientFactory.CreateClient("Api");
        var token = HttpContext.Session.GetString("Jwt");
        if (!string.IsNullOrEmpty(token))
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private bool LoggedIn => !string.IsNullOrEmpty(HttpContext.Session.GetString("Jwt"));
    private string Role => HttpContext.Session.GetString("Role") ?? "";
    private string Name => HttpContext.Session.GetString("Name") ?? "";

    [HttpGet]
    public IActionResult Login() => View();

    [HttpPost]
    public async Task<IActionResult> Login(string email, string password)
    {
        var client = httpClientFactory.CreateClient("Api");
        var response = await client.PostAsJsonAsync("api/auth/login", new { email, password });
        if (!response.IsSuccessStatusCode)
        {
            ViewBag.Error = "Login failed.";
            return View();
        }
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        HttpContext.Session.SetString("Jwt", root.GetProperty("token").GetString()!);
        HttpContext.Session.SetString("Role", root.GetProperty("role").GetString()!);
        HttpContext.Session.SetString("Name", root.GetProperty("fullName").GetString()!);
        return RedirectToAction(nameof(Home));
    }

    public IActionResult Logout()
    {
        HttpContext.Session.Clear();
        return RedirectToAction(nameof(Login));
    }

    public override async Task OnActionExecutionAsync(Microsoft.AspNetCore.Mvc.Filters.ActionExecutingContext context, Microsoft.AspNetCore.Mvc.Filters.ActionExecutionDelegate next)
    {
        if (LoggedIn)
        {
            ViewBag.Role = Role;
            ViewBag.Name = Name;
            if (context.HttpContext.Request.Method == "GET")
            {
                ViewBag.Notifications = await Api().GetFromJsonAsync<List<NotificationVm>>("api/notifications", JsonOpts) ?? [];
            }
        }
        await base.OnActionExecutionAsync(context, next);
    }

    public async Task<IActionResult> Home()
    {
        if (!LoggedIn) return RedirectToAction(nameof(Login));
        var client = Api();
        ViewBag.Summary = await client.GetFromJsonAsync<Dictionary<string, int>>("api/dashboard/summary", JsonOpts) ?? new();
        return View();
    }

    public async Task<IActionResult> Events()
    {
        if (!LoggedIn || Role is not ("Admin" or "Student")) return RedirectToAction(nameof(Home));
        var client = Api();
        ViewBag.Events = await client.GetFromJsonAsync<List<EventVm>>("api/events", JsonOpts) ?? [];
        ViewBag.Groups = await client.GetFromJsonAsync<List<GroupVm>>("api/groups", JsonOpts) ?? [];
        if (Role == "Admin") {
            ViewBag.Buildings = await client.GetFromJsonAsync<List<BuildingVm>>("api/locations/buildings", JsonOpts) ?? [];
        }
        return View();
    }

    public async Task<IActionResult> Settings()
    {
        if (!LoggedIn || Role != "Admin") return RedirectToAction(nameof(Home));
        var client = Api();
        ViewBag.Domains = await client.GetFromJsonAsync<List<DomainVm>>("api/domains", JsonOpts) ?? [];
        ViewBag.Outbox = await client.GetFromJsonAsync<List<EmailVm>>("api/notifications/email-outbox", JsonOpts) ?? [];
        return View();
    }

    public async Task<IActionResult> AuditLogs()
    {
        if (!LoggedIn || Role != "Admin") return RedirectToAction(nameof(Home));
        var client = Api();
        var logs = await client.GetFromJsonAsync<List<AuditLogVm>>("api/auditlogs", JsonOpts) ?? [];
        return View(logs);
    }

    public async Task<IActionResult> Submissions()
    {
        if (!LoggedIn) return RedirectToAction(nameof(Login));
        var client = Api();
        ViewBag.Submissions = await client.GetFromJsonAsync<List<SubmissionVm>>("api/submissions", JsonOpts) ?? [];
        return View();
    }

    public async Task<IActionResult> Groups()
    {
        if (!LoggedIn || Role != "Student") return RedirectToAction(nameof(Home));
        var client = Api();
        ViewBag.Groups = await client.GetFromJsonAsync<List<GroupVm>>("api/groups", JsonOpts) ?? [];
        ViewBag.Domains = await client.GetFromJsonAsync<List<DomainVm>>("api/domains", JsonOpts) ?? [];
        ViewBag.Advisors = await client.GetFromJsonAsync<List<AdvisorVm>>("api/groups/advisors", JsonOpts) ?? [];
        ViewBag.Events = (await client.GetFromJsonAsync<List<EventVm>>("api/events", JsonOpts) ?? []).Where(e => (e.Status == "Open" || e.Status == "Draft") && !e.IsFinalized).ToList();
        return View();
    }

    public async Task<IActionResult> Scores()
    {
        if (!LoggedIn || Role is not ("Admin" or "Advisor")) return RedirectToAction(nameof(Home));
        var client = Api();
        ViewBag.Evaluations = await client.GetFromJsonAsync<List<EvaluationVm>>("api/evaluations", JsonOpts) ?? [];
        return View();
    }

    [HttpPost]
    public async Task<IActionResult> CreateEvent(string name, DateTime startDate, DateTime endDate, int slotDurationMinutes, int[]? floorIds)
    {
        var ids = (floorIds ?? []).Where(id => id > 0).Distinct().ToList();
        if (ids.Count == 0)
        {
            TempData["Error"] = "Select at least one floor for the event.";
            return RedirectToAction(nameof(Events));
        }

        await Api().PostAsJsonAsync("api/events", new
        {
            name,
            description = "",
            startDate,
            endDate,
            slotDurationMinutes = slotDurationMinutes <= 0 ? 15 : slotDurationMinutes,
            floorIds = ids,
            roomCapOverrides = Array.Empty<object>()
        });
        TempData["Success"] = "Event created successfully.";
        return RedirectToAction(nameof(Events));
    }

    [HttpPost]
    public async Task<IActionResult> MarkNotificationsRead()
    {
        await Api().PostAsync("api/notifications/read-all", null);
        return Ok();
    }

    [HttpPost]
    public async Task<IActionResult> ClearNotifications()
    {
        await Api().DeleteAsync("api/notifications/clear");
        return Redirect(Request.Headers["Referer"].ToString() ?? "/App/Home");
    }

    [HttpPost]
    public async Task<IActionResult> CreateGroup(
        string title, int domainId, int advisorUserId, int eventId,
        string member1Name, string member1Email, string member1RegId,
        string member2Name, string member2Email, string member2RegId, 
        string member3Name, string member3Email, string member3RegId)
    {
        var members = new List<object>();
        if (!string.IsNullOrWhiteSpace(member1Name)) members.Add(new { fullName = member1Name, email = member1Email, studentId = member1RegId });
        if (!string.IsNullOrWhiteSpace(member2Name)) members.Add(new { fullName = member2Name, email = member2Email, studentId = member2RegId });
        if (!string.IsNullOrWhiteSpace(member3Name)) members.Add(new { fullName = member3Name, email = member3Email, studentId = member3RegId });
        
        var res = await Api().PostAsJsonAsync("api/groups", new { title, domainId, advisorUserId, members });
        if (res.IsSuccessStatusCode)
        {
            var groupDto = await res.Content.ReadFromJsonAsync<GroupVm>(JsonOpts);
            if (groupDto != null && eventId > 0)
            {
                var submitRes = await Api().PostAsJsonAsync($"api/submissions/event/{eventId}", new { projectGroupId = groupDto.Id });
                if (submitRes.IsSuccessStatusCode)
                {
                    TempData["Success"] = "Group created and submitted successfully.";
                    return RedirectToAction(nameof(Submissions));
                }
                else
                {
                    TempData["Error"] = "Group created, but failed to submit. " + await submitRes.Content.ReadAsStringAsync();
                }
            }
            else 
            {
                TempData["Success"] = "Group created successfully.";
            }
        }
        else
        {
            TempData["Error"] = "Failed to create group. " + await res.Content.ReadAsStringAsync();
        }
        return RedirectToAction(nameof(Groups));
    }

    [HttpPost]
    public async Task<IActionResult> Submit(int eventId, int projectGroupId)
    {
        var res = await Api().PostAsJsonAsync($"api/submissions/event/{eventId}", new { projectGroupId });
        if (res.IsSuccessStatusCode)
        {
            TempData["Success"] = "Project submitted to event successfully.";
        }
        else
        {
            TempData["Error"] = "Failed to submit project. " + await res.Content.ReadAsStringAsync();
        }
        return Redirect(Request.Headers["Referer"].ToString() ?? "/App/Home");
    }

    [HttpPost]
    public async Task<IActionResult> Upload(int submissionId, string fileType, IFormFile file)
    {
        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(fileType), "fileType");
        await using var stream = file.OpenReadStream();
        var streamContent = new StreamContent(stream);
        streamContent.Headers.ContentType = new MediaTypeHeaderValue(file.ContentType ?? "application/octet-stream");
        content.Add(streamContent, "file", file.FileName);
        await Api().PostAsync($"api/submissions/{submissionId}/files", content);
        TempData["Success"] = "File uploaded successfully.";
        return Redirect(Request.Headers["Referer"].ToString() ?? "/App/Home");
    }

    [HttpPost]
    public async Task<IActionResult> Acknowledge(int submissionId)
    {
        await Api().PostAsync($"api/submissions/{submissionId}/acknowledge-rejection", null);
        TempData["Success"] = "Rejection acknowledged.";
        return Redirect(Request.Headers["Referer"].ToString() ?? "/App/Home");
    }

    [HttpPost]
    public async Task<IActionResult> AdvisorApprove(int submissionId)
    {
        await Api().PostAsync($"api/submissions/{submissionId}/advisor-approve", null);
        TempData["Success"] = "Submission approved.";
        return Redirect(Request.Headers["Referer"].ToString() ?? "/App/Home");
    }

    [HttpPost]
    public async Task<IActionResult> AdvisorReject(int submissionId, string reason)
    {
        await Api().PostAsJsonAsync($"api/submissions/{submissionId}/advisor-reject", new { reason });
        TempData["Success"] = "Submission rejected.";
        return Redirect(Request.Headers["Referer"].ToString() ?? "/App/Home");
    }

    [HttpPost]
    public async Task<IActionResult> AdminAccept(int submissionId)
    {
        await Api().PostAsync($"api/submissions/{submissionId}/admin-accept", null);
        TempData["Success"] = "Submission accepted by admin.";
        return Redirect(Request.Headers["Referer"].ToString() ?? "/App/Home");
    }

    [HttpPost]
    public async Task<IActionResult> AdminReject(int submissionId, string reason)
    {
        await Api().PostAsJsonAsync($"api/submissions/{submissionId}/admin-reject", new { reason });
        TempData["Success"] = "Submission rejected by admin.";
        return Redirect(Request.Headers["Referer"].ToString() ?? "/App/Home");
    }

    [HttpPost]
    public async Task<IActionResult> AssignRoom(int submissionId, int roomId)
    {
        await Api().PostAsJsonAsync($"api/submissions/{submissionId}/assign-room", new { roomId });
        TempData["Success"] = "Room assigned successfully.";
        return Redirect(Request.Headers["Referer"].ToString() ?? "/App/Home");
    }

    [HttpPost]
    public async Task<IActionResult> AssignEvaluator(int submissionId, int evaluatorUserId)
    {
        await Api().PostAsJsonAsync($"api/submissions/{submissionId}/assign-evaluator", new { evaluatorUserId });
        TempData["Success"] = "Evaluator assigned successfully.";
        return Redirect(Request.Headers["Referer"].ToString() ?? "/App/Home");
    }

    [HttpPost]
    public async Task<IActionResult> Finalize(int eventId)
    {
        await Api().PostAsync($"api/events/{eventId}/finalize", null);
        TempData["Success"] = "Event finalized. Notifications sent.";
        return Redirect(Request.Headers["Referer"].ToString() ?? "/App/Home");
    }

    [HttpPost]
    public async Task<IActionResult> Score(int submissionId, int technicalDemo, int presentation, int innovation, int completeness, int qa, string? thoughts)
    {
        await Api().PostAsJsonAsync($"api/evaluations/submission/{submissionId}", new
        {
            technicalDemo, presentation, innovation, completeness, qa, thoughts
        });
        TempData["Success"] = "Scores submitted successfully.";
        return RedirectToAction(nameof(Home));
    }

    [HttpPost]
    public async Task<IActionResult> AddDomain(string name)
    {
        var res = await Api().PostAsJsonAsync("api/domains", new { name });
        if (res.IsSuccessStatusCode) TempData["Success"] = "Domain added.";
        else TempData["Error"] = "Failed to add domain.";
        return RedirectToAction(nameof(Settings));
    }

    public async Task<IActionResult> ManageSubmission(int id)
    {
        if (!LoggedIn || Role != "Admin") return RedirectToAction(nameof(Home));
        var client = Api();
        var submissions = await client.GetFromJsonAsync<List<SubmissionVm>>("api/submissions", JsonOpts) ?? [];
        var sub = submissions.FirstOrDefault(s => s.Id == id);
        if (sub is null) return RedirectToAction(nameof(Home));
        ViewBag.Submission = sub;
        ViewBag.Rooms = await client.GetFromJsonAsync<List<RoomSuggestionVm>>($"api/submissions/event/{sub.EventId}/room-suggestions", JsonOpts) ?? [];
        ViewBag.Evals = await client.GetFromJsonAsync<List<EvalRecVm>>($"api/submissions/{id}/evaluator-recommendations", JsonOpts) ?? [];
        return View();
    }
}

public record EventVm(int Id, string Name, string? Description, DateTime StartDate, DateTime EndDate, int SlotDurationMinutes, string Status, bool IsFinalized);
public record FileVm(int Id, string FileName, string FileType, long SizeBytes);
public record SubmissionVm(int Id, int EventId, string EventName, int ProjectGroupId, string ProjectTitle, string DomainName, string Status, string? AdvisorRejectReason, string? AdminRejectReason, bool StudentAcknowledgedRejection, int? AssignedRoomId, string? RoomCode, int? EvaluatorUserId, string? EvaluatorName, DateTime? EvaluationStart, DateTime? EvaluationEnd, string AdvisorName, string LeaderName, List<MemberVm> Members, List<FileVm> Files);
public record NotificationVm(int Id, string Title, string Message, bool IsRead, DateTime CreatedAt);
public record DomainVm(int Id, string Name, bool IsActive);
public record GroupVm(int Id, string Title, int DomainId, string DomainName, int AdvisorUserId, string AdvisorName, List<MemberVm> Members);
public record MemberVm(int Id, string FullName, string Email, string? StudentId, bool IsLeader);
public record AdvisorVm(int Id, string FullName, string Email, string Domain);
public record EvaluationVm(int SubmissionId, string ProjectTitle, int TechnicalDemo, int Presentation, int Innovation, int Completeness, int Qa, decimal AverageScore, string? Thoughts, string EvaluatorName, DateTime SubmittedAt);
public record BuildingVm(int Id, string Name, string Code, List<FloorVm> Floors);
public record FloorVm(int Id, int BuildingId, string BuildingCode, string Name, int LevelNumber, List<RoomVm> Rooms);
public record RoomVm(int Id, int FloorId, string Code, int CapacityGroupsDefault, bool IsActive);
public record EmailVm(int Id, string ToEmail, string Subject, string Body, DateTime CreatedAt);
public record RoomSuggestionVm(int RoomId, string Code, string BuildingName, string FloorName, int MaxGroups, int AssignedCount, int Remaining);
public record EvalRecVm(int UserId, string FullName, string Email, string? DomainName, bool DomainMatch, int CurrentAssignments);
public record AuditLogVm(int Id, string Action, string TableName, string PrimaryKey, string? OldValues, string? NewValues, DateTime Timestamp, string UserName, string UserEmail);
