using Microsoft.EntityFrameworkCore;
using WEB_BTL.Domain.Enums;
using WEB_BTL.Infrastructure.Data;

namespace WEB_BTL.Application.Services;

public record RevenueReportDto(
    DateOnly FromDate,
    DateOnly ToDate,
    decimal GrossCaptured,
    decimal Refunded,
    decimal NetCashFlow,
    decimal TicketSales,
    decimal ComboSales,
    decimal CancelledShowtimeSales,
    int TicketsIssued,
    int TicketsCheckedIn,
    decimal OccupancyPercent,
    IReadOnlyList<DailyRevenueRow> Daily,
    IReadOnlyList<MovieRevenueRow> TopMovies);

public record DailyRevenueRow(DateOnly Date, decimal Captured, decimal Refunded, int BookingsConfirmed);

public record MovieRevenueRow(string MovieTitle, decimal TicketSales, int TicketsSold);

/// <summary>
/// Báo cáo tính trên giao dịch thực thu/hoàn, không cộng tất cả Bookings.
/// Khoảng ngày Việt Nam được quy đổi thành [fromUtc, toUtc) trước khi truy vấn.
/// </summary>
public interface IReportingService
{
    Task<RevenueReportDto> GetRevenueAsync(DateOnly fromDate, DateOnly toDate, CancellationToken ct = default);
    Task<DateTime> GetUtcNowAsync(CancellationToken ct = default);
}

public class ReportingService(CinemaDbContext db) : IReportingService
{
    public Task<DateTime> GetUtcNowAsync(CancellationToken ct = default) => db.GetDatabaseUtcNowAsync(ct);

    public async Task<RevenueReportDto> GetRevenueAsync(
        DateOnly fromDate, DateOnly toDate, CancellationToken ct = default)
    {
        if (toDate < fromDate)
        {
            (fromDate, toDate) = (toDate, fromDate);
        }

        if (toDate.DayNumber - fromDate.DayNumber > 366)
        {
            fromDate = toDate.AddDays(-366);
        }

        var fromUtc = VietnamDayStartUtc(fromDate);
        var toUtc = VietnamDayStartUtc(toDate.AddDays(1));

        var captured = await db.Payments.AsNoTracking()
            .Where(p => p.CapturedAtUtc >= fromUtc && p.CapturedAtUtc < toUtc)
            .Select(p => new { p.Amount, p.RefundAmount, p.CapturedAtUtc, p.RefundedAtUtc, p.BookingId })
            .ToListAsync(ct);

        var refundedInPeriod = await db.Payments.AsNoTracking()
            .Where(p => p.RefundedAtUtc >= fromUtc && p.RefundedAtUtc < toUtc)
            .SumAsync(p => p.RefundAmount, ct);

        var acceptedBookingIds = await db.Payments.AsNoTracking()
            .Where(p => p.IsAccepted)
            .Select(p => p.BookingId)
            .Distinct()
            .ToListAsync(ct);

        var confirmedBookings = await db.Bookings.AsNoTracking()
            .Where(b => acceptedBookingIds.Contains(b.BookingId))
            .Select(b => new
            {
                b.BookingId,
                b.MovieTitleSnapshot,
                b.Status,
                b.ConfirmedAtUtc,
                b.TotalTicketAmount,
                b.TotalComboAmount
            })
            .ToListAsync(ct);

        var inPeriod = confirmedBookings
            .Where(b => b.ConfirmedAtUtc >= fromUtc && b.ConfirmedAtUtc < toUtc)
            .ToList();

        var ticketSales = inPeriod.Sum(b => b.TotalTicketAmount);
        var comboSales = inPeriod.Sum(b => b.TotalComboAmount);
        var cancelledSales = inPeriod.Where(b => b.Status == BookingStatus.Cancelled)
            .Sum(b => b.TotalTicketAmount + b.TotalComboAmount);

        var ticketsIssued = await db.BookingTickets.AsNoTracking()
            .CountAsync(t => t.Status == TicketStatus.Issued
                             && t.Booking!.Status == BookingStatus.Confirmed
                             && t.Booking.ConfirmedAtUtc >= fromUtc
                             && t.Booking.ConfirmedAtUtc < toUtc, ct);

        var ticketsCheckedIn = await db.BookingTickets.AsNoTracking()
            .CountAsync(t => t.CheckedInAtUtc >= fromUtc && t.CheckedInAtUtc < toUtc, ct);

        var seatStats = await db.ShowtimeSeats.AsNoTracking()
            .Where(s => s.Showtime!.StartAtUtc >= fromUtc && s.Showtime.StartAtUtc < toUtc
                        && s.State != ShowtimeSeatState.Blocked
                        && s.Showtime.Status != ShowtimeStatus.Cancelled)
            .GroupBy(s => 1)
            .Select(g => new
            {
                Total = g.Count(),
                Sold = g.Count(x => x.State == ShowtimeSeatState.Sold)
            })
            .FirstOrDefaultAsync(ct);

        var occupancy = seatStats is null || seatStats.Total == 0
            ? 0m
            : Math.Round(seatStats.Sold * 100m / seatStats.Total, 2);

        var daily = captured
            .GroupBy(p => DateOnly.FromDateTime(p.CapturedAtUtc!.Value.AddHours(7)))
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                var refunded = captured.Where(p => p.RefundedAtUtc is not null
                                                   && DateOnly.FromDateTime(p.RefundedAtUtc.Value.AddHours(7)) == g.Key)
                    .Sum(p => p.RefundAmount);
                return new DailyRevenueRow(
                    g.Key,
                    g.Sum(p => p.Amount),
                    refunded,
                    confirmedBookings.Count(b => b.ConfirmedAtUtc is not null
                                                 && DateOnly.FromDateTime(b.ConfirmedAtUtc.Value.AddHours(7)) == g.Key));
            })
            .ToList();

        // Phim nhiều thể loại không bị cộng trùng: doanh thu nhóm theo phim trong snapshot đơn.
        var topMovies = inPeriod
            .GroupBy(b => b.MovieTitleSnapshot)
            .Select(g => new MovieRevenueRow(g.Key, g.Sum(x => x.TotalTicketAmount), g.Count()))
            .OrderByDescending(x => x.TicketSales)
            .Take(10)
            .ToList();

        var gross = captured.Sum(p => p.Amount);

        return new RevenueReportDto(
            fromDate,
            toDate,
            gross,
            refundedInPeriod,
            gross - refundedInPeriod,
            ticketSales,
            comboSales,
            cancelledSales,
            ticketsIssued,
            ticketsCheckedIn,
            occupancy,
            daily,
            topMovies);
    }

    private static DateTime VietnamDayStartUtc(DateOnly date) =>
        DateTime.SpecifyKind(date.ToDateTime(TimeOnly.MinValue).AddHours(-7), DateTimeKind.Utc);
}
