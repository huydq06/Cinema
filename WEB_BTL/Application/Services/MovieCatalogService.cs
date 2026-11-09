using Microsoft.EntityFrameworkCore;
using WEB_BTL.Application.DTOs;
using WEB_BTL.Domain.Enums;
using WEB_BTL.Infrastructure.Data;

namespace WEB_BTL.Application.Services;

public interface IMovieCatalogService
{
    Task<PagedResult<MovieListItemDto>> SearchPublishedAsync(
        string? keyword, int? genreId, DateOnly? date, int page, int pageSize,
        CancellationToken ct = default);

    Task<MovieListItemDto?> GetPublishedBySlugAsync(string slug, CancellationToken ct = default);
    Task<IReadOnlyList<GenreItemDto>> ListGenresAsync(CancellationToken ct = default);
}

public record GenreItemDto(int GenreId, string Name, string Slug);

public class MovieCatalogService(CinemaDbContext db) : IMovieCatalogService
{
    public const int DefaultPageSize = 8;
    public const int MaxPageSize = 50;

    public async Task<PagedResult<MovieListItemDto>> SearchPublishedAsync(
        string? keyword, int? genreId, DateOnly? date, int page, int pageSize,
        CancellationToken ct = default)
    {
        page = page < 1 ? 1 : page;
        pageSize = pageSize is < 1 or > MaxPageSize ? DefaultPageSize : pageSize;

        // Biên ngày Việt Nam (UTC+7) chuyển sang UTC cho truy vấn suất chiếu.
        var dayStartUtc = date?.ToDateTime(TimeOnly.MinValue).AddHours(-7);
        var dayEndUtc = dayStartUtc?.AddDays(1);

        var query = db.Movies.AsNoTracking()
            .Where(m => m.PublicationStatus == MoviePublicationStatus.Published);

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var term = keyword.Trim();
            query = query.Where(m => m.Title.Contains(term));
        }

        if (genreId is > 0)
        {
            query = query.Where(m => m.MovieGenres.Any(g => g.GenreId == genreId));
        }

        if (dayStartUtc is not null)
        {
            query = query.Where(m => m.Showtimes.Any(s =>
                s.Status != ShowtimeStatus.Cancelled && s.Status != ShowtimeStatus.Draft
                && s.StartAtUtc >= dayStartUtc && s.StartAtUtc < dayEndUtc));
        }

        var total = await query.CountAsync(ct);

        // Sắp xếp ổn định: tiêu đề rồi MovieId làm khóa phụ.
        var items = await query
            .OrderBy(m => m.ReleaseDate <= DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7)) ? 0 : 1)
            .ThenBy(m => m.Title)
            .ThenBy(m => m.MovieId)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(m => new MovieListItemDto(
                m.MovieId,
                m.Title,
                m.Slug,
                m.PosterUrl,
                m.DurationMinutes,
                m.AgeRating,
                m.ReleaseDate,
                m.MovieGenres.Select(g => g.Genre!.Name).ToList(),
                m.Showtimes.Any(s => s.Status == ShowtimeStatus.OnSale
                                     && s.SalesCloseAtUtc > DateTime.UtcNow)))
            .ToListAsync(ct);

        return new PagedResult<MovieListItemDto>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = total
        };
    }

    public async Task<MovieListItemDto?> GetPublishedBySlugAsync(string slug, CancellationToken ct = default)
    {
        return await db.Movies.AsNoTracking()
            .Where(m => m.PublicationStatus == MoviePublicationStatus.Published && m.Slug == slug)
            .Select(m => new MovieListItemDto(
                m.MovieId,
                m.Title,
                m.Slug,
                m.PosterUrl,
                m.DurationMinutes,
                m.AgeRating,
                m.ReleaseDate,
                m.MovieGenres.Select(g => g.Genre!.Name).ToList(),
                m.Showtimes.Any(s => s.Status == ShowtimeStatus.OnSale)))
            .SingleOrDefaultAsync(ct);
    }

    public async Task<IReadOnlyList<GenreItemDto>> ListGenresAsync(CancellationToken ct = default)
    {
        return await db.Genres.AsNoTracking()
            .Where(g => g.IsActive)
            .OrderBy(g => g.Name)
            .Select(g => new GenreItemDto(g.GenreId, g.Name, g.Slug))
            .ToListAsync(ct);
    }
}
