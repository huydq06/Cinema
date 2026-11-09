using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using WEB_BTL.Domain.Entities;
using WEB_BTL.Domain.Enums;
using WEB_BTL.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace WEB_BTL.Application.Services;

public record TmdbMovieDto(
    int Id,
    string Title,
    string Overview,
    string? PosterPath,
    string? BackdropPath,
    string ReleaseDate,
    double VoteAverage,
    int Runtime);

public record TmdbGenreDto(int Id, string Name);

public interface ITmdbService
{
    Task<List<TmdbMovieDto>> GetNowPlayingAsync(string language = "vi-VN", int page = 1, CancellationToken ct = default);
    Task<List<TmdbMovieDto>> GetUpcomingAsync(string language = "vi-VN", int page = 1, CancellationToken ct = default);
    Task<TmdbMovieDto?> GetMovieDetailAsync(int tmdbId, string language = "vi-VN", CancellationToken ct = default);
    Task<List<TmdbGenreDto>> GetGenresAsync(string language = "vi-VN", CancellationToken ct = default);
    Task SyncNowPlayingAsync(CancellationToken ct = default);
    string PosterUrl(string? path, string size = "w342");
}

public class TmdbService(IHttpClientFactory httpFactory, CinemaDbContext db, ILogger<TmdbService> logger) : ITmdbService
{
    private const string BaseUrl = "https://api.themoviedb.org/3";
    private const string ImageBase = "https://image.tmdb.org/t/p/";

    public string PosterUrl(string? path, string size = "w342") =>
        string.IsNullOrEmpty(path) ? "/img/poster-placeholder.svg" : $"{ImageBase}{size}{path}";

    private HttpClient CreateClient()
    {
        var client = httpFactory.CreateClient("tmdb");
        return client;
    }

    public async Task<List<TmdbMovieDto>> GetNowPlayingAsync(string language = "vi-VN", int page = 1, CancellationToken ct = default)
    {
        try
        {
            var client = CreateClient();
            var response = await client.GetFromJsonAsync<TmdbListResponse<TmdbMovieRaw>>(
                $"{BaseUrl}/movie/now_playing?language={language}&page={page}&region=VN", ct);
            return response?.Results.Select(MapMovie).ToList() ?? [];
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "TMDB GetNowPlaying failed");
            return [];
        }
    }

    public async Task<List<TmdbMovieDto>> GetUpcomingAsync(string language = "vi-VN", int page = 1, CancellationToken ct = default)
    {
        try
        {
            var client = CreateClient();
            var response = await client.GetFromJsonAsync<TmdbListResponse<TmdbMovieRaw>>(
                $"{BaseUrl}/movie/upcoming?language={language}&page={page}&region=VN", ct);
            return response?.Results.Select(MapMovie).ToList() ?? [];
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "TMDB GetUpcoming failed");
            return [];
        }
    }

    public async Task<TmdbMovieDto?> GetMovieDetailAsync(int tmdbId, string language = "vi-VN", CancellationToken ct = default)
    {
        try
        {
            var client = CreateClient();
            var raw = await client.GetFromJsonAsync<TmdbMovieDetailRaw>(
                $"{BaseUrl}/movie/{tmdbId}?language={language}", ct);
            return raw is null ? null : MapMovieDetail(raw);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "TMDB GetMovieDetail {Id} failed", tmdbId);
            return null;
        }
    }

    public async Task<List<TmdbGenreDto>> GetGenresAsync(string language = "vi-VN", CancellationToken ct = default)
    {
        try
        {
            var client = CreateClient();
            var response = await client.GetFromJsonAsync<TmdbGenreResponse>(
                $"{BaseUrl}/genre/movie/list?language={language}", ct);
            return response?.Genres ?? [];
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "TMDB GetGenres failed");
            return [];
        }
    }

    /// <summary>
    /// Đồng bộ phim từ TMDB vào DB — gộp now_playing + upcoming (nhiều trang) để đủ phim.
    /// Chỉ thêm phim mới (theo slug tmdb-{id}); bỏ qua phim không có poster.
    /// </summary>
    public async Task SyncNowPlayingAsync(CancellationToken ct = default)
    {
        var pages = await Task.WhenAll(
            GetNowPlayingAsync(page: 1, ct: ct),
            GetNowPlayingAsync(page: 2, ct: ct),
            GetNowPlayingAsync(page: 3, ct: ct),
            GetUpcomingAsync(page: 1, ct: ct),
            GetUpcomingAsync(page: 2, ct: ct));

        var movies = pages.SelectMany(p => p)
                          .Where(m => !string.IsNullOrEmpty(m.PosterPath))
                          .GroupBy(m => m.Id)
                          .Select(g => g.First())
                          .ToList();

        if (movies.Count == 0)
        {
            logger.LogWarning("TMDB sync: không lấy được danh sách phim");
            return;
        }

        int added = 0;
        foreach (var tm in movies.Take(50))
        {
            var slug = $"tmdb-{tm.Id}";
            if (await db.Movies.AnyAsync(m => m.Slug == slug, ct))
            {
                continue;
            }

            var detail = await GetMovieDetailAsync(tm.Id, ct: ct);

            var movie = new Movie
            {
                Title = detail?.Title ?? tm.Title,
                Slug = slug,
                DescriptionHtml = $"<p>{System.Net.WebUtility.HtmlEncode(detail?.Overview ?? tm.Overview)}</p>",
                DurationMinutes = detail?.Runtime > 0 ? detail.Runtime : 100,
                ReleaseDate = DateOnly.TryParse(tm.ReleaseDate, out var rd) ? rd : DateOnly.FromDateTime(DateTime.UtcNow),
                PosterUrl = PosterUrl(tm.PosterPath, "w342"),
                AgeRating = "T13",
                PublicationStatus = MoviePublicationStatus.Published,
                CreatedAtUtc = DateTime.UtcNow
            };

            db.Movies.Add(movie);
            await db.SaveChangesAsync(ct);
            added++;

            logger.LogInformation("TMDB sync: đã thêm phim {Title}", movie.Title);
        }

        logger.LogInformation("TMDB sync hoàn tất: {Added} phim mới / {Total} phim lấy được", added, movies.Count);
    }

    // ---- Helpers ----
    private TmdbMovieDto MapMovie(TmdbMovieRaw r) => new(
        r.Id, r.Title ?? r.OriginalTitle ?? "Unknown", r.Overview ?? "",
        r.PosterPath, r.BackdropPath, r.ReleaseDate ?? "", r.VoteAverage, 0);

    private TmdbMovieDto MapMovieDetail(TmdbMovieDetailRaw r) => new(
        r.Id, r.Title ?? r.OriginalTitle ?? "Unknown", r.Overview ?? "",
        r.PosterPath, r.BackdropPath, r.ReleaseDate ?? "", r.VoteAverage, r.Runtime);

    // ---- Raw JSON models ----
    private record TmdbListResponse<T>([property: JsonPropertyName("results")] List<T> Results);

    private record TmdbMovieRaw(
        [property: JsonPropertyName("id")] int Id,
        [property: JsonPropertyName("title")] string? Title,
        [property: JsonPropertyName("original_title")] string? OriginalTitle,
        [property: JsonPropertyName("overview")] string? Overview,
        [property: JsonPropertyName("poster_path")] string? PosterPath,
        [property: JsonPropertyName("backdrop_path")] string? BackdropPath,
        [property: JsonPropertyName("release_date")] string? ReleaseDate,
        [property: JsonPropertyName("vote_average")] double VoteAverage);

    private record TmdbMovieDetailRaw(
        [property: JsonPropertyName("id")] int Id,
        [property: JsonPropertyName("title")] string? Title,
        [property: JsonPropertyName("original_title")] string? OriginalTitle,
        [property: JsonPropertyName("overview")] string? Overview,
        [property: JsonPropertyName("poster_path")] string? PosterPath,
        [property: JsonPropertyName("backdrop_path")] string? BackdropPath,
        [property: JsonPropertyName("release_date")] string? ReleaseDate,
        [property: JsonPropertyName("vote_average")] double VoteAverage,
        [property: JsonPropertyName("runtime")] int Runtime);

    private record TmdbGenreResponse([property: JsonPropertyName("genres")] List<TmdbGenreDto> Genres);
}
