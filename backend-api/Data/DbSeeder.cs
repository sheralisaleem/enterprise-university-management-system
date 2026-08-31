using BackendApi.Data;
using BackendApi.Models;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Data;

public static class DbSeeder
{
    public static async Task SeedAsync(AppDbContext db)
    {
        if (!await db.Buildings.AnyAsync())
        {
            foreach (var code in new[] { "A", "B", "C" })
            {
                var building = new Building { Name = $"Block {code}", Code = code };
                for (var level = 0; level < 4; level++)
                {
                    var floor = new Floor
                    {
                        Name = level == 0 ? "Ground Floor" : $"Floor {level}",
                        LevelNumber = level
                    };
                    for (var n = 1; n <= 20; n++)
                    {
                        floor.Rooms.Add(new Room
                        {
                            Code = $"{code}-{level}{n:D2}",
                            CapacityGroupsDefault = 5
                        });
                    }
                    building.Floors.Add(floor);
                }
                db.Buildings.Add(building);
            }
            await db.SaveChangesAsync();
        }

        if (!await db.Users.AnyAsync())
        {
            string Hash(string p) => BCrypt.Net.BCrypt.HashPassword(p);
            db.Users.AddRange(
                new User { Email = "admin@fyp.local", FullName = "Super Admin", RoleId = 1, PasswordHash = Hash("Admin@123") },
                new User { Email = "advisor.ai@fyp.local", FullName = "Dr. AI Advisor", RoleId = 2, DomainId = 1, PasswordHash = Hash("Advisor@123") },
                new User { Email = "advisor.web@fyp.local", FullName = "Dr. Web Advisor", RoleId = 2, DomainId = 3, PasswordHash = Hash("Advisor@123") },
                new User { Email = "eval.ai@fyp.local", FullName = "Eval AI", RoleId = 3, DomainId = 1, PasswordHash = Hash("Eval@123") },
                new User { Email = "eval.robotics@fyp.local", FullName = "Eval Robotics", RoleId = 3, DomainId = 2, PasswordHash = Hash("Eval@123") },
                new User { Email = "eval.web@fyp.local", FullName = "Eval Web", RoleId = 3, DomainId = 3, PasswordHash = Hash("Eval@123") },
                new User { Email = "eval.health@fyp.local", FullName = "Eval Health", RoleId = 3, DomainId = 4, PasswordHash = Hash("Eval@123") },
                new User { Email = "student@fyp.local", FullName = "Group Leader", RoleId = 4, PasswordHash = Hash("Student@123") }
            );
            await db.SaveChangesAsync();
        }
    }
}
