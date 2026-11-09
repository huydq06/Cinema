using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WEB_BTL.Application.DTOs;
using WEB_BTL.Application.Options;
using WEB_BTL.Domain.Entities;
using WEB_BTL.Domain.Enums;
using WEB_BTL.Infrastructure.Concurrency;
using WEB_BTL.Infrastructure.Data;

namespace WEB_BTL.Application.Services;

public record ShowtimeSlotDto(
    int ShowtimeId,
    DateTime StartAtUtc,
    DateTime EndAtUtc,
    decimal BasePrice,
    string RoomName,
    string RoomType,
    string Status,
    int AvailableSeats,
    int TotalSeats);

public record ShowtimeSummaryDto(
    int ShowtimeId,
    string MovieTitle,
    string RoomName,
    DateTime StartAtUtc,
    DateTime EndAtUtc,
    decimal BasePrice,
    string Status,
    int SoldSeats,
    int TotalSeats);

public class ShowtimeInput
{
    public int MovieId { get; set; }
    public int RoomId { get; set; }
    public DateTime StartAtUtc { get; set; }
    public int? CleaningMinutes { get; set; }
    public decimal BasePrice { get; set; }
    public bool PublishImmediately { get; set; }

    /// <summary>Token từ form để phát hiện form cũ; null khi tạo mới.</summary>
    public byte[]? RowVersion { get; set; }
}

public interface IShowtimeService
{
    Task<IReadOnlyList<ShowtimeSlotDto>> GetForMovieOnDateAsync(
        int movieId, DateOnly date, CancellationToken ct = default);

    Task<IReadOnlyList<ShowtimeSummaryDto>> ListAsync(DateOnly? date, CancellationToken ct = default);

    Task<ServiceResult<int>> CreateAsync(ShowtimeInput input, int actorUserId, CancellationToken ct = default);
    Task<ServiceResult> UpdateAsync(int showtimeId, ShowtimeInput input, int actorUserId, CancellationToken ct = default);
    Task<ServiceResult> PublishAsync(int showtimeId, int actorUserId, CancellationToken ct = default);

    Task<ServiceResult> CancelAsync(int showtimeId, string reason, int actorUserId, CancellationToken ct = default);

    Task<ServiceResult> BlockSeatsAsync(int showtimeId, IReadOnlyCollection<int> seatIds, bool block, int actorUserId, CancellationToken ct = default);

    Task<Showtime?> GetEntityAsync(int showtimeId, CancellationToken ct = default);
}

public class ShowtimeService(
    CinemaDbContext db,
    IAppLockService appLock,
    IOptions<ShowtimeOptions> showtimeOptions,
    IOptions<BookingOptions> bookingOptions,
    IAuditService audit,
    ILogger<ShowtimeService> logger) : IShowtimeService
{
    private readonly ShowtimeOptions _options = showtimeOptions.Value;
    private readonly BookingOptions _booking = bookingOptions.Value;

    public async Task<Showtime?> GetEntityAsync(int showtimeId, CancellationToken ct = default) =>
        await db.Showtimes.Include(s => s.Movie).Include(s => s.Room)
            .SingleOrDefaultAsync(s => s.ShowtimeId == showtimeId, ct);

    public async Task<IReadOnlyList<ShowtimeSlotDto>> GetForMovieOnDateAsync(
        int movieId, DateOnly date, CancellationToken ct = default)
    {
        var (dayStartUtc, dayEndUtc) = ToVietnamDayRangeUtc(date);

        return await db.Showtimes.AsNoTracking()
            .Where(s => s.MovieId == movieId
                        && s.Status != ShowtimeStatus.Cancelled
                        && s.StartAtUtc >= dayStartUtc
                        && s.StartAtUtc < dayEndUtc)
            .OrderBy(s => s.StartAtUtc)
            .ThenBy(s => s.ShowtimeId)
            .Select(s => new ShowtimeSlotDto(
                s.ShowtimeId,
                s.StartAtUtc,
                s.EndAtUtc,
                s.BasePrice,
                s.Room!.RoomName,
                s.Room.RoomType.ToString(),
                s.Status.ToString(),
                s.Seats.Count(x => x.State == ShowtimeSeatState.Available),
                s.Seats.Count))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<ShowtimeSummaryDto>> ListAsync(DateOnly? date, CancellationToken ct = default)
    {
        var query = db.Showtimes.AsNoTracking().AsQueryable();
        if (date is not null)
        {
            var (from, to) = ToVietnamDayRangeUtc(date.Value);
            query = query.Where(s => s.StartAtUtc >= from && s.StartAtUtc < to);
        }

        return await query
            .OrderByDescending(s => s.StartAtUtc)
            .Take(200)
            .Select(s => new ShowtimeSummaryDto(
                s.ShowtimeId,
                s.Movie!.Title,
                s.Room!.RoomName,
                s.StartAtUtc,
                s.EndAtUtc,
                s.BasePrice,
                s.Status.ToString(),
                s.Seats.Count(x => x.State == ShowtimeSeatState.Sold || x.State == ShowtimeSeatState.Held),
                s.Seats.Count))
            .ToListAsync(ct);
    }

    public async Task<ServiceResult<int>> CreateAsync(
        ShowtimeInput input, int actorUserId, CancellationToken ct = default)
    {
        var utcNow = DateTime.UtcNow;
        var validation = await ValidateTimingAsync(input, null, ct);
        if (!validation.Succeeded)
        {
            return ServiceResult<int>.Fail(validation.ErrorCode!, validation.ErrorMessage!);
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        if (!await appLock.AcquireAsync(SqlAppLockService.RoomResource(input.RoomId), ct))
        {
            return ServiceResult<int>.Fail(ErrorCodes.ResourceBusy,
                "Phòng đang bận xử lý một thay đổi khác. Vui lòng thử lại.");
        }

        var overlap = await FindOverlapAsync(input.RoomId, input.StartAtUtc, validation.Value, null, ct);
        if (overlap is not null)
        {
            return ServiceResult<int>.Fail(ErrorCodes.ShowtimeOverlap,
                $"Khung giờ bị trùng với suất #{overlap}");
        }

        var movie = await db.Movies.SingleAsync(m => m.MovieId == input.MovieId, ct);
        var cleaning = input.CleaningMinutes ?? _options.CleaningMinutes;

        var showtime = new Showtime
        {
            MovieId = movie.MovieId,
            RoomId = input.RoomId,
            StartAtUtc = input.StartAtUtc,
            EndAtUtc = validation.Value,
            CleaningMinutes = cleaning,
            CleanupEndsAtUtc = validation.Value.AddMinutes(cleaning),
            SalesCloseAtUtc = input.StartAtUtc.AddMinutes(-_booking.SalesCloseMinutes),
            BasePrice = input.BasePrice,
            Status = input.PublishImmediately ? ShowtimeStatus.OnSale : ShowtimeStatus.Draft,
            CreatedAtUtc = utcNow
        };

        db.Showtimes.Add(showtime);
        await db.SaveChangesAsync(ct);

        if (input.PublishImmediately)
        {
            var snapshot = await CreateSeatSnapshotAsync(showtime, ct);
            if (!snapshot.Succeeded)
            {
                await tx.RollbackAsync(ct);
                return ServiceResult<int>.Fail(snapshot.ErrorCode!, snapshot.ErrorMessage!);
            }
        }

        await audit.LogAsync(actorUserId, "Showtime.Created", nameof(Showtime),
            showtime.ShowtimeId.ToString(), new { showtime.StartAtUtc, showtime.RoomId, showtime.MovieId }, ct);

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return ServiceResult<int>.Ok(showtime.ShowtimeId);
    }

    public async Task<ServiceResult> UpdateAsync(
        int showtimeId, ShowtimeInput input, int actorUserId, CancellationToken ct = default)
    {
        var validation = await ValidateTimingAsync(input, showtimeId, ct);
        if (!validation.Succeeded)
        {
            return validation;
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var showtime = await db.Showtimes.SingleOrDefaultAsync(s => s.ShowtimeId == showtimeId, ct);
        if (showtime is null)
        {
            return ServiceResult.Fail(ErrorCodes.NotFound, "Không tìm thấy suất chiếu.");
        }

        // Chuyển phòng phải khóa cả hai phòng theo RoomId tăng dần.
        var locks = new[] { showtime.RoomId, input.RoomId }.Distinct().OrderBy(x => x)
            .Select(SqlAppLockService.RoomResource);
        if (!await appLock.AcquireManyAsync(locks, ct))
        {
            return ServiceResult.Fail(ErrorCodes.ResourceBusy, "Phòng đang bận xử lý một thay đổi khác.");
        }

        await db.Entry(showtime).ReloadAsync(ct);

        if (showtime.Status != ShowtimeStatus.Draft)
        {
            return ServiceResult.Fail(ErrorCodes.InvalidState,
                "Suất đã mở bán: không sửa phòng, thời gian hoặc giá. Hãy hủy và tạo suất mới.");
        }

        if (input.RowVersion is { Length: > 0 } && !showtime.RowVersion.SequenceEqual(input.RowVersion))
        {
            return ServiceResult.Fail(ErrorCodes.InvalidState,
                "Dữ liệu đã được người khác cập nhật. Vui lòng tải lại trang.");
        }

        var overlap = await FindOverlapAsync(input.RoomId, input.StartAtUtc, validation.Value, showtimeId, ct);
        if (overlap is not null)
        {
            return ServiceResult.Fail(ErrorCodes.ShowtimeOverlap, $"Khung giờ bị trùng với suất #{overlap}");
        }

        var movie = await db.Movies.SingleAsync(m => m.MovieId == input.MovieId, ct);
        var cleaning = input.CleaningMinutes ?? _options.CleaningMinutes;

        showtime.MovieId = movie.MovieId;
        showtime.RoomId = input.RoomId;
        showtime.StartAtUtc = input.StartAtUtc;
        showtime.EndAtUtc = validation.Value;
        showtime.CleaningMinutes = cleaning;
        showtime.CleanupEndsAtUtc = validation.Value.AddMinutes(cleaning);
        showtime.SalesCloseAtUtc = input.StartAtUtc.AddMinutes(-_booking.SalesCloseMinutes);
        showtime.BasePrice = input.BasePrice;

        await audit.LogAsync(actorUserId, "Showtime.Updated", nameof(Showtime),
            showtime.ShowtimeId.ToString(), new { showtime.StartAtUtc, showtime.RoomId }, ct);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ServiceResult.Fail(ErrorCodes.InvalidState,
                "Dữ liệu đã được người khác cập nhật. Vui lòng tải lại trang.");
        }

        await tx.CommitAsync(ct);
        return ServiceResult.Ok();
    }

    public async Task<ServiceResult> PublishAsync(int showtimeId, int actorUserId, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var showtime = await db.Showtimes.SingleOrDefaultAsync(s => s.ShowtimeId == showtimeId, ct);
        if (showtime is null)
        {
            return ServiceResult.Fail(ErrorCodes.NotFound, "Không tìm thấy suất chiếu.");
        }

        if (!await appLock.AcquireAsync(SqlAppLockService.RoomResource(showtime.RoomId), ct))
        {
            return ServiceResult.Fail(ErrorCodes.ResourceBusy, "Phòng đang bận. Vui lòng thử lại.");
        }

        await db.Entry(showtime).ReloadAsync(ct);
        if (showtime.Status != ShowtimeStatus.Draft)
        {
            return ServiceResult.Fail(ErrorCodes.InvalidState, "Chỉ mở bán được suất đang ở trạng thái Draft.");
        }

        var snapshot = await CreateSeatSnapshotAsync(showtime, ct);
        if (!snapshot.Succeeded)
        {
            return snapshot;
        }

        await audit.LogAsync(actorUserId, "Showtime.Published", nameof(Showtime),
            showtime.ShowtimeId.ToString(), new { showtime.StartAtUtc }, ct);

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return ServiceResult.Ok();
    }

    /// <summary>
    /// Hủy suất: khóa phòng rồi khóa suất; chặn bán, hủy đơn chờ, hủy đơn đã xác nhận và tạo
    /// yêu cầu hoàn toàn phần. Ghế Sold và vé Issued được giữ để lưu lịch sử.
    /// </summary>
    public async Task<ServiceResult> CancelAsync(
        int showtimeId, string reason, int actorUserId, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var showtime = await db.Showtimes.SingleOrDefaultAsync(s => s.ShowtimeId == showtimeId, ct);
        if (showtime is null)
        {
            return ServiceResult.Fail(ErrorCodes.NotFound, "Không tìm thấy suất chiếu.");
        }

        if (!await appLock.AcquireManyAsync(
            [
                SqlAppLockService.RoomResource(showtime.RoomId),
                SqlAppLockService.ShowtimeResource(showtimeId)
            ], ct))
        {
            return ServiceResult.Fail(ErrorCodes.ResourceBusy, "Suất đang xử lý một thay đổi khác.");
        }

        await db.Entry(showtime).ReloadAsync(ct);
        if (showtime.Status == ShowtimeStatus.Cancelled)
        {
            return ServiceResult.Fail(ErrorCodes.InvalidState, "Suất đã bị hủy trước đó.");
        }

        var utcNow = await db.GetDatabaseUtcNowAsync(ct);
        showtime.Status = ShowtimeStatus.Cancelled;

        var bookings = await db.Bookings
            .Include(b => b.Payments)
            .Where(b => b.ShowtimeId == showtimeId
                        && (b.Status == BookingStatus.PendingPayment || b.Status == BookingStatus.Confirmed))
            .ToListAsync(ct);

        foreach (var booking in bookings)
        {
            var wasConfirmed = booking.Status == BookingStatus.Confirmed;
            booking.Status = BookingStatus.Cancelled;
            booking.CancelledAtUtc = utcNow;
            booking.CancellationReason = reason;

            if (wasConfirmed)
            {
                // Giữ vé Issued và ghế Sold để lưu lịch sử; quyền dùng bị chặn bởi Booking/Showtime.
                foreach (var payment in booking.Payments.Where(p => p.IsAccepted))
                {
                    if (payment.Status == PaymentStatus.Succeeded)
                    {
                        payment.Status = PaymentStatus.RefundPending;
                        payment.RefundRequestedAtUtc = utcNow;
                    }
                }
            }
            else
            {
                foreach (var ticket in await db.BookingTickets
                             .Where(t => t.BookingId == booking.BookingId).ToListAsync(ct))
                {
                    ticket.Status = TicketStatus.Void;
                }

                await db.ShowtimeSeats
                    .Where(s => s.ShowtimeId == showtimeId && s.BookingId == booking.BookingId)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(s => s.State, ShowtimeSeatState.Available)
                        .SetProperty(s => s.BookingId, (long?)null)
                        .SetProperty(s => s.HeldUntilUtc, (DateTime?)null), ct);
            }
        }

        await audit.LogAsync(actorUserId, "Showtime.Cancelled", nameof(Showtime),
            showtimeId.ToString(), new { reason, bookings = bookings.Count }, ct);

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        logger.LogInformation("Hủy suất {ShowtimeId}, {Count} đơn liên quan", showtimeId, bookings.Count);
        return ServiceResult.Ok();
    }

    public async Task<ServiceResult> BlockSeatsAsync(
        int showtimeId, IReadOnlyCollection<int> seatIds, bool block, int actorUserId,
        CancellationToken ct = default)
    {
        if (seatIds.Count == 0)
        {
            return ServiceResult.Fail(ErrorCodes.InvalidSelection, "Chưa chọn ghế.");
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var showtime = await db.Showtimes.SingleOrDefaultAsync(s => s.ShowtimeId == showtimeId, ct);
        if (showtime is null)
        {
            return ServiceResult.Fail(ErrorCodes.NotFound, "Không tìm thấy suất chiếu.");
        }

        if (!await appLock.AcquireAsync(SqlAppLockService.ShowtimeResource(showtimeId), ct))
        {
            return ServiceResult.Fail(ErrorCodes.ResourceBusy, "Suất đang bận. Vui lòng thử lại.");
        }

        var seats = await db.ShowtimeSeats
            .Where(s => s.ShowtimeId == showtimeId && seatIds.Contains(s.SeatId))
            .ToListAsync(ct);

        if (seats.Count != seatIds.Distinct().Count())
        {
            return ServiceResult.Fail(ErrorCodes.NotFound, "Có ghế không thuộc suất chiếu này.");
        }

        if (block && seats.Any(s => s.State is ShowtimeSeatState.Held or ShowtimeSeatState.Sold))
        {
            return ServiceResult.Fail(ErrorCodes.InvalidState,
                "Không thể chặn ghế đang được giữ hoặc đã bán.");
        }

        var target = block ? ShowtimeSeatState.Blocked : ShowtimeSeatState.Available;
        if (!block && seats.Any(s => s.State != ShowtimeSeatState.Blocked))
        {
            return ServiceResult.Fail(ErrorCodes.InvalidState, "Chỉ mở lại được ghế đang bảo trì.");
        }

        foreach (var seat in seats)
        {
            seat.State = target;
        }

        await audit.LogAsync(actorUserId, block ? "Showtime.SeatsBlocked" : "Showtime.SeatsReleased",
            nameof(ShowtimeSeat), showtimeId.ToString(), new { seatIds }, ct);

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return ServiceResult.Ok();
    }

    private async Task<ServiceResult<DateTime>> ValidateTimingAsync(
        ShowtimeInput input, int? showtimeId, CancellationToken ct)
    {
        var movie = await db.Movies.AsNoTracking()
            .SingleOrDefaultAsync(m => m.MovieId == input.MovieId, ct);
        if (movie is null)
        {
            return ServiceResult<DateTime>.Fail(ErrorCodes.ValidationFailed, "Phim không tồn tại.");
        }

        var room = await db.Rooms.AsNoTracking()
            .SingleOrDefaultAsync(r => r.RoomId == input.RoomId, ct);
        if (room is null || !room.IsActive)
        {
            return ServiceResult<DateTime>.Fail(ErrorCodes.ValidationFailed, "Phòng không tồn tại hoặc đã ngưng dùng.");
        }

        if (!await db.Seats.AnyAsync(s => s.RoomId == room.RoomId && s.IsActive, ct))
        {
            return ServiceResult<DateTime>.Fail(ErrorCodes.ValidationFailed,
                "Phòng chưa có ghế đang hoạt động nên không thể mở bán suất.");
        }

        if (input.BasePrice < 0)
        {
            return ServiceResult<DateTime>.Fail(ErrorCodes.ValidationFailed, "Giá gốc không hợp lệ.");
        }

        var startUtc = DateTime.SpecifyKind(input.StartAtUtc, DateTimeKind.Utc);
        if (startUtc <= DateTime.UtcNow)
        {
            return ServiceResult<DateTime>.Fail(ErrorCodes.ValidationFailed,
                "Giờ bắt đầu phải ở tương lai.");
        }

        var end = startUtc.AddMinutes(movie.DurationMinutes);
        return ServiceResult<DateTime>.Ok(end);
    }

    private async Task<int?> FindOverlapAsync(
        int roomId, DateTime startUtc, DateTime endUtc, int? excludeShowtimeId, CancellationToken ct)
    {
        var cleaning = _options.CleaningMinutes;
        var cleanupEnd = endUtc.AddMinutes(cleaning);

        // Hai khoảng nửa mở [Start, CleanupEnd) trùng khi newStart < existingCleanupEnd && newCleanupEnd > existingStart.
        var overlapping = await db.Showtimes.AsNoTracking()
            .Where(s => s.RoomId == roomId
                        && s.Status != ShowtimeStatus.Cancelled
                        && (excludeShowtimeId == null || s.ShowtimeId != excludeShowtimeId)
                        && startUtc < s.CleanupEndsAtUtc
                        && cleanupEnd > s.StartAtUtc)
            .Select(s => (int?)s.ShowtimeId)
            .FirstOrDefaultAsync(ct);

        return overlapping;
    }

    private async Task<ServiceResult> CreateSeatSnapshotAsync(Showtime showtime, CancellationToken ct)
    {
        if (await db.ShowtimeSeats.AnyAsync(s => s.ShowtimeId == showtime.ShowtimeId, ct))
        {
            return ServiceResult.Fail(ErrorCodes.InvalidState, "Suất chiếu đã có sơ đồ ghế.");
        }

        var seats = await db.Seats.AsNoTracking()
            .Where(s => s.RoomId == showtime.RoomId && s.IsActive)
            .ToListAsync(ct);

        if (seats.Count == 0)
        {
            return ServiceResult.Fail(ErrorCodes.ValidationFailed, "Phòng chưa có ghế đang hoạt động.");
        }

        foreach (var seat in seats)
        {
            db.ShowtimeSeats.Add(new ShowtimeSeat
            {
                ShowtimeId = showtime.ShowtimeId,
                SeatId = seat.SeatId,
                RoomId = showtime.RoomId,
                SeatCodeSnapshot = seat.SeatCode,
                SeatTypeSnapshot = seat.SeatType.ToString(),
                RowIndexSnapshot = seat.RowIndex,
                ColIndexSnapshot = seat.ColIndex,
                Price = showtime.BasePrice + seat.PriceSurcharge,
                State = ShowtimeSeatState.Available
            });
        }

        showtime.Status = ShowtimeStatus.OnSale;
        return ServiceResult.Ok();
    }

    private static (DateTime FromUtc, DateTime ToUtc) ToVietnamDayRangeUtc(DateOnly date)
    {
        var from = date.ToDateTime(TimeOnly.MinValue).AddHours(-7);
        return (DateTime.SpecifyKind(from, DateTimeKind.Utc), DateTime.SpecifyKind(from.AddDays(1), DateTimeKind.Utc));
    }
}
