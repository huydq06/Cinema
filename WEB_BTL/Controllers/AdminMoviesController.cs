using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WEB_BTL.Application.Services;
using WEB_BTL.Domain.Constants;
using WEB_BTL.Domain.Enums;

namespace WEB_BTL.Controllers;

[Route("quan-tri/phim")]
[Authorize(Policy = Policies.ManageCinema)]
public class AdminMoviesController(IAdminCatalogService catalog) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(string? q, CancellationToken ct)
    {
        ViewData["Keyword"] = q;
        return View(await catalog.ListMoviesAsync(q, ct));
    }

    [HttpGet("tao")]
    public async Task<IActionResult> Create(CancellationToken ct)
    {
        ViewData["Genres"] = await catalog.ListGenresAsync(ct);
        return View(new MovieInput());
    }

    [HttpPost("tao")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(MovieInput input, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            ViewData["Genres"] = await catalog.ListGenresAsync(ct);
            return View(input);
        }

        var result = await catalog.CreateMovieAsync(input, User.GetUserId(), ct);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "Không lưu được phim.");
            ViewData["Genres"] = await catalog.ListGenresAsync(ct);
            return View(input);
        }

        TempData["Success"] = "Đã tạo phim ở trạng thái Draft. Kiểm tra rồi bấm xuất bản.";
        return RedirectToAction(nameof(Edit), new { id = result.Value });
    }

    [HttpGet("{id:int}/sua")]
    public async Task<IActionResult> Edit(int id, CancellationToken ct)
    {
        var input = await catalog.GetMovieAsync(id, ct);
        if (input is null)
        {
            return NotFound();
        }

        ViewData["MovieId"] = id;
        ViewData["Genres"] = await catalog.ListGenresAsync(ct);
        return View(input);
    }

    [HttpPost("{id:int}/sua")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, MovieInput input, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            ViewData["MovieId"] = id;
            ViewData["Genres"] = await catalog.ListGenresAsync(ct);
            return View(input);
        }

        var result = await catalog.UpdateMovieAsync(id, input, User.GetUserId(), ct);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "Không lưu được phim.");
            ViewData["MovieId"] = id;
            ViewData["Genres"] = await catalog.ListGenresAsync(ct);
            return View(input);
        }

        TempData["Success"] = "Đã cập nhật phim.";
        return RedirectToAction(nameof(Edit), new { id });
    }

    [HttpPost("{id:int}/xuat-ban")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Publish(int id, CancellationToken ct)
    {
        var result = await catalog.SetPublicationAsync(id, MoviePublicationStatus.Published, User.GetUserId(), ct);
        TempData[result.Succeeded ? "Success" : "Error"] =
            result.Succeeded ? "Đã xuất bản phim." : result.ErrorMessage;
        return RedirectToAction(nameof(Edit), new { id });
    }

    [HttpPost("{id:int}/luu-tru")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Archive(int id, CancellationToken ct)
    {
        var result = await catalog.SetPublicationAsync(id, MoviePublicationStatus.Archived, User.GetUserId(), ct);
        TempData[result.Succeeded ? "Success" : "Error"] =
            result.Succeeded ? "Đã lưu trữ phim." : result.ErrorMessage;
        return RedirectToAction(nameof(Edit), new { id });
    }

    [HttpGet("the-loai")]
    public async Task<IActionResult> Genres(CancellationToken ct) => View(await catalog.ListGenresAsync(ct));

    [HttpPost("the-loai")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateGenre(string name, string slug, CancellationToken ct)
    {
        var result = await catalog.CreateGenreAsync(name, slug, User.GetUserId(), ct);
        TempData[result.Succeeded ? "Success" : "Error"] =
            result.Succeeded ? "Đã thêm thể loại." : result.ErrorMessage;
        return RedirectToAction(nameof(Genres));
    }

    [HttpPost("the-loai/{id:int}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateGenre(int id, string name, bool isActive, CancellationToken ct)
    {
        var result = await catalog.RenameGenreAsync(id, name, isActive, User.GetUserId(), ct);
        TempData[result.Succeeded ? "Success" : "Error"] =
            result.Succeeded ? "Đã cập nhật thể loại." : result.ErrorMessage;
        return RedirectToAction(nameof(Genres));
    }
}
