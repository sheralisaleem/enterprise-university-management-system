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
public class DomainsController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    [AllowAnonymous]
    public async Task<ActionResult<IEnumerable<DomainDto>>> List() =>
        Ok(await db.Domains.Where(d => d.IsActive).OrderBy(d => d.Name)
            .Select(d => new DomainDto(d.Id, d.Name, d.IsActive)).ToListAsync());

    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<DomainDto>> Create(CreateDomainRequest request)
    {
        if (await db.Domains.AnyAsync(d => d.Name == request.Name))
            return Conflict(new { message = "Domain already exists." });
        var d = new Domain { Name = request.Name.Trim() };
        db.Domains.Add(d);
        await db.SaveChangesAsync();
        return Ok(new DomainDto(d.Id, d.Name, d.IsActive));
    }
}

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class LocationsController(AppDbContext db) : ControllerBase
{
    [HttpGet("buildings")]
    public async Task<ActionResult<IEnumerable<BuildingDto>>> Buildings()
    {
        var list = await db.Buildings.Include(b => b.Floors).ThenInclude(f => f.Rooms)
            .OrderBy(b => b.Code).ToListAsync();
        return Ok(list.Select(Map));
    }

    [HttpPost("buildings")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<BuildingDto>> CreateBuilding(CreateBuildingRequest request)
    {
        var b = new Building { Name = request.Name, Code = request.Code.Trim().ToUpperInvariant() };
        db.Buildings.Add(b);
        await db.SaveChangesAsync();
        return Ok(Map(b));
    }

    [HttpPost("buildings/{buildingId:int}/floors")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<FloorDto>> CreateFloor(int buildingId, CreateFloorRequest request)
    {
        var building = await db.Buildings.FindAsync(buildingId);
        if (building is null) return NotFound();
        var floor = new Floor { BuildingId = buildingId, Name = request.Name, LevelNumber = request.LevelNumber };
        db.Floors.Add(floor);
        await db.SaveChangesAsync();
        return Ok(new FloorDto(floor.Id, buildingId, building.Code, floor.Name, floor.LevelNumber, []));
    }

    [HttpPost("floors/{floorId:int}/rooms")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<RoomDto>> CreateRoom(int floorId, CreateRoomRequest request)
    {
        var floor = await db.Floors.Include(f => f.Building).FirstOrDefaultAsync(f => f.Id == floorId);
        if (floor is null) return NotFound();
        var code = $"{floor.Building.Code}-{floor.LevelNumber}{request.RoomNumber:D2}";
        if (await db.Rooms.AnyAsync(r => r.Code == code))
            return Conflict(new { message = $"Room {code} exists." });
        var room = new Room { FloorId = floorId, Code = code, CapacityGroupsDefault = 5 };
        db.Rooms.Add(room);
        await db.SaveChangesAsync();
        return Ok(new RoomDto(room.Id, floorId, room.Code, room.CapacityGroupsDefault, room.IsActive));
    }

    private static BuildingDto Map(Building b) => new(
        b.Id, b.Name, b.Code,
        b.Floors.OrderBy(f => f.LevelNumber).Select(f => new FloorDto(
            f.Id, f.BuildingId, b.Code, f.Name, f.LevelNumber,
            f.Rooms.OrderBy(r => r.Code).Select(r => new RoomDto(r.Id, r.FloorId, r.Code, r.CapacityGroupsDefault, r.IsActive)))));
}
