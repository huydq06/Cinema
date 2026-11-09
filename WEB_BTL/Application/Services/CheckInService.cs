using Microsoft.EntityFrameworkCore;
using WEB_BTL.Application.DTOs;
using WEB_BTL.Domain.Entities;
using WEB_BTL.Domain.Enums;
using WEB_BTL.Infrastructure.Concurrency;
using WEB_BTL.Infrastructure.Data;
using WEB_BTL.Infrastructure.Security;

namespace WEB_BTL.Application.Services;

public interface ICheckInService
{
    Task<ServiceResult<CheckInResultDto>> CheckInAsync(
        string token, int staffUserId, CancellationToken ct = default);
}

public class CheckInService(
    CinemaDbContext db,
    IAppLockService appLock,
    IQrTokenService qrTokens,
    IAuditService audit,
    ILogger<CheckInService> logger) : ICheckInService
{
    public async Task<ServiceResult<CheckInResultDto>> CheckInAsync(
        string token, int staffUserId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return ServiceResult<CheckInResultDto>.Fail(ErrorCodes.ValidationFailed, "Thiếu mã vé.");
        }

        var hash = qrTokens.HashOf(token.Trim());

        var ticketRef = await db.BookingTickets.AsNoTracking()
            .Where(t => t.QrTokenHash == hash)
            .Select(t => new
            {
                t.TicketId,
                t.Status,
                t.BookingId,
                t.ShowtimeId,
                t.SeatCodeSnapshot,
                ShowtimeStatus = t.Booking!.Showtime!.Status,
                BookingStatus = t.Booking.Status,
                StartAtUtc = t.Booking.Showtime.StartAtUtc,
                EndAtUtc = t.Booking.Showtime.EndAtUtc,
                MovieTitle = t.Booking.MovieTitleSnapshot,
                RoomName = t.Booking.RoomNameSnapshot
            })
            .SingleOrDefaultAsync(ct);

        if (ticketRef is null)
        {
            return ServiceResult<CheckInResultDto>.Fail(ErrorCodes.NotFound, "Không tìm thấy vé.");
        }

        if (ticketRef.Status != TicketStatus.Issued || ticketRef.BookingStatus != BookingStatus.Confirmed)
        {
            return ServiceResult<CheckInResultDto>.Fail(ErrorCodes.InvalidState, "Vé không hợp lệ để soát.");
        }

        if (ticketRef.ShowtimeStatus == ShowtimeStatus.Cancelled)
        {
            return ServiceResult<CheckInResultDto>.Fail(ErrorCodes.InvalidState,
                "Suất chiếu đã bị hủy. Vé không còn hiệu lực.");
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        if (!await appLock.AcquireAsync(SqlAppLockService.ShowtimeResource(ticketRef.ShowtimeId), ct))
        {
            return ServiceResult<CheckInResultDto>.Fail(ErrorCodes.ResourceBusy, "Suất đang bận. Thử lại sau.");
        }

        var utcNow = await db.GetDatabaseUtcNowAsync(ct);
        var windowStart = ticketRef.StartAtUtc.AddMinutes(-30);
        if (utcNow < windowStart)
        {
            return ServiceResult<CheckInResultDto>.Fail(ErrorCodes.InvalidState,
                $"Chưa tới giờ soát vé. Cửa soát mở từ {windowStart:HH:mm} UTC.");
        }

        if (utcNow > ticketRef.EndAtUtc)
        {
            return ServiceResult<CheckInResultDto>.Fail(ErrorCodes.InvalidState, "Suất chiếu đã kết thúc.");
        }

        var ticket = await db.BookingTickets.SingleAsync(t => t.TicketId == ticketRef.TicketId, ct);
        if (ticket.CheckedInAtUtc is not null)
        {
            return ServiceResult<CheckInResultDto>.Fail(ErrorCodes.AlreadyCheckedIn,
                $"Vé đã được soát lúc {ticket.CheckedInAtUtc:HH:mm:ss} UTC.");
        }

        ticket.CheckedInAtUtc = utcNow;
        ticket.CheckedInByUserId = staffUserId;

        await audit.LogAsync(staffUserId, "Ticket.CheckedIn", nameof(BookingTicket),
            ticket.TicketId.ToString(), new { ticket.BookingId, ticket.SeatCodeSnapshot }, ct);

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        logger.LogInformation("Soát vé {TicketId} bởi {StaffId}", ticket.TicketId, staffUserId);

        return ServiceResult<CheckInResultDto>.Ok(new CheckInResultDto
        {
            TicketId = ticket.TicketId,
            SeatCode = ticket.SeatCodeSnapshot,
            MovieTitle = ticketRef.MovieTitle,
            RoomName = ticketRef.RoomName,
            CheckedInAtUtc = utcNow
        });
    }
}
