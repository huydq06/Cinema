using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WEB_BTL.Application.DTOs;
using WEB_BTL.Application.Services;
using WEB_BTL.Domain.Constants;

namespace WEB_BTL.Controllers;

[Route("soat-ve")]
[Authorize(Policy = Policies.OperateCinema)]
public class CheckInController(ICheckInService checkIn, IBookingService bookings) : Controller
{
    [HttpGet("")]
    public IActionResult Index() => View(new CheckInRequest());

    [HttpPost("")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(CheckInRequest request, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            return View(request);
        }

        var result = await checkIn.CheckInAsync(request.Token, User.GetUserId(), ct);
        if (!result.Succeeded || result.Value is null)
        {
            ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "Không soát được vé.");
            return View(request);
        }

        var ticket = result.Value;
        ViewData["CheckedIn"] = ticket;
        ViewData["CheckedInLocal"] = ticket.CheckedInAtUtc.AddHours(7);
        return View(new CheckInRequest());
    }

    /// <summary>Tra cứu đơn theo mã vé để nhân viên đối chiếu thông tin trước khi soát.</summary>
    [HttpGet("tra-cuu")]
    public IActionResult Lookup() => View();

    [HttpPost("tra-cuu")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Lookup(long bookingId, CancellationToken ct)
    {
        var booking = await bookings.GetForStaffAsync(bookingId, ct);
        if (booking is null)
        {
            TempData["Error"] = "Không tìm thấy đơn.";
            return RedirectToAction(nameof(Lookup));
        }

        return View("BookingDetail", booking);
    }
}
