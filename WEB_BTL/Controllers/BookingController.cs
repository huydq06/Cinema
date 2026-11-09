using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WEB_BTL.Application;
using WEB_BTL.Application.DTOs;
using WEB_BTL.Application.Services;
using WEB_BTL.Domain.Constants;

namespace WEB_BTL.Controllers;

[Route("dat-ve")]
[Authorize(Policy = Policies.CustomerBooking)]
public class BookingController(
    IBookingService bookings,
    IPaymentService payments,
    IShowtimeService showtimes) : Controller
{
    private const string IdempotencySessionKey = "Booking:LastCreateKey";

    [HttpGet("{showtimeId:int}")]
    public async Task<IActionResult> Seats(int showtimeId, CancellationToken ct)
    {
        var map = await bookings.GetSeatMapAsync(showtimeId, ct);
        if (!map.Succeeded || map.Value is null)
        {
            TempData["Error"] = map.ErrorMessage ?? "Không tìm thấy suất chiếu.";
            return RedirectToAction("Index", "Home");
        }

        ViewData["Combos"] = await bookings.ListAvailableCombosAsync(ct);
        ViewData["MaxSeats"] = 8;
        return View(map.Value);
    }

    [HttpPost("{showtimeId:int}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        int showtimeId, CreateBookingRequest request, CancellationToken ct)
    {
        if (!ModelState.IsValid || request.SeatIds.Count == 0)
        {
            TempData["Error"] = "Vui lòng chọn ít nhất một ghế.";
            return RedirectToAction(nameof(Seats), new { showtimeId });
        }

        request.ShowtimeId = showtimeId;

        // Khóa idempotency giữ trong Session để lần submit lặp (double click, mạng chậm)
        // trả lại đúng đơn thay vì tạo đơn thứ hai.
        var keyRef = HttpContext.Session.GetString(IdempotencySessionKey);
        var key = Guid.TryParse(keyRef, out var parsed) ? parsed : Guid.NewGuid();
        HttpContext.Session.SetString(IdempotencySessionKey, key.ToString());

        var result = await bookings.CreateAsync(
            User.GetUserId(), User.GetUserId(), request, key,
            isPos: false, contact: null, ct);

        if (!result.Succeeded || result.Value is null)
        {
            HttpContext.Session.Remove(IdempotencySessionKey);

            if (result.ConflictingSeatIds.Count > 0)
            {
                TempData["Error"] = result.ErrorMessage;
                return RedirectToAction(nameof(Seats), new { showtimeId });
            }

            ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "Không tạo được đơn.");
            var map = await bookings.GetSeatMapAsync(showtimeId, ct);
            ViewData["Combos"] = await bookings.ListAvailableCombosAsync(ct);
            ViewData["MaxSeats"] = 8;
            return View("Seats", map.Value);
        }

        HttpContext.Session.Remove(IdempotencySessionKey);
        return RedirectToAction(nameof(Pay), new { bookingId = result.Value.BookingId });
    }

    [HttpGet("thanh-toan/{bookingId:long}")]
    public async Task<IActionResult> Pay(long bookingId, CancellationToken ct)
    {
        var booking = await bookings.GetForUserAsync(bookingId, User.GetUserId(), ct);
        if (booking is null)
        {
            return NotFound();
        }

        var status = await payments.GetStatusAsync(bookingId, User.GetUserId(), ct);
        return View(new PaymentPageViewModel(booking, status));
    }

    /// <summary>
    /// Bắt đầu thanh toán mô phỏng. Idempotency key sinh mới mỗi lần bấm, nhưng
    /// service chỉ giữ một lần thử đang mở cho mỗi đơn.
    /// </summary>
    [HttpPost("thanh-toan/{bookingId:long}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> StartPayment(long bookingId, CancellationToken ct)
    {
        var result = await payments.StartAsync(bookingId, User.GetUserId(), Guid.NewGuid(), ct);
        if (!result.Succeeded || result.Value is null)
        {
            TempData["Error"] = result.ErrorMessage ?? "Không khởi tạo được thanh toán.";
            return RedirectToAction(nameof(Pay), new { bookingId });
        }

        return RedirectToAction(nameof(FakeCheckout), new { reference = result.Value.MerchantReference });
    }

    /// <summary>Trang mô phỏng cổng thanh toán: ký callback bằng secret phía server.</summary>
    [HttpGet("cong-tt/{reference}")]
    public async Task<IActionResult> FakeCheckout(string reference, CancellationToken ct)
    {
        var simulation = await payments.GetSimulationAsync(reference, User.GetUserId(), ct);
        if (simulation is null)
        {
            return NotFound();
        }

        ViewData["Reference"] = reference;
        return View(simulation);
    }

    /// <summary>
    /// Mô phỏng cổng gọi callback (IPN). Ở môi trường thật đây là endpoint công khai
    /// xác thực chữ ký của cổng, không phải form người dùng bấm.
    /// </summary>
    [HttpPost("cong-tt/{reference}/xac-nhan")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ConfirmFakePayment(string reference, CancellationToken ct)
    {
        var outcome = await payments.SimulateCallbackAsync(reference, User.GetUserId(), ct);
        if (outcome is null)
        {
            return NotFound();
        }

        TempData[outcome.Ack && outcome.Code is "CONFIRMED" or "DUPLICATE" ? "Success" : "Error"] = outcome.Message;
        var status = await payments.GetStatusByReferenceAsync(reference, User.GetUserId(), ct);
        return status is null
            ? RedirectToAction("History")
            : RedirectToAction(nameof(Pay), new { bookingId = status.BookingId });
    }

    [HttpPost("huy/{bookingId:long}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(long bookingId, CancellationToken ct)
    {
        var result = await bookings.CancelAsync(bookingId, User.GetUserId(), isStaff: false, "Khách hủy", ct);
        TempData[result.Succeeded ? "Success" : "Error"] =
            result.Succeeded ? "Đã hủy đơn và giải phóng ghế." : result.ErrorMessage;
        return RedirectToAction("History");
    }

    [HttpGet("lich-su")]
    public async Task<IActionResult> History(CancellationToken ct)
    {
        var list = await bookings.ListForUserAsync(User.GetUserId(), ct);
        return View(list);
    }

    [HttpGet("ve/{bookingId:long}")]
    public async Task<IActionResult> Ticket(long bookingId, CancellationToken ct)
    {
        var booking = await bookings.GetForUserAsync(bookingId, User.GetUserId(), ct);
        if (booking is null)
        {
            return NotFound();
        }

        return View(booking);
    }
}

public record PaymentPageViewModel(BookingDto Booking, PaymentStatusDto? Status);
