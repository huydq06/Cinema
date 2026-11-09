using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WEB_BTL.Application.Services;
using WEB_BTL.Domain.Constants;
using WEB_BTL.Domain.Entities;
using WEB_BTL.Domain.Enums;
using WEB_BTL.Infrastructure.Data;

namespace WEB_BTL.Controllers;

[Authorize(Policy = Policies.ManageCinema)]
[Route("quan-tri/phong-chieu")]
public class AdminRoomsController(CinemaDbContext db, IAuditService audit) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var rooms = await db.Rooms
            .Include(r => r.Seats)
            .OrderBy(r => r.RoomName)
            .ToListAsync();

        return View(rooms);
    }

    [HttpGet("tao-moi")]
    public IActionResult Create()
    {
        return View(new Room { IsActive = true, RoomType = RoomType.Standard2D });
    }

    [HttpPost("tao-moi")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Room room)
    {
        if (!ModelState.IsValid)
        {
            return View(room);
        }

        db.Rooms.Add(room);
        await db.SaveChangesAsync();

        await audit.LogAsync(User.GetUserId(), "CREATE", "Room", room.RoomId.ToString(),
            new { room.RoomName, room.RoomType });

        TempData["Success"] = $"Đã tạo phòng {room.RoomName}.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet("{id:int}/sua")]
    public async Task<IActionResult> Edit(int id)
    {
        var room = await db.Rooms.FindAsync(id);
        if (room is null)
        {
            return NotFound();
        }

        return View(room);
    }

    [HttpPost("{id:int}/sua")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, Room input)
    {
        var room = await db.Rooms.FindAsync(id);
        if (room is null)
        {
            return NotFound();
        }

        if (!ModelState.IsValid)
        {
            return View(input);
        }

        room.RoomName = input.RoomName;
        room.RoomType = input.RoomType;
        room.IsActive = input.IsActive;

        await db.SaveChangesAsync();

        await audit.LogAsync(User.GetUserId(), "UPDATE", "Room", room.RoomId.ToString(),
            new { room.RoomName, room.RoomType, room.IsActive });

        TempData["Success"] = $"Đã cập nhật phòng {room.RoomName}.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet("{id:int}/ghe")]
    public async Task<IActionResult> Seats(int id)
    {
        var room = await db.Rooms
            .Include(r => r.Seats.OrderBy(s => s.RowIndex).ThenBy(s => s.ColIndex))
            .FirstOrDefaultAsync(r => r.RoomId == id);

        if (room is null)
        {
            return NotFound();
        }

        ViewData["RoomId"] = id;
        ViewData["RoomName"] = room.RoomName;

        return View(room.Seats);
    }

    [HttpPost("{id:int}/ghe/tao-moi")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateSeat(int id, Seat seat)
    {
        var room = await db.Rooms.FindAsync(id);
        if (room is null)
        {
            return NotFound();
        }

        seat.RoomId = id;

        if (!ModelState.IsValid)
        {
            TempData["Error"] = "Dữ liệu không hợp lệ.";
            return RedirectToAction(nameof(Seats), new { id });
        }

        db.Seats.Add(seat);
        await db.SaveChangesAsync();

        await audit.LogAsync(User.GetUserId(), "CREATE", "Seat", seat.SeatId.ToString(),
            new { seat.RoomId, seat.SeatCode, seat.SeatType });

        TempData["Success"] = $"Đã tạo ghế {seat.SeatCode}.";
        return RedirectToAction(nameof(Seats), new { id });
    }

    [HttpPost("{roomId:int}/ghe/{seatId:int}/xoa")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteSeat(int roomId, int seatId)
    {
        var seat = await db.Seats
            .Include(s => s.ShowtimeSeats)
            .FirstOrDefaultAsync(s => s.SeatId == seatId && s.RoomId == roomId);

        if (seat is null)
        {
            return NotFound();
        }

        // Chặn xóa nếu ghế đã được dùng trong suất chiếu
        if (seat.ShowtimeSeats.Any())
        {
            TempData["Error"] = $"Không thể xóa ghế {seat.SeatCode} vì đã được dùng trong lịch chiếu.";
            return RedirectToAction(nameof(Seats), new { id = roomId });
        }

        db.Seats.Remove(seat);
        await db.SaveChangesAsync();

        await audit.LogAsync(User.GetUserId(), "DELETE", "Seat", seat.SeatId.ToString(),
            new { seat.RoomId, seat.SeatCode });

        TempData["Success"] = $"Đã xóa ghế {seat.SeatCode}.";
        return RedirectToAction(nameof(Seats), new { id = roomId });
    }
}
