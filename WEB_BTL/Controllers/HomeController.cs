using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using WEB_BTL.Application.Services;
using WEB_BTL.Models;

namespace WEB_BTL.Controllers
{
    public class HomeController(IMovieCatalogService catalog, ITmdbService tmdb) : Controller
    {
        public async Task<IActionResult> Index(int page = 1, CancellationToken ct = default)
        {
            // Phim từ DB (đã seed/sync)
            var movies = await catalog.SearchPublishedAsync(null, null, null, page, 8, ct);

            // Featured: phim TMDB đầu tiên đang chiếu (dùng cho hero)
            var nowPlaying = await tmdb.GetNowPlayingAsync(ct: ct);
            var featured = nowPlaying.FirstOrDefault();

            ViewData["FeaturedTmdb"] = featured;
            ViewData["TmdbService"] = tmdb;

            return View(movies);
        }

        public IActionResult Privacy() => View();

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
