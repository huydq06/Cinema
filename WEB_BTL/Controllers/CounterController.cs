using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WEB_BTL.Application.DTOs;
using WEB_BTL.Application.Services;
using WEB_BTL.Domain.Constants;

namespace WEB_BTL.Controllers;

/// <summary>Quầy bán vé tại rạp: chọn suất, giữ ghế (kênh POS), thu tiền mặt.</summary>
[Route("quay")]
[Authorize(Policy = Policies.OperateCinema)]
public class CounterController(
    IBookingService bookings,
    IPaymentService payments,
    IShowtimeService showtimes) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(DateOnly? date, CancellationToken ct)
    {
        var day = date ?? DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));
        var list = await showtimes.ListAsync(day, ct);

        ViewData["Date"] = day.ToString("yyyy-MM-dd");
        return View(list);
    }

    [HttpGet("{showtimeId:int}")]
    public async Task<IActionResult> Seats(int showtimeId, CancellationToken ct)
    {
        var map = await bookings.GetSeatMapAsync(showtimeId, ct);
        if (!map.Succeeded || map.Value is null)
        {
            TempData["Error"] = map.ErrorMessage ?? "Không tìm thấy suất chiếu.";
            return RedirectToAction(nameof(Index));
        }

        ViewData["Combos"] = await bookings.ListAvailableCombosAsync(ct);
        ViewData["MaxSeats"] = 8;
        return View(map.Value);
    }

    [HttpPost("{showtimeId:int}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        int showtimeId, PosBookingRequest request, CancellationToken ct)
    {
        if (!ModelState.IsValid || request.SeatIds.Count == 0)
        {
            TempData["Error"] = "Vui lòng nhập tên khách và chọn ít nhất một ghế.";
            return RedirectToAction(nameof(Seats), new { showtimeId });
        }

        request.ShowtimeId = showtimeId;

        var result = await bookings.CreateAsync(
            User.GetUserId(),
            customerUserId: null,
            request,
            Guid.NewGuid(),
            isPos: true,
            new PosContactInfo(request.ContactName, request.ContactPhone, request.ContactEmail),
            ct);

        if (!result.Succeeded || result.Value is null)
        {
            TempData["Error"] = result.ErrorMessage ?? "Không tạo được đơn.";
            return RedirectToAction(nameof(Seats), new { showtimeId });
        }

        return RedirectToAction(nameof(Checkout), new { bookingId = result.Value.BookingId });
    }

    [HttpGet("thu-tien/{bookingId:long}")]
    public async Task<IActionResult> Checkout(long bookingId, CancellationToken ct)
    {
        var booking = await bookings.GetForStaffAsync(bookingId, ct);
        if (booking is null)
        {
            return NotFound();
        }

        return View(booking);
    }

    /// <summary>
    /// Thu tiền mặt. Idempotency key cố định theo đơn + người thu để bấm lặp không tạo lần thu thứ hai.
    /// </summary>
    [HttpPost("thu-tien/{bookingId:long}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ConfirmCash(long bookingId, decimal? amountReceived, CancellationToken ct)
    {
        var key = DeterministicKey(bookingId, User.GetUserId());
        var result = await payments.CashPaymentAsync(
            bookingId, User.GetUserId(),
            new CashPaymentRequest { IdempotencyKey = key, AmountReceived = amountReceived }, ct);

        if (!result.Succeeded || result.Value is null)
        {
            TempData["Error"] = result.ErrorMessage ?? "Không xác nhận được thanh toán.";
            return RedirectToAction(nameof(Checkout), new { bookingId });
        }

        TempData["Success"] = "Đã thu tiền mặt và phát vé.";
        return RedirectToAction(nameof(Receipt), new { bookingId });
    }

    [HttpGet("bien-lai/{bookingId:long}")]
    public async Task<IActionResult> Receipt(long bookingId, CancellationToken ct)
    {
        var booking = await bookings.GetForStaffAsync(bookingId, ct);
        if (booking is null)
        {
            return NotFound();
        }

        return View(booking);
    }

    [HttpPost("huy/{bookingId:long}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(long bookingId, CancellationToken ct)
    {
        var result = await bookings.CancelAsync(bookingId, User.GetUserId(), isStaff: true, "Nhân viên hủy", ct);
        TempData[result.Succeeded ? "Success" : "Error"] =
            result.Succeeded ? "Đã hủy đơn tại quầy." : result.ErrorMessage;
        return RedirectToAction(nameof(Index));
    }

    private static Guid DeterministicKey(long bookingId, int staffUserId)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes($"cash|{bookingId}|{staffUserId}"));
        return new Guid(bytes.AsSpan(0, 16));
    }
}
