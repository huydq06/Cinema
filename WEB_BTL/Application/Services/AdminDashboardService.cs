using Microsoft.EntityFrameworkCore;
using WEB_BTL.Domain.Enums;
using WEB_BTL.Infrastructure.Data;

namespace WEB_BTL.Application.Services;

public record DashboardStatsDto(
    int UpcomingShowtimes,
    int TodayBookings,
    int PendingPayments,
    decimal TodayCaptured,
    IReadOnlyList<ShowtimeSummaryDto> TodayShowtimes);

public interface IAdminDashboardService
{
    Task<DashboardStatsDto> GetAsync(CancellationToken ct = default);
}

public class AdminDashboardService(CinemaDbContext db, IShowtimeService showtimes) : IAdminDashboardService
{
    public async Task<DashboardStatsDto> GetAsync(CancellationToken ct = default)
    {
        var utcNow = await db.GetDatabaseUtcNowAsync(ct);
        var todayVn = DateOnly.FromDateTime(utcNow.AddHours(7));
        var dayStartUtc = DateTime.SpecifyKind(
            todayVn.ToDateTime(TimeOnly.MinValue).AddHours(-7), DateTimeKind.Utc);
        var dayEndUtc = dayStartUtc.AddDays(1);

        var upcoming = await db.Showtimes.AsNoTracking()
            .CountAsync(s => s.Status == ShowtimeStatus.OnSale
                             && s.StartAtUtc > utcNow && s.StartAtUtc < utcNow.AddDays(7), ct);

        var todayBookings = await db.Bookings.AsNoTracking()
            .CountAsync(b => b.CreatedAtUtc >= dayStartUtc && b.CreatedAtUtc < dayEndUtc, ct);

        var pending = await db.Bookings.AsNoTracking()
            .CountAsync(b => b.Status == BookingStatus.PendingPayment, ct);

        var captured = await db.Payments.AsNoTracking()
            .Where(p => p.CapturedAtUtc >= dayStartUtc && p.CapturedAtUtc < dayEndUtc)
            .SumAsync(p => p.Amount, ct);

        var showtimeList = await showtimes.ListAsync(todayVn, ct);

        return new DashboardStatsDto(upcoming, todayBookings, pending, captured, showtimeList);
    }
}
