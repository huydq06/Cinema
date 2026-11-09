using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using WEB_BTL.Domain.Entities;
using WEB_BTL.Domain.Enums;
using WEB_BTL.Infrastructure.Data;
using WEB_BTL.Infrastructure.Security;

namespace WEB_BTL.Application.Services;

public class MovieInput
{
    [Required(ErrorMessage = "Nhập tên phim.")]
    [StringLength(200)]
    public string Title { get; set; } = string.Empty;

    [Required(ErrorMessage = "Nhập slug.")]
    [RegularExpression("^[a-z0-9]+(?:-[a-z0-9]+)*$", ErrorMessage = "Slug chỉ gồm chữ thường, số và dấu gạch ngang.")]
    [StringLength(240)]
    public string Slug { get; set; } = string.Empty;

    public string DescriptionHtml { get; set; } = string.Empty;

    [StringLength(150)]
    public string? Director { get; set; }

    [StringLength(1000)]
    public string? Cast { get; set; }

    [Range(1, 600, ErrorMessage = "Thời lượng phải từ 1 đến 600 phút.")]
    public int DurationMinutes { get; set; } = 100;

    [Required]
    public DateOnly ReleaseDate { get; set; } = DateOnly.FromDateTime(DateTime.UtcNow);

    [StringLength(500)]
    public string PosterUrl { get; set; } = string.Empty;

    [StringLength(500)]
    public string? TrailerUrl { get; set; }

    [Required(ErrorMessage = "Nhập phân loại độ tuổi.")]
    [StringLength(20)]
    public string AgeRating { get; set; } = "P";

    public List<int> GenreIds { get; set; } = [];

    public byte[]? RowVersion { get; set; }
}

public record AdminMovieListItemDto(
    int MovieId,
    string Title,
    string Slug,
    string AgeRating,
    int DurationMinutes,
    string PublicationStatus,
    DateOnly ReleaseDate,
    int ShowtimeCount);

public interface IAdminCatalogService
{
    Task<IReadOnlyList<AdminMovieListItemDto>> ListMoviesAsync(string? keyword, CancellationToken ct = default);
    Task<MovieInput?> GetMovieAsync(int movieId, CancellationToken ct = default);
    Task<ServiceResult<int>> CreateMovieAsync(MovieInput input, int actorUserId, CancellationToken ct = default);
    Task<ServiceResult> UpdateMovieAsync(int movieId, MovieInput input, int actorUserId, CancellationToken ct = default);
    Task<ServiceResult> SetPublicationAsync(int movieId, MoviePublicationStatus status, int actorUserId, CancellationToken ct = default);

    Task<IReadOnlyList<Genre>> ListGenresAsync(CancellationToken ct = default);
    Task<ServiceResult<int>> CreateGenreAsync(string name, string slug, int actorUserId, CancellationToken ct = default);
    Task<ServiceResult> RenameGenreAsync(int genreId, string name, bool isActive, int actorUserId, CancellationToken ct = default);
}

public class AdminCatalogService(CinemaDbContext db, IAuditService audit) : IAdminCatalogService
{
    public async Task<IReadOnlyList<AdminMovieListItemDto>> ListMoviesAsync(
        string? keyword, CancellationToken ct = default)
    {
        var query = db.Movies.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var term = keyword.Trim();
            query = query.Where(m => m.Title.Contains(term) || m.Slug.Contains(term));
        }

        return await query
            .OrderByDescending(m => m.MovieId)
            .Select(m => new AdminMovieListItemDto(
                m.MovieId, m.Title, m.Slug, m.AgeRating, m.DurationMinutes,
                m.PublicationStatus.ToString(), m.ReleaseDate, m.Showtimes.Count))
            .ToListAsync(ct);
    }

    public async Task<MovieInput?> GetMovieAsync(int movieId, CancellationToken ct = default)
    {
        var movie = await db.Movies.AsNoTracking()
            .Include(m => m.MovieGenres)
            .SingleOrDefaultAsync(m => m.MovieId == movieId, ct);

        if (movie is null)
        {
            return null;
        }

        return new MovieInput
        {
            Title = movie.Title,
            Slug = movie.Slug,
            DescriptionHtml = movie.DescriptionHtml,
            Director = movie.Director,
            Cast = movie.Cast,
            DurationMinutes = movie.DurationMinutes,
            ReleaseDate = movie.ReleaseDate,
            PosterUrl = movie.PosterUrl,
            TrailerUrl = movie.TrailerUrl,
            AgeRating = movie.AgeRating,
            GenreIds = movie.MovieGenres.Select(g => g.GenreId).ToList(),
            RowVersion = movie.RowVersion
        };
    }

    public async Task<ServiceResult<int>> CreateMovieAsync(
        MovieInput input, int actorUserId, CancellationToken ct = default)
    {
        var slug = input.Slug.Trim().ToLowerInvariant();
        if (await db.Movies.AnyAsync(m => m.Slug == slug, ct))
        {
            return ServiceResult<int>.Fail(ErrorCodes.ValidationFailed, "Slug đã được dùng cho phim khác.");
        }

        var genreIds = input.GenreIds.Distinct().ToList();
        if (genreIds.Count > 0)
        {
            var valid = await db.Genres.CountAsync(g => genreIds.Contains(g.GenreId), ct);
            if (valid != genreIds.Count)
            {
                return ServiceResult<int>.Fail(ErrorCodes.ValidationFailed, "Có thể loại không tồn tại.");
            }
        }

        var movie = new Movie
        {
            Title = input.Title.Trim(),
            Slug = slug,
            DescriptionHtml = HtmlSanitizer.Sanitize(input.DescriptionHtml),
            Director = NullIfBlank(input.Director),
            Cast = NullIfBlank(input.Cast),
            DurationMinutes = input.DurationMinutes,
            ReleaseDate = input.ReleaseDate,
            PosterUrl = input.PosterUrl.Trim(),
            TrailerUrl = NullIfBlank(input.TrailerUrl),
            AgeRating = input.AgeRating.Trim().ToUpperInvariant(),
            PublicationStatus = MoviePublicationStatus.Draft,
            CreatedAtUtc = DateTime.UtcNow,
            MovieGenres = genreIds.Select(id => new MovieGenre { GenreId = id }).ToList()
        };

        db.Movies.Add(movie);
        await audit.LogAsync(actorUserId, "Movie.Created", nameof(Movie), "pending",
            new { movie.Title, movie.Slug }, ct);
        await db.SaveChangesAsync(ct);

        return ServiceResult<int>.Ok(movie.MovieId);
    }

    public async Task<ServiceResult> UpdateMovieAsync(
        int movieId, MovieInput input, int actorUserId, CancellationToken ct = default)
    {
        var movie = await db.Movies
            .Include(m => m.MovieGenres)
            .SingleOrDefaultAsync(m => m.MovieId == movieId, ct);

        if (movie is null)
        {
            return ServiceResult.Fail(ErrorCodes.NotFound, "Không tìm thấy phim.");
        }

        if (input.RowVersion is { Length: > 0 } && !movie.RowVersion.SequenceEqual(input.RowVersion))
        {
            return ServiceResult.Fail(ErrorCodes.InvalidState,
                "Dữ liệu đã được người khác cập nhật. Vui lòng tải lại trang.");
        }

        var slug = input.Slug.Trim().ToLowerInvariant();
        if (await db.Movies.AnyAsync(m => m.Slug == slug && m.MovieId != movieId, ct))
        {
            return ServiceResult.Fail(ErrorCodes.ValidationFailed, "Slug đã được dùng cho phim khác.");
        }

        var genreIds = input.GenreIds.Distinct().ToList();
        if (genreIds.Count > 0)
        {
            var valid = await db.Genres.CountAsync(g => genreIds.Contains(g.GenreId), ct);
            if (valid != genreIds.Count)
            {
                return ServiceResult.Fail(ErrorCodes.ValidationFailed, "Có thể loại không tồn tại.");
            }
        }

        movie.Title = input.Title.Trim();
        movie.Slug = slug;
        movie.DescriptionHtml = HtmlSanitizer.Sanitize(input.DescriptionHtml);
        movie.Director = NullIfBlank(input.Director);
        movie.Cast = NullIfBlank(input.Cast);
        movie.DurationMinutes = input.DurationMinutes;
        movie.ReleaseDate = input.ReleaseDate;
        movie.PosterUrl = input.PosterUrl.Trim();
        movie.TrailerUrl = NullIfBlank(input.TrailerUrl);
        movie.AgeRating = input.AgeRating.Trim().ToUpperInvariant();
        movie.UpdatedAtUtc = DateTime.UtcNow;

        foreach (var id in genreIds.Where(id => movie.MovieGenres.All(g => g.GenreId != id)))
        {
            movie.MovieGenres.Add(new MovieGenre { MovieId = movieId, GenreId = id });
        }

        foreach (var link in movie.MovieGenres.Where(g => !genreIds.Contains(g.GenreId)).ToList())
        {
            movie.MovieGenres.Remove(link);
        }

        await audit.LogAsync(actorUserId, "Movie.Updated", nameof(Movie), movieId.ToString(),
            new { movie.Title, movie.Slug }, ct);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ServiceResult.Fail(ErrorCodes.InvalidState,
                "Dữ liệu đã được người khác cập nhật. Vui lòng tải lại trang.");
        }

        return ServiceResult.Ok();
    }

    /// <summary>
    /// Chuyển trạng thái xuất bản. Không cho ẩn (Archived) phim còn suất chưa kết thúc
    /// để tránh lịch đang bán trỏ tới phim không còn công khai.
    /// </summary>
    public async Task<ServiceResult> SetPublicationAsync(
        int movieId, MoviePublicationStatus status, int actorUserId, CancellationToken ct = default)
    {
        var movie = await db.Movies.SingleOrDefaultAsync(m => m.MovieId == movieId, ct);
        if (movie is null)
        {
            return ServiceResult.Fail(ErrorCodes.NotFound, "Không tìm thấy phim.");
        }

        if (status == MoviePublicationStatus.Archived)
        {
            var activeShowtimes = await db.Showtimes.AsNoTracking()
                .CountAsync(s => s.MovieId == movieId
                                 && s.Status != ShowtimeStatus.Cancelled
                                 && s.Status != ShowtimeStatus.Completed
                                 && s.StartAtUtc > DateTime.UtcNow, ct);
            if (activeShowtimes > 0)
            {
                return ServiceResult.Fail(ErrorCodes.InvalidState,
                    $"Phim còn {activeShowtimes} suất chưa chiếu. Hãy hủy/đóng các suất đó trước khi lưu trữ.");
            }
        }

        if (status == MoviePublicationStatus.Published && string.IsNullOrWhiteSpace(movie.PosterUrl))
        {
            return ServiceResult.Fail(ErrorCodes.ValidationFailed, "Phim chưa có ảnh poster.");
        }

        movie.PublicationStatus = status;
        movie.UpdatedAtUtc = DateTime.UtcNow;

        await audit.LogAsync(actorUserId, "Movie.PublicationChanged", nameof(Movie), movieId.ToString(),
            new { status = status.ToString() }, ct);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ServiceResult.Fail(ErrorCodes.InvalidState,
                "Dữ liệu đã được người khác cập nhật. Vui lòng tải lại trang.");
        }

        return ServiceResult.Ok();
    }

    public async Task<IReadOnlyList<Genre>> ListGenresAsync(CancellationToken ct = default) =>
        await db.Genres.AsNoTracking().OrderBy(g => g.Name).ToListAsync(ct);

    public async Task<ServiceResult<int>> CreateGenreAsync(
        string name, string slug, int actorUserId, CancellationToken ct = default)
    {
        name = name.Trim();
        slug = slug.Trim().ToLowerInvariant();

        if (name.Length == 0 || slug.Length == 0)
        {
            return ServiceResult<int>.Fail(ErrorCodes.ValidationFailed, "Nhập tên và slug thể loại.");
        }

        if (await db.Genres.AnyAsync(g => g.Slug == slug, ct))
        {
            return ServiceResult<int>.Fail(ErrorCodes.ValidationFailed, "Slug thể loại đã tồn tại.");
        }

        var genre = new Genre { Name = name, Slug = slug, IsActive = true };
        db.Genres.Add(genre);

        await audit.LogAsync(actorUserId, "Genre.Created", nameof(Genre), "pending", new { name, slug }, ct);
        await db.SaveChangesAsync(ct);

        return ServiceResult<int>.Ok(genre.GenreId);
    }

    public async Task<ServiceResult> RenameGenreAsync(
        int genreId, string name, bool isActive, int actorUserId, CancellationToken ct = default)
    {
        var genre = await db.Genres.SingleOrDefaultAsync(g => g.GenreId == genreId, ct);
        if (genre is null)
        {
            return ServiceResult.Fail(ErrorCodes.NotFound, "Không tìm thấy thể loại.");
        }

        name = name.Trim();
        if (name.Length == 0)
        {
            return ServiceResult.Fail(ErrorCodes.ValidationFailed, "Tên thể loại không được trống.");
        }

        genre.Name = name;
        genre.IsActive = isActive;

        await audit.LogAsync(actorUserId, "Genre.Updated", nameof(Genre), genreId.ToString(),
            new { name, isActive }, ct);
        await db.SaveChangesAsync(ct);

        return ServiceResult.Ok();
    }

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
