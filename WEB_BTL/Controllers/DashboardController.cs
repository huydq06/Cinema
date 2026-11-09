using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WEB_BTL.Application.Services;
using WEB_BTL.Domain.Constants;

namespace WEB_BTL.Controllers;

/// <summary>Bảng điều khiển quản trị. Tên route không cấp quyền; policy mới quyết định.</summary>
[Route("quan-tri")]
[Authorize(Policy = Policies.ManageCinema)]
public class DashboardController(IAdminDashboardService dashboard) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var stats = await dashboard.GetAsync(ct);
        return View(stats);
    }
}
