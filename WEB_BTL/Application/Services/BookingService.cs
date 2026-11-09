using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WEB_BTL.Application.DTOs;
using WEB_BTL.Application.Options;
using WEB_BTL.Domain.Entities;
using WEB_BTL.Domain.Enums;
using WEB_BTL.Infrastructure.Concurrency;
using WEB_BTL.Infrastructure.Data;

namespace WEB_BTL.Application.Services;

public record PosContactInfo(string ContactName, string? ContactPhone, string? ContactEmail);

public record ComboOptionDto(int ComboId, string Name, string? Description, decimal Price, string? ImageUrl);

public interface IBookingService
{
    Task<ServiceResult<BookingDto>> CreateAsync(
        int actingUserId, int? customerUserId, CreateBookingRequest request, Guid idempotencyKey,
        bool isPos, PosContactInfo? contact, CancellationToken ct = default);

    Task<ServiceResult<BookingDto>> UpdateSelectionAsync(
        long bookingId, int userId, UpdateSelectionRequest request, CancellationToken ct = default);

    Task<ServiceResult> CancelAsync(long bookingId, int userId, bool isStaff, string? reason,
        CancellationToken ct = default);

    Task<BookingDto?> GetForUserAsync(long bookingId, int userId, CancellationToken ct = default);
    Task<BookingDto?> GetForStaffAsync(long bookingId, CancellationToken ct = default);
    Task<IReadOnlyList<BookingDto>> ListForUserAsync(int userId, CancellationToken ct = default);

    /// <summary>Sơ đồ ghế công khai; ghế Held đã quá hạn được hiển thị là Available.</summary>
    Task<ServiceResult<SeatMapDto>> GetSeatMapAsync(int showtimeId, CancellationToken ct = default);

    Task<IReadOnlyList<ComboOptionDto>> ListAvailableCombosAsync(CancellationToken ct = default);

    Task<int> ExpireDueBookingsAsync(int showtimeId, DateTime utcNow, CancellationToken ct = default);
    Task<int> ExpireAllDueBookingsAsync(CancellationToken ct = default);
}

public class BookingService(
    CinemaDbContext db,
    IAppLockService appLock,
    IOptions<BookingOptions> options,
    IAuditService audit,
    IQrTokenServiceHolder qrTokens,
    ILogger<BookingService> logger) : IBookingService
{
    private readonly BookingOptions _options = options.Value;

    public async Task<ServiceResult<BookingDto>> CreateAsync(
        int actingUserId, int? customerUserId, CreateBookingRequest request, Guid idempotencyKey,
        bool isPos, PosContactInfo? contact, CancellationToken ct = default)
    {
        var seatIds = request.SeatIds.Distinct().ToList();
        if (seatIds.Count != request.SeatIds.Count)
        {
            return ServiceResult<BookingDto>.Fail(ErrorCodes.ValidationFailed,
                "Danh sách ghế có ghế trùng nhau.");
        }

        if (seatIds.Count is < 1 || seatIds.Count > _options.MaxSeats)
        {
            return ServiceResult<BookingDto>.Fail(ErrorCodes.ValidationFailed,
                $"Mỗi đơn phải có từ 1 đến {_options.MaxSeats} ghế.");
        }

        var combos = NormalizeCombos(request.Combos);
        if (combos is null)
        {
            return ServiceResult<BookingDto>.Fail(ErrorCodes.ValidationFailed,
                "Combo hoặc số lượng combo không hợp lệ.");
        }

        if (!isPos && customerUserId is null)
        {
            return ServiceResult<BookingDto>.Fail(ErrorCodes.Unauthenticated,
                "Vui lòng đăng nhập để đặt vé online.");
        }

        var requestHash = HashPayload(request.ShowtimeId, seatIds, combos);

        var showtime = await db.Showtimes.AsNoTracking()
            .SingleOrDefaultAsync(s => s.ShowtimeId == request.ShowtimeId, ct);
        if (showtime is null)
        {
            return ServiceResult<BookingDto>.Fail(ErrorCodes.NotFound, "Không tìm thấy suất chiếu.");
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        if (!await appLock.AcquireAsync(SqlAppLockService.ShowtimeResource(showtime.ShowtimeId), ct))
        {
            return ServiceResult<BookingDto>.Fail(ErrorCodes.ResourceBusy,
                "Suất chiếu đang xử lý giao dịch khác. Vui lòng thử lại sau.");
        }

        // Đọc lại sau khi có khóa, dùng giờ DB làm nguồn thời gian.
        var utcNow = await db.GetDatabaseUtcNowAsync(ct);

        var existingByKey = await db.Bookings.AsNoTracking()
            .SingleOrDefaultAsync(b => b.ClientRequestId == idempotencyKey, ct);
        if (existingByKey is not null)
        {
            if (existingByKey.CreatedByUserId != actingUserId || existingByKey.RequestHash != requestHash)
            {
                return ServiceResult<BookingDto>.Fail(ErrorCodes.IdempotencyConflict,
                    "Yêu cầu trùng mã nhưng khác nội dung hoặc khác người gửi.");
            }

            var dto = await BuildDtoAsync(existingByKey.BookingId, ct);
            return ServiceResult<BookingDto>.Ok(dto!);
        }

        await ExpireDueBookingsAsync(showtime.ShowtimeId, utcNow, ct);

        showtime = await db.Showtimes.AsNoTracking().SingleAsync(s => s.ShowtimeId == showtime.ShowtimeId, ct);
        if (showtime.Status != ShowtimeStatus.OnSale || utcNow >= showtime.SalesCloseAtUtc)
        {
            return ServiceResult<BookingDto>.Fail(ErrorCodes.InvalidState,
                "Suất chiếu đã ngừng bán vé.");
        }

        if (!isPos && customerUserId is not null)
        {
            var hasPending = await db.Bookings.AnyAsync(b =>
                b.UserId == customerUserId && b.ShowtimeId == showtime.ShowtimeId
                && b.Status == BookingStatus.PendingPayment && b.ExpiresAtUtc > utcNow, ct);
            if (hasPending)
            {
                return ServiceResult<BookingDto>.Fail(ErrorCodes.InvalidState,
                    "Bạn đang có một đơn chờ thanh toán cho suất này. Hãy thanh toán hoặc hủy đơn cũ trước.");
            }
        }

        var seats = await db.ShowtimeSeats
            .Where(s => s.ShowtimeId == showtime.ShowtimeId && seatIds.Contains(s.SeatId))
            .ToListAsync(ct);

        if (seats.Count != seatIds.Count)
        {
            return ServiceResult<BookingDto>.Fail(ErrorCodes.NotFound, "Có ghế không thuộc suất chiếu này.");
        }

        var unavailable = seats.Where(s => s.State != ShowtimeSeatState.Available).Select(s => s.SeatId).ToList();
        if (unavailable.Count > 0)
        {
            return ServiceResult<BookingDto>.Fail(ErrorCodes.SeatUnavailable,
                "Một số ghế không còn khả dụng.", unavailable);
        }

        var comboRows = await LoadCombosAsync(combos, ct);
        if (comboRows is null)
        {
            return ServiceResult<BookingDto>.Fail(ErrorCodes.ValidationFailed,
                "Có combo không còn bán.");
        }

        var expiresAt = utcNow.AddMinutes(_options.HoldMinutes);
        if (expiresAt > showtime.SalesCloseAtUtc)
        {
            expiresAt = showtime.SalesCloseAtUtc;
        }

        if (expiresAt <= utcNow)
        {
            return ServiceResult<BookingDto>.Fail(ErrorCodes.InvalidState, "Đã quá giờ ngừng bán vé.");
        }

        var movie = await db.Movies.AsNoTracking().SingleAsync(m => m.MovieId == showtime.MovieId, ct);
        var room = await db.Rooms.AsNoTracking().SingleAsync(r => r.RoomId == showtime.RoomId, ct);

        var booking = new Booking
        {
            BookingCode = GenerateBookingCode(),
            UserId = isPos ? null : customerUserId,
            CreatedByUserId = actingUserId,
            Channel = isPos ? BookingChannel.POS : BookingChannel.Online,
            ShowtimeId = showtime.ShowtimeId,
            ContactName = (contact?.ContactName ?? string.Empty).Trim(),
            ContactEmail = contact?.ContactEmail?.Trim(),
            ContactPhone = contact?.ContactPhone?.Trim(),
            MovieTitleSnapshot = movie.Title,
            RoomNameSnapshot = room.RoomName,
            ShowStartAtUtcSnapshot = showtime.StartAtUtc,
            Status = BookingStatus.PendingPayment,
            CreatedAtUtc = utcNow,
            ExpiresAtUtc = expiresAt,
            ClientRequestId = idempotencyKey,
            RequestHash = requestHash
        };

        if (!isPos)
        {
            var customer = await db.Users.AsNoTracking().SingleAsync(u => u.UserId == customerUserId, ct);
            booking.ContactName = customer.FullName;
            booking.ContactEmail = customer.Email;
            booking.ContactPhone = customer.Phone;
        }
        else if (string.IsNullOrWhiteSpace(booking.ContactName))
        {
            booking.ContactName = "Khách vãng lai";
        }

        decimal ticketAmount = 0;
        foreach (var seat in seats)
        {
            ticketAmount += seat.Price;
            booking.Tickets.Add(new BookingTicket
            {
                ShowtimeId = showtime.ShowtimeId,
                SeatId = seat.SeatId,
                SeatCodeSnapshot = seat.SeatCodeSnapshot,
                SeatTypeSnapshot = seat.SeatTypeSnapshot,
                UnitPrice = seat.Price,
                Status = TicketStatus.Reserved
            });
        }

        decimal comboAmount = 0;
        foreach (var row in comboRows)
        {
            comboAmount += row.Combo.Price * row.Quantity;
            booking.Combos.Add(new BookingCombo
            {
                ComboId = row.Combo.ComboId,
                ComboNameSnapshot = row.Combo.Name,
                Quantity = row.Quantity,
                UnitPrice = row.Combo.Price
            });
        }

        booking.TotalTicketAmount = ticketAmount;
        booking.TotalComboAmount = comboAmount;
        booking.FinalAmount = ticketAmount + comboAmount;

        db.Bookings.Add(booking);
        await db.SaveChangesAsync(ct);

        foreach (var seat in seats)
        {
            seat.State = ShowtimeSeatState.Held;
            seat.BookingId = booking.BookingId;
            seat.HeldUntilUtc = expiresAt;
        }

        await audit.LogAsync(actingUserId, "Booking.Created", nameof(Booking), booking.BookingId.ToString(),
            new { booking.BookingCode, booking.FinalAmount, seats = seatIds.Count, isPos }, ct);

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        logger.LogInformation("Tạo đơn {BookingCode} giữ {Seats} ghế tới {ExpiresAt:o}",
            booking.BookingCode, seatIds.Count, expiresAt);

        var result = await BuildDtoAsync(booking.BookingId, ct);
        return ServiceResult<BookingDto>.Ok(result!);
    }

    public async Task<ServiceResult<BookingDto>> UpdateSelectionAsync(
        long bookingId, int userId, UpdateSelectionRequest request, CancellationToken ct = default)
    {
        var seatIds = request.SeatIds.Distinct().ToList();
        if (seatIds.Count < 1 || seatIds.Count > _options.MaxSeats)
        {
            return ServiceResult<BookingDto>.Fail(ErrorCodes.ValidationFailed,
                $"Mỗi đơn phải có từ 1 đến {_options.MaxSeats} ghế.");
        }

        var combos = NormalizeCombos(request.Combos);
        if (combos is null)
        {
            return ServiceResult<BookingDto>.Fail(ErrorCodes.ValidationFailed, "Combo không hợp lệ.");
        }

        var bookingRef = await db.Bookings.AsNoTracking()
            .Where(b => b.BookingId == bookingId)
            .Select(b => new { b.BookingId, b.ShowtimeId, b.UserId, b.Status, b.ExpiresAtUtc })
            .SingleOrDefaultAsync(ct);

        if (bookingRef is null || bookingRef.UserId != userId)
        {
            return ServiceResult<BookingDto>.Fail(ErrorCodes.NotFound, "Không tìm thấy đơn.");
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        if (!await appLock.AcquireAsync(SqlAppLockService.ShowtimeResource(bookingRef.ShowtimeId), ct))
        {
            return ServiceResult<BookingDto>.Fail(ErrorCodes.ResourceBusy, "Suất đang bận. Vui lòng thử lại.");
        }

        var utcNow = await db.GetDatabaseUtcNowAsync(ct);
        var booking = await db.Bookings
            .Include(b => b.Tickets)
            .Include(b => b.Combos)
            .SingleAsync(b => b.BookingId == bookingId, ct);

        if (booking.UserId != userId)
        {
            return ServiceResult<BookingDto>.Fail(ErrorCodes.NotFound, "Không tìm thấy đơn.");
        }

        if (booking.Status != BookingStatus.PendingPayment)
        {
            return ServiceResult<BookingDto>.Fail(ErrorCodes.InvalidState, "Đơn không còn ở trạng thái chờ thanh toán.");
        }

        if (booking.ExpiresAtUtc <= utcNow)
        {
            return ServiceResult<BookingDto>.Fail(ErrorCodes.BookingExpired, "Đơn đã hết hạn giữ ghế.");
        }

        if (await db.Payments.AnyAsync(p => p.BookingId == bookingId, ct))
        {
            return ServiceResult<BookingDto>.Fail(ErrorCodes.InvalidState,
                "Đơn đã có lần thanh toán. Hãy thanh toán tiếp hoặc hủy đơn và tạo đơn mới.");
        }

        var seats = await db.ShowtimeSeats
            .Where(s => s.ShowtimeId == booking.ShowtimeId && seatIds.Contains(s.SeatId))
            .ToListAsync(ct);

        if (seats.Count != seatIds.Count)
        {
            return ServiceResult<BookingDto>.Fail(ErrorCodes.NotFound, "Có ghế không thuộc suất chiếu này.");
        }

        var notAvailable = seats
            .Where(s => !(s.State == ShowtimeSeatState.Available
                          || (s.State == ShowtimeSeatState.Held && s.BookingId == bookingId)))
            .Select(s => s.SeatId)
            .ToList();
        if (notAvailable.Count > 0)
        {
            return ServiceResult<BookingDto>.Fail(ErrorCodes.SeatUnavailable,
                "Một số ghế không còn khả dụng.", notAvailable);
        }

        var comboRows = await LoadCombosAsync(combos, ct);
        if (comboRows is null)
        {
            return ServiceResult<BookingDto>.Fail(ErrorCodes.ValidationFailed, "Có combo không còn bán.");
        }

        // Giải phóng ghế bỏ chọn.
        var allHeldSeats = await db.ShowtimeSeats
            .Where(s => s.ShowtimeId == booking.ShowtimeId && s.BookingId == bookingId)
            .ToListAsync(ct);

        foreach (var seat in allHeldSeats.Where(s => !seatIds.Contains(s.SeatId)))
        {
            seat.State = ShowtimeSeatState.Available;
            seat.BookingId = null;
            seat.HeldUntilUtc = null;
        }

        foreach (var seat in seats)
        {
            seat.State = ShowtimeSeatState.Held;
            seat.BookingId = bookingId;
            seat.HeldUntilUtc = booking.ExpiresAtUtc; // Không gia hạn khi đổi lựa chọn.
        }

        db.BookingTickets.RemoveRange(booking.Tickets);
        db.BookingCombos.RemoveRange(booking.Combos);
        booking.Tickets.Clear();
        booking.Combos.Clear();

        decimal ticketAmount = 0;
        foreach (var seat in seats)
        {
            ticketAmount += seat.Price;
            booking.Tickets.Add(new BookingTicket
            {
                BookingId = bookingId,
                ShowtimeId = booking.ShowtimeId,
                SeatId = seat.SeatId,
                SeatCodeSnapshot = seat.SeatCodeSnapshot,
                SeatTypeSnapshot = seat.SeatTypeSnapshot,
                UnitPrice = seat.Price,
                Status = TicketStatus.Reserved
            });
        }

        decimal comboAmount = 0;
        foreach (var row in comboRows)
        {
            comboAmount += row.Combo.Price * row.Quantity;
            booking.Combos.Add(new BookingCombo
            {
                BookingId = bookingId,
                ComboId = row.Combo.ComboId,
                ComboNameSnapshot = row.Combo.Name,
                Quantity = row.Quantity,
                UnitPrice = row.Combo.Price
            });
        }

        booking.TotalTicketAmount = ticketAmount;
        booking.TotalComboAmount = comboAmount;
        booking.FinalAmount = ticketAmount + comboAmount;

        await audit.LogAsync(userId, "Booking.SelectionChanged", nameof(Booking), bookingId.ToString(),
            new { seats = seatIds.Count }, ct);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ServiceResult<BookingDto>.Fail(ErrorCodes.SeatUnavailable,
                "Một số ghế vừa được người khác chọn. Vui lòng tải lại sơ đồ.");
        }

        await tx.CommitAsync(ct);
        return ServiceResult<BookingDto>.Ok((await BuildDtoAsync(bookingId, ct))!);
    }

    public async Task<ServiceResult> CancelAsync(
        long bookingId, int userId, bool isStaff, string? reason, CancellationToken ct = default)
    {
        var reference = await db.Bookings.AsNoTracking()
            .Where(b => b.BookingId == bookingId)
            .Select(b => new { b.ShowtimeId, b.UserId })
            .SingleOrDefaultAsync(ct);

        if (reference is null || (!isStaff && reference.UserId != userId))
        {
            return ServiceResult.Fail(ErrorCodes.NotFound, "Không tìm thấy đơn.");
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        if (!await appLock.AcquireAsync(SqlAppLockService.ShowtimeResource(reference.ShowtimeId), ct))
        {
            return ServiceResult.Fail(ErrorCodes.ResourceBusy, "Suất đang bận. Vui lòng thử lại.");
        }

        var utcNow = await db.GetDatabaseUtcNowAsync(ct);
        var booking = await db.Bookings
            .Include(b => b.Tickets)
            .SingleAsync(b => b.BookingId == bookingId, ct);

        if (!isStaff && booking.UserId != userId)
        {
            return ServiceResult.Fail(ErrorCodes.NotFound, "Không tìm thấy đơn.");
        }

        if (booking.Status != BookingStatus.PendingPayment)
        {
            return ServiceResult.Fail(ErrorCodes.InvalidState,
                booking.Status == BookingStatus.Confirmed
                    ? "Vé đã thanh toán không tự hủy được. Vui lòng liên hệ rạp."
                    : "Đơn không còn ở trạng thái chờ thanh toán.");
        }

        booking.Status = BookingStatus.Cancelled;
        booking.CancelledAtUtc = utcNow;
        booking.CancellationReason = reason ?? (isStaff ? "Nhân viên hủy" : "Khách hủy");

        foreach (var ticket in booking.Tickets)
        {
            ticket.Status = TicketStatus.Void;
        }

        await ReleaseSeatsAsync(booking.ShowtimeId, bookingId, ct);

        await audit.LogAsync(userId, "Booking.Cancelled", nameof(Booking), bookingId.ToString(),
            new { reason = booking.CancellationReason }, ct);

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return ServiceResult.Ok();
    }

    public async Task<BookingDto?> GetForUserAsync(long bookingId, int userId, CancellationToken ct = default)
    {
        var owner = await db.Bookings.AsNoTracking()
            .Where(b => b.BookingId == bookingId)
            .Select(b => b.UserId)
            .SingleOrDefaultAsync(ct);

        return owner != userId ? null : await BuildDtoAsync(bookingId, ct, revealQr: true);
    }

    public Task<BookingDto?> GetForStaffAsync(long bookingId, CancellationToken ct = default) =>
        BuildDtoAsync(bookingId, ct, revealQr: false);

    public async Task<IReadOnlyList<BookingDto>> ListForUserAsync(int userId, CancellationToken ct = default)
    {
        var ids = await db.Bookings.AsNoTracking()
            .Where(b => b.UserId == userId)
            .OrderByDescending(b => b.CreatedAtUtc)
            .Take(50)
            .Select(b => b.BookingId)
            .ToListAsync(ct);

        var list = new List<BookingDto>(ids.Count);
        foreach (var id in ids)
        {
            var dto = await BuildDtoAsync(id, ct, revealQr: false);
            if (dto is not null)
            {
                list.Add(dto);
            }
        }

        return list;
    }

    public async Task<ServiceResult<SeatMapDto>> GetSeatMapAsync(int showtimeId, CancellationToken ct = default)
    {
        var showtime = await db.Showtimes.AsNoTracking()
            .Where(s => s.ShowtimeId == showtimeId)
            .Select(s => new
            {
                s.ShowtimeId,
                s.StartAtUtc,
                s.SalesCloseAtUtc,
                s.BasePrice,
                s.Status,
                MovieTitle = s.Movie!.Title,
                RoomName = s.Room!.RoomName
            })
            .SingleOrDefaultAsync(ct);

        if (showtime is null || showtime.Status == ShowtimeStatus.Cancelled)
        {
            return ServiceResult<SeatMapDto>.Fail(ErrorCodes.NotFound, "Không tìm thấy suất chiếu.");
        }

        var utcNow = await db.GetDatabaseUtcNowAsync(ct);

        var seats = await db.ShowtimeSeats.AsNoTracking()
            .Where(s => s.ShowtimeId == showtimeId)
            .OrderBy(s => s.RowIndexSnapshot)
            .ThenBy(s => s.ColIndexSnapshot)
            .Select(s => new
            {
                s.SeatId,
                s.SeatCodeSnapshot,
                s.SeatTypeSnapshot,
                s.RowIndexSnapshot,
                s.ColIndexSnapshot,
                s.Price,
                s.State,
                s.HeldUntilUtc
            })
            .ToListAsync(ct);

        var seatDtos = seats
            .Select(s => new SeatMapSeatDto(
                s.SeatId,
                s.SeatCodeSnapshot,
                s.SeatTypeSnapshot,
                s.RowIndexSnapshot,
                s.ColIndexSnapshot,
                s.Price,
                // Giữ chỗ quá hạn sẽ được worker dọn; hiển thị như còn trống để khách chọn lại.
                s.State == ShowtimeSeatState.Held && s.HeldUntilUtc is { } until && until <= utcNow
                    ? nameof(ShowtimeSeatState.Available)
                    : s.State.ToString()))
            .ToList();

        return ServiceResult<SeatMapDto>.Ok(new SeatMapDto(
            showtime.ShowtimeId,
            showtime.MovieTitle,
            showtime.RoomName,
            showtime.StartAtUtc,
            showtime.SalesCloseAtUtc,
            utcNow,
            showtime.BasePrice,
            seatDtos));
    }

    public async Task<IReadOnlyList<ComboOptionDto>> ListAvailableCombosAsync(CancellationToken ct = default)
    {
        return await db.FoodCombos.AsNoTracking()
            .Where(c => c.IsAvailable)
            .OrderBy(c => c.Price)
            .Select(c => new ComboOptionDto(c.ComboId, c.Name, c.Description, c.Price, c.ImageUrl))
            .ToListAsync(ct);
    }

    /// <summary>Hết hạn các đơn chờ của một suất. Phải gọi bên trong khóa suất.</summary>
    public async Task<int> ExpireDueBookingsAsync(int showtimeId, DateTime utcNow, CancellationToken ct = default)
    {
        var due = await db.Bookings
            .Include(b => b.Tickets)
            .Where(b => b.ShowtimeId == showtimeId
                        && b.Status == BookingStatus.PendingPayment
                        && b.ExpiresAtUtc <= utcNow)
            .ToListAsync(ct);

        foreach (var booking in due)
        {
            booking.Status = BookingStatus.Expired;
            foreach (var ticket in booking.Tickets)
            {
                ticket.Status = TicketStatus.Void;
            }

            await ReleaseSeatsAsync(showtimeId, booking.BookingId, ct);
        }

        if (due.Count > 0)
        {
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Hết hạn {Count} đơn của suất {ShowtimeId}", due.Count, showtimeId);
        }

        return due.Count;
    }

    /// <summary>Worker quét toàn bộ đơn tới hạn, xử lý từng suất một để transaction ngắn.</summary>
    public async Task<int> ExpireAllDueBookingsAsync(CancellationToken ct = default)
    {
        var utcNow = await db.GetDatabaseUtcNowAsync(ct);
        var showtimeIds = await db.Bookings.AsNoTracking()
            .Where(b => b.Status == BookingStatus.PendingPayment && b.ExpiresAtUtc <= utcNow)
            .Select(b => b.ShowtimeId)
            .Distinct()
            .ToListAsync(ct);

        var total = 0;
        foreach (var showtimeId in showtimeIds)
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            if (!await appLock.AcquireAsync(SqlAppLockService.ShowtimeResource(showtimeId), ct))
            {
                continue;
            }

            total += await ExpireDueBookingsAsync(showtimeId, utcNow, ct);
            await tx.CommitAsync(ct);
        }

        return total;
    }

    private async Task ReleaseSeatsAsync(int showtimeId, long bookingId, CancellationToken ct)
    {
        await db.ShowtimeSeats
            .Where(s => s.ShowtimeId == showtimeId && s.BookingId == bookingId
                        && s.State == ShowtimeSeatState.Held)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(s => s.State, ShowtimeSeatState.Available)
                .SetProperty(s => s.BookingId, (long?)null)
                .SetProperty(s => s.HeldUntilUtc, (DateTime?)null), ct);
    }

    private async Task<BookingDto?> BuildDtoAsync(long bookingId, CancellationToken ct, bool revealQr = false)
    {
        var booking = await db.Bookings.AsNoTracking()
            .Include(b => b.Tickets)
            .Include(b => b.Combos)
            .SingleOrDefaultAsync(b => b.BookingId == bookingId, ct);

        if (booking is null)
        {
            return null;
        }

        var tickets = booking.Tickets
            .OrderBy(t => t.SeatCodeSnapshot)
            .Select(t => new BookingTicketDto(
                t.TicketId,
                t.SeatId,
                t.SeatCodeSnapshot,
                t.SeatTypeSnapshot,
                t.UnitPrice,
                t.Status.ToString(),
                revealQr && t.Status == TicketStatus.Issued && t.QrTokenProtected is not null
                    ? qrTokens.Unprotect(t.QrTokenProtected)
                    : null,
                t.CheckedInAtUtc))
            .ToList();

        return new BookingDto(
            booking.BookingId,
            booking.BookingCode,
            booking.Status.ToString(),
            booking.Channel.ToString(),
            booking.ShowtimeId,
            booking.MovieTitleSnapshot,
            booking.RoomNameSnapshot,
            booking.ShowStartAtUtcSnapshot,
            booking.ContactName,
            booking.ContactEmail,
            booking.ContactPhone,
            booking.TotalTicketAmount,
            booking.TotalComboAmount,
            booking.FinalAmount,
            "VND",
            booking.CreatedAtUtc,
            booking.ExpiresAtUtc,
            booking.ConfirmedAtUtc,
            booking.CancelledAtUtc,
            tickets,
            booking.Combos
                .OrderBy(c => c.ComboNameSnapshot)
                .Select(c => new BookingComboDto(c.ComboId, c.ComboNameSnapshot, c.Quantity,
                    c.UnitPrice, c.UnitPrice * c.Quantity))
                .ToList());
    }

    private List<(int ComboId, int Quantity)>? NormalizeCombos(List<ComboSelection>? input)
    {
        if (input is null || input.Count == 0)
        {
            return [];
        }

        var result = new List<(int, int)>();
        foreach (var group in input.GroupBy(x => x.ComboId))
        {
            var quantity = group.Sum(x => x.Quantity);
            if (group.Key <= 0 || quantity is < 1 || quantity > _options.MaxComboQuantity)
            {
                return null;
            }

            result.Add((group.Key, quantity));
        }

        return result;
    }

    private async Task<List<(FoodCombo Combo, int Quantity)>?> LoadCombosAsync(
        List<(int ComboId, int Quantity)> combos, CancellationToken ct)
    {
        if (combos.Count == 0)
        {
            return [];
        }

        var ids = combos.Select(c => c.ComboId).ToList();
        var rows = await db.FoodCombos.AsNoTracking()
            .Where(c => ids.Contains(c.ComboId) && c.IsAvailable)
            .ToListAsync(ct);

        if (rows.Count != ids.Count)
        {
            return null;
        }

        return combos.Select(c => (rows.Single(r => r.ComboId == c.ComboId), c.Quantity)).ToList();
    }

    private static string HashPayload(
        int showtimeId, List<int> seatIds, List<(int ComboId, int Quantity)> combos)
    {
        var orderedSeats = seatIds.OrderBy(x => x);
        var orderedCombos = combos.OrderBy(c => c.ComboId);
        var payload = $"{showtimeId}|{string.Join(',', orderedSeats)}|" +
                      string.Join(',', orderedCombos.Select(c => $"{c.ComboId}x{c.Quantity}"));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
    }

    private static string GenerateBookingCode() =>
        $"CIN-{Convert.ToHexString(RandomNumberGenerator.GetBytes(6))}";
}

/// <summary>
/// Vỏ bọc lấy token QR từ phạm vi request; tách riêng để service không phụ thuộc trực tiếp
/// vào Data Protection khi chạy unit test.
/// </summary>
public interface IQrTokenServiceHolder
{
    string? Unprotect(string protectedToken);
}

public class QrTokenServiceHolder(Infrastructure.Security.IQrTokenService inner) : IQrTokenServiceHolder
{
    public string? Unprotect(string protectedToken) => inner.Unprotect(protectedToken);
}
