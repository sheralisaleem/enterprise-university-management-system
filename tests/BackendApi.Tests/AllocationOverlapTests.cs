using BackendApi.Data;
using BackendApi.Dtos;
using BackendApi.Models;
using BackendApi.Services;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Tests;

public class RoomCapAndOverlapTests
{
    private static AppDbContext Db()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new AppDbContext(options);
        db.Roles.AddRange(
            new Role { Id = 1, Name = "Admin" },
            new Role { Id = 3, Name = "Evaluator" },
            new Role { Id = 4, Name = "Student" });
        db.Domains.Add(new Domain { Id = 1, Name = "AI" });
        db.Users.AddRange(
            new User { Id = 1, Email = "a@t", FullName = "Admin", RoleId = 1, PasswordHash = "x" },
            new User { Id = 2, Email = "e@t", FullName = "Eval", RoleId = 3, DomainId = 1, PasswordHash = "x" },
            new User { Id = 3, Email = "s@t", FullName = "Lead", RoleId = 4, PasswordHash = "x" });
        db.Buildings.Add(new Building
        {
            Id = 1, Name = "Block A", Code = "A",
            Floors =
            [
                new Floor
                {
                    Id = 1, Name = "Ground", LevelNumber = 0,
                    Rooms =
                    [
                        new Room { Id = 1, Code = "A-001", CapacityGroupsDefault = 5 },
                        new Room { Id = 2, Code = "A-002", CapacityGroupsDefault = 5 }
                    ]
                }
            ]
        });
        db.Events.Add(new Event
        {
            Id = 1, Name = "OH", StartDate = DateTime.UtcNow.Date.AddHours(9),
            EndDate = DateTime.UtcNow.Date.AddHours(12), SlotDurationMinutes = 15,
            Status = "Open", CreatedByUserId = 1
        });
        db.EventFloors.Add(new EventFloor { EventId = 1, FloorId = 1 });
        db.EventRoomCaps.Add(new EventRoomCap { EventId = 1, RoomId = 1, MaxGroups = 2 }); // hard cap override
        db.EventRoomCaps.Add(new EventRoomCap { EventId = 1, RoomId = 2, MaxGroups = 5 });
        db.ProjectGroups.Add(new ProjectGroup { Id = 1, Title = "G1", DomainId = 1, LeaderUserId = 3, AdvisorUserId = 1 });
        db.ProjectGroups.Add(new ProjectGroup { Id = 2, Title = "G2", DomainId = 1, LeaderUserId = 3, AdvisorUserId = 1 });
        db.ProjectGroups.Add(new ProjectGroup { Id = 3, Title = "G3", DomainId = 1, LeaderUserId = 3, AdvisorUserId = 1 });
        db.ProjectSubmissions.AddRange(
            new ProjectSubmission { Id = 1, EventId = 1, ProjectGroupId = 1, Status = "Accepted" },
            new ProjectSubmission { Id = 2, EventId = 1, ProjectGroupId = 2, Status = "Accepted" },
            new ProjectSubmission { Id = 3, EventId = 1, ProjectGroupId = 3, Status = "Accepted" });
        db.SaveChanges();
        return db;
    }

    [Fact]
    public async Task Hard_cap_blocks_sixth_style_overflow()
    {
        await using var db = Db();
        var rooms = new RoomAssignmentService(db);
        await rooms.AssignRoomAsync(1, 1);
        await rooms.AssignRoomAsync(2, 1);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => rooms.AssignRoomAsync(3, 1));
        Assert.Contains("capacity", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Evaluator_overlap_rejected_when_rebuilding_slots()
    {
        await using var db = Db();
        var rooms = new RoomAssignmentService(db);
        var scheduling = new SchedulingService(db);
        await rooms.AssignRoomAsync(1, 1);
        await rooms.AssignRoomAsync(2, 2);

        var s1 = await db.ProjectSubmissions.FindAsync(1);
        var s2 = await db.ProjectSubmissions.FindAsync(2);
        s1!.EvaluatorUserId = 2;
        s2!.EvaluatorUserId = 2;
        await db.SaveChangesAsync();

        await scheduling.RebuildTimeslotsAsync(1);
        // both scheduled without overlap
        s1 = await db.ProjectSubmissions.FindAsync(1);
        s2 = await db.ProjectSubmissions.FindAsync(2);
        Assert.NotNull(s1!.EvaluationStart);
        Assert.NotNull(s2!.EvaluationStart);
        Assert.False(s1.EvaluationStart < s2.EvaluationEnd && s1.EvaluationEnd > s2.EvaluationStart);
    }
}
