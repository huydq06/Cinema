using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WEB_BTL.Application.Services;
using WEB_BTL.Domain.Constants;

namespace WEB_BTL.Controllers;

/// <summary>Đồng bộ dữ liệu từ TMDB — chỉ Admin.</summary>
[Authorize(Policy = Policies.ManageCinema)]
[Route("quan-tri/dong-bo")]
public class AdminSyncController(ITmdbService tmdb) : Controller
{
    [HttpGet("")]
    public IActionResult Index() => View();

    [HttpPost("tmdb")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SyncTmdb(CancellationToken ct)
    {
        await tmdb.SyncNowPlayingAsync(ct);
        TempData["Success"] = "Đã đồng bộ phim đang chiếu từ TMDB.";
        return RedirectToAction("Index", "AdminMovies");
    }
}
