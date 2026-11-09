using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using WEB_BTL.Application.Services;
using WEB_BTL.Domain.Constants;
using WEB_BTL.Infrastructure.Data;

namespace WEB_BTL.Controllers;

/// <summary>Form nhập giờ theo giờ Việt Nam; controller đổi sang UTC trước khi gọi service.</summary>
public class ShowtimeFormInput : ShowtimeInput
{
    [Required(ErrorMessage = "Chọn giờ bắt đầu.")]
    public DateTime StartAtLocal { get; set; }
}

[Route("quan-tri/suat-chieu")]
[Authorize(Policy = Policies.ManageCinema)]
public class AdminShowtimesController(
    IShowtimeService showtimes,
    CinemaDbContext db) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(DateOnly? date, CancellationToken ct)
    {
        var day = date ?? DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));
        ViewData["Date"] = day.ToString("yyyy-MM-dd");
        return View(await showtimes.ListAsync(day, ct));
    }

    [HttpGet("tao")]
    public async Task<IActionResult> Create(CancellationToken ct)
    {
        await LoadListsAsync(ct);
        return View(new ShowtimeFormInput
        {
            StartAtLocal = DateTime.UtcNow.AddHours(7).AddHours(2),
            BasePrice = 70_000m,
            CleaningMinutes = 15,
            PublishImmediately = true
        });
    }

    [HttpPost("tao")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(ShowtimeFormInput form, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            await LoadListsAsync(ct);
            return View(form);
        }

        var result = await showtimes.CreateAsync(ToInput(form), User.GetUserId(), ct);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "Không tạo được suất chiếu.");
            await LoadListsAsync(ct);
            return View(form);
        }

        TempData["Success"] = "Đã tạo suất chiếu.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet("{id:int}/sua")]
    public async Task<IActionResult> Edit(int id, CancellationToken ct)
    {
        var showtime = await showtimes.GetEntityAsync(id, ct);
        if (showtime is null)
        {
            return NotFound();
        }

        await LoadListsAsync(ct);
        ViewData["ShowtimeId"] = id;
        return View(new ShowtimeFormInput
        {
            MovieId = showtime.MovieId,
            RoomId = showtime.RoomId,
            StartAtLocal = showtime.StartAtUtc.AddHours(7),
            BasePrice = showtime.BasePrice,
            CleaningMinutes = showtime.CleaningMinutes,
            PublishImmediately = showtime.Status != Domain.Enums.ShowtimeStatus.Draft,
            RowVersion = showtime.RowVersion
        });
    }

    [HttpPost("{id:int}/sua")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, ShowtimeFormInput form, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            await LoadListsAsync(ct);
            return View(form);
        }

        var result = await showtimes.UpdateAsync(id, ToInput(form), User.GetUserId(), ct);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "Không lưu được suất chiếu.");
            await LoadListsAsync(ct);
            return View(form);
        }

        TempData["Success"] = "Đã cập nhật suất chiếu.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("{id:int}/publish")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Publish(int id, CancellationToken ct)
    {
        var result = await showtimes.PublishAsync(id, User.GetUserId(), ct);
        TempData[result.Succeeded ? "Success" : "Error"] =
            result.Succeeded ? "Đã mở bán suất chiếu." : result.ErrorMessage;
        return RedirectToAction(nameof(Index));
    }

    [HttpGet("{id:int}/huy")]
    public async Task<IActionResult> Cancel(int id, CancellationToken ct)
    {
        var showtime = await showtimes.GetEntityAsync(id, ct);
        if (showtime is null)
        {
            return NotFound();
        }

        return View(showtime);
    }

    [HttpPost("{id:int}/huy")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(int id, string reason, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            ModelState.AddModelError(nameof(reason), "Nhập lý do hủy để lưu vết.");
            var showtime = await showtimes.GetEntityAsync(id, ct);
            return showtime is null ? NotFound() : View(showtime);
        }

        var result = await showtimes.CancelAsync(id, reason.Trim(), User.GetUserId(), ct);
        TempData[result.Succeeded ? "Success" : "Error"] =
            result.Succeeded ? "Đã hủy suất và tạo yêu cầu hoàn cho giao dịch đã thu." : result.ErrorMessage;
        return RedirectToAction(nameof(Index));
    }

    [HttpGet("{id:int}/ghe")]
    public async Task<IActionResult> Seats(int id, CancellationToken ct)
    {
        var showtime = await showtimes.GetEntityAsync(id, ct);
        if (showtime is null)
        {
            return NotFound();
        }

        var seats = await db.ShowtimeSeats.AsNoTracking()
            .Where(s => s.ShowtimeId == id)
            .OrderBy(s => s.RowIndexSnapshot).ThenBy(s => s.ColIndexSnapshot)
            .ToListAsync(ct);

        ViewData["Showtime"] = showtime;
        return View(seats);
    }

    /// <summary>Chặn/mở lại ghế bảo trì. Không tác động ghế Held/Sold (service chặn).</summary>
    [HttpPost("{id:int}/ghe")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Seats(
        int id, int[] seatIds, string action, CancellationToken ct)
    {
        var block = action == "block";
        var result = await showtimes.BlockSeatsAsync(id, seatIds ?? [], block, User.GetUserId(), ct);
        TempData[result.Succeeded ? "Success" : "Error"] =
            result.Succeeded
                ? (block ? "Đã chặn ghế đã chọn." : "Đã mở lại ghế đã chọn.")
                : result.ErrorMessage;
        return RedirectToAction(nameof(Seats), new { id });
    }

    private async Task LoadListsAsync(CancellationToken ct)
    {
        ViewData["Movies"] = new SelectList(
            await db.Movies.AsNoTracking()
                .Where(m => m.PublicationStatus != Domain.Enums.MoviePublicationStatus.Archived)
                .OrderBy(m => m.Title)
                .Select(m => new { m.MovieId, m.Title })
                .ToListAsync(ct),
            "MovieId", "Title");

        ViewData["Rooms"] = new SelectList(
            await db.Rooms.AsNoTracking()
                .Where(r => r.IsActive)
                .OrderBy(r => r.RoomName)
                .Select(r => new { r.RoomId, r.RoomName })
                .ToListAsync(ct),
            "RoomId", "RoomName");
    }

    private static ShowtimeInput ToInput(ShowtimeFormInput form) => new()
    {
        MovieId = form.MovieId,
        RoomId = form.RoomId,
        // Giờ nhập là giờ Việt Nam; DB yêu cầu UTC.
        StartAtUtc = DateTime.SpecifyKind(form.StartAtLocal.AddHours(-7), DateTimeKind.Utc),
        CleaningMinutes = form.CleaningMinutes,
        BasePrice = form.BasePrice,
        PublishImmediately = form.PublishImmediately,
        RowVersion = form.RowVersion
    };
}
