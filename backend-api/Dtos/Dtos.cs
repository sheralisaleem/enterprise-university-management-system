using System.ComponentModel.DataAnnotations;

namespace BackendApi.Dtos;

public record RegisterRequest(
    [Required, EmailAddress] string Email,
    [Required, MinLength(6)] string Password,
    [Required] string FullName,
    [Required] string Role,
    int? DomainId);

public record LoginRequest([Required, EmailAddress] string Email, [Required] string Password);

public record AuthResponse(string Token, int UserId, string Email, string FullName, string Role, int? DomainId, string? DomainName);

public record DomainDto(int Id, string Name, bool IsActive);
public record CreateDomainRequest([Required] string Name);

public record BuildingDto(int Id, string Name, string Code, IEnumerable<FloorDto> Floors);
public record FloorDto(int Id, int BuildingId, string BuildingCode, string Name, int LevelNumber, IEnumerable<RoomDto> Rooms);
public record RoomDto(int Id, int FloorId, string Code, int CapacityGroupsDefault, bool IsActive);
public record CreateBuildingRequest([Required] string Name, [Required] string Code);
public record CreateFloorRequest([Required] string Name, int LevelNumber);
public record CreateRoomRequest(int RoomNumber);

public record CreateEventRequest(
    [Required] string Name,
    string? Description,
    DateTime StartDate,
    DateTime EndDate,
    int SlotDurationMinutes,
    IEnumerable<int> FloorIds,
    IEnumerable<RoomCapOverride>? RoomCapOverrides);

public record RoomCapOverride(int RoomId, int MaxGroups);

public record EventDto(
    int Id, string Name, string? Description, DateTime StartDate, DateTime EndDate,
    int SlotDurationMinutes, string Status, bool IsFinalized);

public record MemberInput([Required] string FullName, [Required, EmailAddress] string Email, string? StudentId);

public record CreateGroupRequest(
    [Required] string Title,
    int DomainId,
    int AdvisorUserId,
    [Required] IEnumerable<MemberInput> Members);

public record GroupDto(int Id, string Title, int DomainId, string DomainName, int AdvisorUserId, string AdvisorName, IEnumerable<MemberDto> Members);
public record MemberDto(int Id, string FullName, string Email, string? StudentId, bool IsLeader);

public record SubmitToEventRequest(int ProjectGroupId);

public record RejectRequest([Required] string Reason);

public record SubmissionDto(
    int Id, int EventId, string EventName, int ProjectGroupId, string ProjectTitle,
    string DomainName, string Status, string? AdvisorRejectReason, string? AdminRejectReason,
    bool StudentAcknowledgedRejection, int? AssignedRoomId, string? RoomCode,
    int? EvaluatorUserId, string? EvaluatorName, DateTime? EvaluationStart, DateTime? EvaluationEnd,
    string AdvisorName, string LeaderName, IEnumerable<MemberDto> Members, IEnumerable<FileDto> Files);

public record FileDto(int Id, string FileName, string FileType, long SizeBytes);

public record AssignRoomRequest(int RoomId);
public record AssignEvaluatorRequest(int EvaluatorUserId);

public record EvaluatorRecommendationDto(int UserId, string FullName, string Email, string? DomainName, bool DomainMatch, int CurrentAssignments);

public record RoomSuggestionDto(int RoomId, string Code, string BuildingName, string FloorName, int MaxGroups, int AssignedCount, int Remaining);

public record SubmitEvaluationRequest(
    [Range(1, 5)] int TechnicalDemo,
    [Range(1, 5)] int Presentation,
    [Range(1, 5)] int Innovation,
    [Range(1, 5)] int Completeness,
    [Range(1, 5)] int Qa,
    string? Thoughts);

public record EvaluationDto(
    int SubmissionId, string ProjectTitle, int TechnicalDemo, int Presentation, int Innovation,
    int Completeness, int Qa, decimal AverageScore, string? Thoughts, string EvaluatorName, DateTime SubmittedAt);

public record NotificationDto(int Id, string Title, string Message, bool IsRead, DateTime CreatedAt);
public record EmailOutboxDto(int Id, string ToEmail, string Subject, string Body, DateTime CreatedAt);
