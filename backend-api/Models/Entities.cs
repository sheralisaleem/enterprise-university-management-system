namespace BackendApi.Models;

public interface IAuditable
{
    int? ModifiedByUserId { get; set; }
    User? ModifiedByUser { get; set; }
    DateTime? UpdatedAt { get; set; }
}

public class AuditLog
{
    public int Id { get; set; }
    public int? UserId { get; set; }
    public User? User { get; set; }
    public string Action { get; set; } = string.Empty; // Create, Update, Delete
    public string TableName { get; set; } = string.Empty;
    public string PrimaryKey { get; set; } = string.Empty;
    public string? OldValues { get; set; } // JSON
    public string? NewValues { get; set; } // JSON
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

public class Role
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public ICollection<User> Users { get; set; } = new List<User>();
}

public class User
{
    public int Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public int RoleId { get; set; }
    public Role Role { get; set; } = null!;
    public int? DomainId { get; set; }
    public Domain? Domain { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class Domain
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}

public class Building
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty; // A, B, C
    public ICollection<Floor> Floors { get; set; } = new List<Floor>();
}

public class Floor
{
    public int Id { get; set; }
    public int BuildingId { get; set; }
    public Building Building { get; set; } = null!;
    public string Name { get; set; } = string.Empty;
    public int LevelNumber { get; set; } // 0..3 → codes like A-001, A-101
    public ICollection<Room> Rooms { get; set; } = new List<Room>();
}

public class Room
{
    public int Id { get; set; }
    public int FloorId { get; set; }
    public Floor Floor { get; set; } = null!;
    public string Code { get; set; } = string.Empty; // A-201
    public int CapacityGroupsDefault { get; set; } = 5;
    public bool IsActive { get; set; } = true;
}

public class Event : IAuditable
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public int SlotDurationMinutes { get; set; } = 15;
    public string Status { get; set; } = "Draft"; // Draft, Open, Finalized, Closed
    public bool IsFinalized { get; set; }
    public DateTime? FinalizedAt { get; set; }
    public int CreatedByUserId { get; set; }
    public User CreatedByUser { get; set; } = null!;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public int? ModifiedByUserId { get; set; }
    public User? ModifiedByUser { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public ICollection<EventFloor> EventFloors { get; set; } = new List<EventFloor>();
    public ICollection<EventRoomCap> RoomCaps { get; set; } = new List<EventRoomCap>();
    public ICollection<ProjectSubmission> Submissions { get; set; } = new List<ProjectSubmission>();
}

public class EventFloor
{
    public int Id { get; set; }
    public int EventId { get; set; }
    public Event Event { get; set; } = null!;
    public int FloorId { get; set; }
    public Floor Floor { get; set; } = null!;
}

public class EventRoomCap
{
    public int Id { get; set; }
    public int EventId { get; set; }
    public Event Event { get; set; } = null!;
    public int RoomId { get; set; }
    public Room Room { get; set; } = null!;
    public int MaxGroups { get; set; } = 5; // hard cap, overridable at event creation
}

public class ProjectGroup : IAuditable
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public int DomainId { get; set; }
    public Domain Domain { get; set; } = null!;
    public int LeaderUserId { get; set; }
    public User LeaderUser { get; set; } = null!;
    public int AdvisorUserId { get; set; }
    public User AdvisorUser { get; set; } = null!;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public int? ModifiedByUserId { get; set; }
    public User? ModifiedByUser { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public ICollection<GroupMember> Members { get; set; } = new List<GroupMember>();
    public ICollection<ProjectSubmission> Submissions { get; set; } = new List<ProjectSubmission>();
}

public class GroupMember
{
    public int Id { get; set; }
    public int ProjectGroupId { get; set; }
    public ProjectGroup ProjectGroup { get; set; } = null!;
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? StudentId { get; set; }
    public bool IsLeader { get; set; }
}

public class ProjectSubmission : IAuditable
{
    public int Id { get; set; }
    public int EventId { get; set; }
    public Event Event { get; set; } = null!;
    public int ProjectGroupId { get; set; }
    public ProjectGroup ProjectGroup { get; set; } = null!;
    public string Status { get; set; } = "Draft";
    // Draft, Submitted, AdvisorRejected, AdvisorApproved, AdminRejected, Accepted, RoomAssigned, Scheduled
    public string? AdvisorRejectReason { get; set; }
    public string? AdminRejectReason { get; set; }
    public bool StudentAcknowledgedRejection { get; set; }
    public int? AssignedRoomId { get; set; }
    public Room? AssignedRoom { get; set; }
    public int? EvaluatorUserId { get; set; }
    public User? EvaluatorUser { get; set; }
    public DateTime? EvaluationStart { get; set; }
    public DateTime? EvaluationEnd { get; set; }
    public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    DateTime? IAuditable.UpdatedAt { get => UpdatedAt; set => UpdatedAt = value ?? DateTime.UtcNow; }
    public int? ModifiedByUserId { get; set; }
    public User? ModifiedByUser { get; set; }
    public ICollection<SubmissionFile> Files { get; set; } = new List<SubmissionFile>();
    public Evaluation? Evaluation { get; set; }
}

public class SubmissionFile
{
    public int Id { get; set; }
    public int ProjectSubmissionId { get; set; }
    public ProjectSubmission ProjectSubmission { get; set; } = null!;
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string FileType { get; set; } = string.Empty; // Document, Banner, Source, Presentation
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
}

public class Evaluation : IAuditable
{
    public int Id { get; set; }
    public int ProjectSubmissionId { get; set; }
    public ProjectSubmission ProjectSubmission { get; set; } = null!;
    public int EvaluatorUserId { get; set; }
    public User EvaluatorUser { get; set; } = null!;
    public int TechnicalDemo { get; set; } // 1-5
    public int Presentation { get; set; }
    public int Innovation { get; set; }
    public int Completeness { get; set; }
    public int Qa { get; set; }
    public string? Thoughts { get; set; }
    public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;
    public int? ModifiedByUserId { get; set; }
    public User? ModifiedByUser { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public decimal AverageScore =>
        Math.Round((TechnicalDemo + Presentation + Innovation + Completeness + Qa) / 5m, 2);
}

public class Notification
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class EmailOutbox
{
    public int Id { get; set; }
    public string ToEmail { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
