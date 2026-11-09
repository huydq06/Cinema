using Microsoft.AspNetCore.Mvc;
using WEB_BTL.Application;
using WEB_BTL.Application.DTOs;
using WEB_BTL.Application.Services;

namespace WEB_BTL.Controllers;

[Route("phim")]
public class MoviesController(
    IMovieCatalogService catalog,
    IShowtimeService showtimes,
    IBookingService bookings) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(
        string? q, int? genreId, DateOnly? date, int page = 1, CancellationToken ct = default)
    {
        var result = await catalog.SearchPublishedAsync(q, genreId, date, page,
            MovieCatalogService.DefaultPageSize, ct);

        ViewData["Genres"] = await catalog.ListGenresAsync(ct);
        ViewData["Keyword"] = q;
        ViewData["GenreId"] = genreId;
        ViewData["Date"] = date?.ToString("yyyy-MM-dd");
        return View(result);
    }

    [HttpGet("{slug}")]
    public async Task<IActionResult> Details(string slug, DateOnly? date, CancellationToken ct = default)
    {
        var movie = await catalog.GetPublishedBySlugAsync(slug, ct);
        if (movie is null)
        {
            return NotFound();
        }

        var day = date ?? DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));
        var slots = await showtimes.GetForMovieOnDateAsync(movie.MovieId, day, ct);

        ViewData["SelectedDate"] = day.ToString("yyyy-MM-dd");
        ViewData["Dates"] = Enumerable.Range(0, 7)
            .Select(i => DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7)).AddDays(i))
            .ToList();

        return View(new MovieDetailsViewModel(movie, slots));
    }

    /// <summary>Sơ đồ ghế dạng JSON cho AJAX và poll định kỳ khi tab đang mở.</summary>
    [HttpGet("/api/showtimes/{showtimeId:int}/seats")]
    public async Task<IActionResult> SeatMap(int showtimeId, CancellationToken ct)
    {
        var result = await bookings.GetSeatMapAsync(showtimeId, ct);
        if (!result.Succeeded)
        {
            return StatusCode(ErrorCodes.ToHttpStatus(result.ErrorCode!), new
            {
                code = result.ErrorCode,
                message = result.ErrorMessage
            });
        }

        return Ok(result.Value);
    }

    /// <summary>Khối lịch chiếu của một ngày; dùng cho chuyển ngày bằng AJAX.</summary>
    [HttpGet("/api/movies/{movieId:int}/showtimes")]
    public async Task<IActionResult> ShowtimeSlots(int movieId, DateOnly date, CancellationToken ct)
    {
        var slots = await showtimes.GetForMovieOnDateAsync(movieId, date, ct);
        return PartialView("_ShowtimeSlots", slots);
    }
}

public record MovieDetailsViewModel(
    MovieListItemDto Movie,
    IReadOnlyList<ShowtimeSlotDto> Slots);
