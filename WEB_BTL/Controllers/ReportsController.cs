using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WEB_BTL.Application.Services;
using WEB_BTL.Domain.Constants;

namespace WEB_BTL.Controllers;

[Route("bao-cao")]
[Authorize(Policy = Policies.ViewRevenue)]
public class ReportsController(IReportingService reporting) : Controller
{
    [HttpGet("doanh-thu")]
    public async Task<IActionResult> Revenue(DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));
        var fromDate = from ?? today.AddDays(-29);
        var toDate = to ?? today;

        var report = await reporting.GetRevenueAsync(fromDate, toDate, ct);
        return View(report);
    }
}
