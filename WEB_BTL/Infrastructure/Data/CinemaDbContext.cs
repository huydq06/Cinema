using Microsoft.EntityFrameworkCore;
using WEB_BTL.Domain.Entities;

namespace WEB_BTL.Infrastructure.Data;

public class CinemaDbContext(DbContextOptions<CinemaDbContext> options) : DbContext(options)
{
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Genre> Genres => Set<Genre>();
    public DbSet<Movie> Movies => Set<Movie>();
    public DbSet<MovieGenre> MovieGenres => Set<MovieGenre>();
    public DbSet<Room> Rooms => Set<Room>();
    public DbSet<Seat> Seats => Set<Seat>();
    public DbSet<Showtime> Showtimes => Set<Showtime>();
    public DbSet<ShowtimeSeat> ShowtimeSeats => Set<ShowtimeSeat>();
    public DbSet<FoodCombo> FoodCombos => Set<FoodCombo>();
    public DbSet<Booking> Bookings => Set<Booking>();
    public DbSet<BookingTicket> BookingTickets => Set<BookingTicket>();
    public DbSet<BookingCombo> BookingCombos => Set<BookingCombo>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<PaymentEvent> PaymentEvents => Set<PaymentEvent>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CinemaDbContext).Assembly);
    }

    /// <summary>Đọc giờ UTC từ SQL Server, dùng làm nguồn thời gian duy nhất cho nghiệp vụ.</summary>
    public async Task<DateTime> GetDatabaseUtcNowAsync(CancellationToken ct = default)
    {
        var result = await Database
            .SqlQuery<DateTime>($"SELECT SYSUTCDATETIME() AS Value")
            .SingleAsync(ct);
        return DateTime.SpecifyKind(result, DateTimeKind.Utc);
    }
}
