using BackendApi.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Security.Claims;

namespace BackendApi.Data;

public class AppDbContext : DbContext
{
    private readonly IHttpContextAccessor? _httpContextAccessor;

    public AppDbContext(DbContextOptions<AppDbContext> options, IHttpContextAccessor? httpContextAccessor = null) : base(options)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public DbSet<Role> Roles => Set<Role>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Domain> Domains => Set<Domain>();
    public DbSet<Building> Buildings => Set<Building>();
    public DbSet<Floor> Floors => Set<Floor>();
    public DbSet<Room> Rooms => Set<Room>();
    public DbSet<Event> Events => Set<Event>();
    public DbSet<EventFloor> EventFloors => Set<EventFloor>();
    public DbSet<EventRoomCap> EventRoomCaps => Set<EventRoomCap>();
    public DbSet<ProjectGroup> ProjectGroups => Set<ProjectGroup>();
    public DbSet<GroupMember> GroupMembers => Set<GroupMember>();
    public DbSet<ProjectSubmission> ProjectSubmissions => Set<ProjectSubmission>();
    public DbSet<SubmissionFile> SubmissionFiles => Set<SubmissionFile>();
    public DbSet<Evaluation> Evaluations => Set<Evaluation>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<EmailOutbox> EmailOutbox => Set<EmailOutbox>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Role>().HasIndex(r => r.Name).IsUnique();
        modelBuilder.Entity<User>().HasIndex(u => u.Email).IsUnique();
        modelBuilder.Entity<Domain>().HasIndex(d => d.Name).IsUnique();
        modelBuilder.Entity<Building>().HasIndex(b => b.Code).IsUnique();
        modelBuilder.Entity<Room>().HasIndex(r => r.Code).IsUnique();
        modelBuilder.Entity<Floor>().HasIndex(f => new { f.BuildingId, f.LevelNumber }).IsUnique();
        modelBuilder.Entity<EventFloor>().HasIndex(e => new { e.EventId, e.FloorId }).IsUnique();
        modelBuilder.Entity<EventRoomCap>().HasIndex(e => new { e.EventId, e.RoomId }).IsUnique();
        modelBuilder.Entity<ProjectSubmission>().HasIndex(s => new { s.EventId, s.ProjectGroupId }).IsUnique();
        modelBuilder.Entity<Evaluation>().HasIndex(e => e.ProjectSubmissionId).IsUnique();

        Restrict(modelBuilder);

        modelBuilder.Entity<Role>().HasData(
            new Role { Id = 1, Name = "Admin" },
            new Role { Id = 2, Name = "Advisor" },
            new Role { Id = 3, Name = "Evaluator" },
            new Role { Id = 4, Name = "Student" });

        modelBuilder.Entity<Domain>().HasData(
            new Domain { Id = 1, Name = "AI" },
            new Domain { Id = 2, Name = "Robotics" },
            new Domain { Id = 3, Name = "Web" },
            new Domain { Id = 4, Name = "Health" },
            new Domain { Id = 5, Name = "IoT" },
            new Domain { Id = 6, Name = "Cybersecurity" },
            new Domain { Id = 7, Name = "Data Science" },
            new Domain { Id = 8, Name = "Mobile" });
    }

    private static void Restrict(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>().HasOne(u => u.Role).WithMany(r => r.Users)
            .HasForeignKey(u => u.RoleId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<User>().HasOne(u => u.Domain).WithMany()
            .HasForeignKey(u => u.DomainId).OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<Event>().HasOne(e => e.CreatedByUser).WithMany()
            .HasForeignKey(e => e.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<ProjectGroup>().HasOne(g => g.LeaderUser).WithMany()
            .HasForeignKey(g => g.LeaderUserId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<ProjectGroup>().HasOne(g => g.AdvisorUser).WithMany()
            .HasForeignKey(g => g.AdvisorUserId).OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<ProjectSubmission>().HasOne(s => s.AssignedRoom).WithMany()
            .HasForeignKey(s => s.AssignedRoomId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<ProjectSubmission>().HasOne(s => s.EvaluatorUser).WithMany()
            .HasForeignKey(s => s.EvaluatorUserId).OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Evaluation>().HasOne(e => e.EvaluatorUser).WithMany()
            .HasForeignKey(e => e.EvaluatorUserId).OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Notification>().HasOne(n => n.User).WithMany()
            .HasForeignKey(n => n.UserId).OnDelete(DeleteBehavior.Cascade);
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var userIdString = _httpContextAccessor?.HttpContext?.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        int? currentUserId = int.TryParse(userIdString, out var uid) ? uid : null;
        var now = DateTime.UtcNow;

        var auditEntries = new List<AuditLog>();
        
        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.Entity is IAuditable auditable && (entry.State == EntityState.Added || entry.State == EntityState.Modified))
            {
                auditable.UpdatedAt = now;
                if (currentUserId.HasValue)
                {
                    auditable.ModifiedByUserId = currentUserId;
                }
            }

            if (entry.State == EntityState.Added || entry.State == EntityState.Modified || entry.State == EntityState.Deleted)
            {
                if (entry.Entity is AuditLog) continue; // don't audit the audit logs

                var auditLog = new AuditLog
                {
                    TableName = entry.Metadata.GetTableName() ?? entry.Entity.GetType().Name,
                    UserId = currentUserId,
                    Timestamp = now,
                    Action = entry.State.ToString()
                };

                var primaryKey = entry.Metadata.FindPrimaryKey();
                if (primaryKey != null)
                {
                    var pkValues = primaryKey.Properties.Select(p => entry.Property(p.Name).CurrentValue);
                    auditLog.PrimaryKey = string.Join(",", pkValues);
                }

                if (entry.State == EntityState.Modified || entry.State == EntityState.Deleted)
                {
                    var oldValues = new Dictionary<string, object?>();
                    foreach (var prop in entry.Properties)
                    {
                        if (prop.Metadata.IsPrimaryKey()) continue;
                        oldValues[prop.Metadata.Name] = prop.OriginalValue;
                    }
                    auditLog.OldValues = JsonSerializer.Serialize(oldValues);
                }

                if (entry.State == EntityState.Added || entry.State == EntityState.Modified)
                {
                    var newValues = new Dictionary<string, object?>();
                    foreach (var prop in entry.Properties)
                    {
                        if (prop.Metadata.IsPrimaryKey()) continue;
                        newValues[prop.Metadata.Name] = prop.CurrentValue;
                    }
                    auditLog.NewValues = JsonSerializer.Serialize(newValues);
                }

                auditEntries.Add(auditLog);
            }
        }

        if (auditEntries.Any())
        {
            AuditLogs.AddRange(auditEntries);
        }

        return await base.SaveChangesAsync(cancellationToken);
    }
}
