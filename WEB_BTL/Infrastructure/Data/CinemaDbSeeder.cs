using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WEB_BTL.Application.Services;
using WEB_BTL.Domain.Constants;
using WEB_BTL.Domain.Entities;
using WEB_BTL.Domain.Enums;
using WEB_BTL.Infrastructure.Security;

namespace WEB_BTL.Infrastructure.Data;

public class SeedOptions
{
    public const string SectionName = "Seed";

    public string AdminEmail { get; set; } = "admin@cinema.local";
    public string AdminPassword { get; set; } = "Admin@12345";
    public string StaffEmail { get; set; } = "staff@cinema.local";
    public string StaffPassword { get; set; } = "Staff@12345";
    public string CustomerEmail { get; set; } = "customer@cinema.local";
    public string CustomerPassword { get; set; } = "Customer@12345";
}

/// <summary>
/// Seed idempotent: chỉ tạo dữ liệu khi bảng Roles còn trống.
/// Phim thật từ TMDB — không có phim giả.
/// </summary>
public static class CinemaDbSeeder
{
    public static async Task SeedAsync(IServiceProvider services, CancellationToken ct = default)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CinemaDbContext>();
        var passwordService = scope.ServiceProvider.GetRequiredService<IPasswordService>();
        var seed = scope.ServiceProvider.GetRequiredService<IOptions<SeedOptions>>().Value;
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<CinemaDbContext>>();

        if (await db.Roles.AnyAsync(ct)) return;

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        // ---- Roles & Users -----------------------------------------------
        var roleMap = Roles.All.ToDictionary(
            name => name,
            name => new Role
            {
                RoleName = name,
                Description = name switch
                {
                    Domain.Constants.Roles.Admin => "Quản trị hệ thống",
                    Domain.Constants.Roles.Staff => "Nhân viên vận hành",
                    _ => "Khách hàng"
                }
            });
        db.Roles.AddRange(roleMap.Values);
        await db.SaveChangesAsync(ct);

        db.Users.AddRange(
            CreateUser(roleMap[Domain.Constants.Roles.Admin].RoleId, "Quản trị viên", seed.AdminEmail, seed.AdminPassword, passwordService),
            CreateUser(roleMap[Domain.Constants.Roles.Staff].RoleId, "Nhân viên quầy", seed.StaffEmail, seed.StaffPassword, passwordService),
            CreateUser(roleMap[Domain.Constants.Roles.Customer].RoleId, "Khách hàng mẫu", seed.CustomerEmail, seed.CustomerPassword, passwordService));
        await db.SaveChangesAsync(ct);

        // ---- Phòng & Ghế ---------------------------------------------------
        var rooms = new[]
        {
            new Room { RoomName = "Phòng 1 — 2D",   RoomType = RoomType.Standard2D, IsActive = true },
            new Room { RoomName = "Phòng 2 — 3D",   RoomType = RoomType.ThreeD,     IsActive = true },
            new Room { RoomName = "Phòng 3 — 2D",   RoomType = RoomType.Standard2D, IsActive = true },
            new Room { RoomName = "Phòng 4 — VIP",  RoomType = RoomType.ThreeD,     IsActive = true },
        };
        db.Rooms.AddRange(rooms);
        await db.SaveChangesAsync(ct);

        var roomSeats = new Dictionary<int, List<Seat>>();
        foreach (var (room, rows, cols, vipRow, vipFee) in new[]
        {
            (rooms[0], 8, 10, 7, 20_000m),
            (rooms[1], 7, 9,  6, 30_000m),
            (rooms[2], 8, 10, 7, 20_000m),
            (rooms[3], 6, 8,  4, 50_000m),
        })
        {
            var seats = BuildSeats(room.RoomId, rows, cols, vipRow, vipFee);
            db.Seats.AddRange(seats);
            roomSeats[room.RoomId] = seats;
        }
        await db.SaveChangesAsync(ct);

        // ---- Combo ---------------------------------------------------------
        db.FoodCombos.AddRange(
            new FoodCombo { Name = "Combo Đơn",     Description = "1 bắp vừa + 1 Pepsi",   Price = 65_000m, IsAvailable = true },
            new FoodCombo { Name = "Combo Đôi",     Description = "1 bắp lớn + 2 Pepsi",   Price = 99_000m, IsAvailable = true },
            new FoodCombo { Name = "Combo Gia Đình",Description = "2 bắp lớn + 4 Pepsi",   Price = 179_000m, IsAvailable = true },
            new FoodCombo { Name = "Bắp rang",      Description = "Bắp lớn vị caramel",    Price = 45_000m, IsAvailable = true },
            new FoodCombo { Name = "Nachos",        Description = "Nachos + sốt pho mát",  Price = 55_000m, IsAvailable = true });
        await db.SaveChangesAsync(ct);

        await tx.CommitAsync(ct);
        logger.LogInformation("Seed cơ sở hạ tầng hoàn tất. Tiến hành đồng bộ phim từ TMDB...");

        // ---- Phim từ TMDB (ngoài transaction để timeout không ảnh hưởng) ---
        try
        {
            var tmdb = scope.ServiceProvider.GetRequiredService<ITmdbService>();
            await SyncMoviesFromTmdbAsync(db, tmdb, rooms, roomSeats, logger, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "TMDB sync trong seed thất bại — app vẫn chạy, sync lại từ /quan-tri/dong-bo");
        }
    }

    // -----------------------------------------------------------------------
    private static async Task SyncMoviesFromTmdbAsync(
        CinemaDbContext db,
        ITmdbService tmdb,
        Room[] rooms,
        Dictionary<int, List<Seat>> roomSeats,
        ILogger logger,
        CancellationToken ct)
    {
        // Lấy now_playing trang 1+2 và upcoming trang 1+2 → ~80 phim, chọn 50 có poster
        var tasks = new[]
        {
            tmdb.GetNowPlayingAsync(page: 1, ct: ct),
            tmdb.GetNowPlayingAsync(page: 2, ct: ct),
            tmdb.GetNowPlayingAsync(page: 3, ct: ct),
            tmdb.GetUpcomingAsync(page: 1, ct: ct),
            tmdb.GetUpcomingAsync(page: 2, ct: ct),
        };
        await Task.WhenAll(tasks);

        var all = tasks.SelectMany(t => t.Result)
                       .Where(m => !string.IsNullOrEmpty(m.PosterPath))
                       .GroupBy(m => m.Id)
                       .Select(g => g.First())
                       .Take(50)
                       .ToList();

        logger.LogInformation("TMDB trả về {Count} phim để seed", all.Count);

        // Genres từ TMDB
        var tmdbGenres = await tmdb.GetGenresAsync(ct: ct);
        var genreByTmdbId = tmdbGenres.ToDictionary(g => g.Id);

        // Genres trong DB (tạo nếu chưa có)
        var dbGenreByName = await db.Genres.ToDictionaryAsync(g => g.Name, g => g, StringComparer.OrdinalIgnoreCase, ct);

        async Task<Genre> EnsureGenreAsync(string name)
        {
            if (dbGenreByName.TryGetValue(name, out var existing)) return existing;
            var slug = ToSlug(name);
            // Đảm bảo slug không trùng
            var n = 1;
            var candidate = slug;
            while (await db.Genres.AnyAsync(g => g.Slug == candidate, ct))
                candidate = $"{slug}-{++n}";

            var g2 = new Genre { Name = name, Slug = candidate, IsActive = true };
            db.Genres.Add(g2);
            await db.SaveChangesAsync(ct);
            dbGenreByName[name] = g2;
            return g2;
        }

        // Map TMDB genre ids → tên Việt (TMDB trả tên Việt khi dùng language=vi-VN)
        var starts = BuildUpcomingStartsUtc(slotCount: all.Count * 2);
        var startIndex = 0;

        foreach (var tm in all)
        {
            var slug = $"tmdb-{tm.Id}";
            if (await db.Movies.AnyAsync(m => m.Slug == slug, ct)) continue;

            // Lấy chi tiết để có runtime và genres
            var detail = await tmdb.GetMovieDetailAsync(tm.Id, ct: ct)
                         ?? tm with { Runtime = 100 };

            var movie = new Movie
            {
                Title = detail.Title,
                Slug = slug,
                DescriptionHtml = $"<p>{System.Net.WebUtility.HtmlEncode(detail.Overview)}</p>",
                DurationMinutes = detail.Runtime > 0 ? detail.Runtime : 100,
                ReleaseDate = DateOnly.TryParse(detail.ReleaseDate, out var rd) ? rd : DateOnly.FromDateTime(DateTime.UtcNow),
                PosterUrl = tmdb.PosterUrl(detail.PosterPath, "w342"),
                AgeRating = "T13",
                PublicationStatus = MoviePublicationStatus.Published,
                CreatedAtUtc = DateTime.UtcNow,
            };
            db.Movies.Add(movie);
            await db.SaveChangesAsync(ct);

            logger.LogInformation("Đã thêm phim [{Id}] {Title}", tm.Id, movie.Title);

            // Tạo 2–3 suất chiếu cho mỗi phim (xoay vòng phòng)
            var showtimeCount = 3;
            for (var i = 0; i < showtimeCount && startIndex < starts.Count; i++, startIndex++)
            {
                var room = rooms[(startIndex) % rooms.Length];
                var seats = roomSeats[room.RoomId];
                var startUtc = starts[startIndex];
                var basePrice = room.RoomType == RoomType.ThreeD ? 95_000m : 70_000m;
                var endUtc = startUtc.AddMinutes(movie.DurationMinutes);

                var showtime = new Showtime
                {
                    MovieId = movie.MovieId,
                    RoomId = room.RoomId,
                    StartAtUtc = startUtc,
                    EndAtUtc = endUtc,
                    CleaningMinutes = 15,
                    CleanupEndsAtUtc = endUtc.AddMinutes(15),
                    SalesCloseAtUtc = startUtc.AddMinutes(-10),
                    BasePrice = basePrice,
                    Status = ShowtimeStatus.OnSale,
                    CreatedAtUtc = DateTime.UtcNow,
                };
                db.Showtimes.Add(showtime);
                await db.SaveChangesAsync(ct);

                foreach (var seat in seats)
                {
                    db.ShowtimeSeats.Add(new ShowtimeSeat
                    {
                        ShowtimeId = showtime.ShowtimeId,
                        SeatId = seat.SeatId,
                        RoomId = room.RoomId,
                        SeatCodeSnapshot = seat.SeatCode,
                        SeatTypeSnapshot = seat.SeatType.ToString(),
                        RowIndexSnapshot = seat.RowIndex,
                        ColIndexSnapshot = seat.ColIndex,
                        Price = basePrice + seat.PriceSurcharge,
                        State = ShowtimeSeatState.Available,
                    });
                }
                await db.SaveChangesAsync(ct);
            }
        }

        logger.LogInformation("TMDB seed hoàn tất: {Count} phim thật", all.Count);
    }

    // -----------------------------------------------------------------------
    private static User CreateUser(int roleId, string fullName, string email, string password, IPasswordService passwordService)
    {
        var user = new User
        {
            RoleId = roleId, FullName = fullName, Email = email,
            NormalizedEmail = email.Trim().ToUpperInvariant(),
            IsActive = true, SecurityVersion = 1, CreatedAtUtc = DateTime.UtcNow,
        };
        user.PasswordHash = passwordService.Hash(user, password);
        return user;
    }

    private static List<Seat> BuildSeats(int roomId, int rows, int cols, int vipFromRow, decimal vipSurcharge)
    {
        var seats = new List<Seat>(rows * cols);
        for (var row = 1; row <= rows; row++)
        for (var col = 1; col <= cols; col++)
        {
            var isVip = row >= vipFromRow;
            seats.Add(new Seat
            {
                RoomId = roomId,
                SeatCode = $"{(char)('A' + row - 1)}{col}",
                RowIndex = row, ColIndex = col,
                SeatType = isVip ? SeatType.VIP : SeatType.Standard,
                PriceSurcharge = isVip ? vipSurcharge : 0m,
                IsActive = true,
            });
        }
        return seats;
    }

    private static List<DateTime> BuildUpcomingStartsUtc(int slotCount)
    {
        var nowUtc = DateTime.UtcNow;
        var first = new DateTime(nowUtc.Year, nowUtc.Month, nowUtc.Day, nowUtc.Hour, 0, 0, DateTimeKind.Utc).AddHours(1);
        while (first < nowUtc.AddMinutes(30)) first = first.AddHours(1);

        return Enumerable.Range(0, slotCount)
                         .Select(i => first.AddMinutes(i * 90))
                         .ToList();
    }

    private static string ToSlug(string text) =>
        new string(text.ToLowerInvariant()
            .Normalize(System.Text.NormalizationForm.FormD)
            .Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c)
                        != System.Globalization.UnicodeCategory.NonSpacingMark)
            .Select(c => char.IsLetterOrDigit(c) ? c : '-')
            .ToArray())
            .Trim('-');
}
