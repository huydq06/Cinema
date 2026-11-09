using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WEB_BTL.Domain.Entities;
using WEB_BTL.Domain.Enums;

namespace WEB_BTL.Infrastructure.Data.Configurations;

public class BookingConfiguration : IEntityTypeConfiguration<Booking>
{
    public void Configure(EntityTypeBuilder<Booking> b)
    {
        b.ToTable("Bookings", t =>
        {
            t.HasCheckConstraint("CK_Bookings_Total",
                "[FinalAmount] = [TotalTicketAmount] + [TotalComboAmount]");
            t.HasCheckConstraint("CK_Bookings_Amounts",
                "[TotalTicketAmount] >= 0 AND [TotalComboAmount] >= 0 AND [FinalAmount] >= 0");
            t.HasCheckConstraint("CK_Bookings_Expiry", "[ExpiresAtUtc] > [CreatedAtUtc]");
            t.HasCheckConstraint("CK_Bookings_OnlineUser",
                "([Channel] = 'Online' AND [UserId] IS NOT NULL AND [CreatedByUserId] = [UserId]) " +
                "OR ([Channel] = 'POS')");
        });
        b.HasKey(x => x.BookingId);
        b.Property(x => x.BookingId).UseIdentityColumn();

        // Đích cho FK ghép từ ShowtimeSeats và BookingTickets.
        b.HasAlternateKey(x => new { x.BookingId, x.ShowtimeId });

        b.Property(x => x.BookingCode).HasColumnType("varchar(32)").IsRequired();
        b.HasIndex(x => x.BookingCode).IsUnique();

        b.Property(x => x.Channel).HasConversion<string>().HasColumnType("varchar(10)").IsRequired();
        b.Property(x => x.ContactName).HasColumnType("nvarchar(100)").IsRequired();
        b.Property(x => x.ContactEmail).HasColumnType("nvarchar(254)");
        b.Property(x => x.ContactPhone).HasColumnType("varchar(20)");
        b.Property(x => x.MovieTitleSnapshot).HasColumnType("nvarchar(200)").IsRequired();
        b.Property(x => x.RoomNameSnapshot).HasColumnType("nvarchar(50)").IsRequired();
        b.Property(x => x.ShowStartAtUtcSnapshot).HasColumnType("datetime2(3)");

        b.Property(x => x.TotalTicketAmount).HasColumnType("decimal(18,2)");
        b.Property(x => x.TotalComboAmount).HasColumnType("decimal(18,2)");
        b.Property(x => x.FinalAmount).HasColumnType("decimal(18,2)");

        b.Property(x => x.Status).HasConversion<string>().HasColumnType("varchar(24)").IsRequired();
        b.Property(x => x.CreatedAtUtc).HasColumnType("datetime2(3)").HasDefaultValueSql("SYSUTCDATETIME()");
        b.Property(x => x.ExpiresAtUtc).HasColumnType("datetime2(3)");
        b.Property(x => x.ConfirmedAtUtc).HasColumnType("datetime2(3)");
        b.Property(x => x.CancelledAtUtc).HasColumnType("datetime2(3)");
        b.Property(x => x.CancellationReason).HasColumnType("nvarchar(500)");

        b.Property(x => x.ClientRequestId).IsRequired();
        b.HasIndex(x => x.ClientRequestId).IsUnique();
        b.Property(x => x.RequestHash).HasColumnType("char(64)").IsRequired();
        b.Property(x => x.RowVersion).IsRowVersion();

        // Một khách tối đa một đơn chờ mỗi suất.
        b.HasIndex(x => new { x.UserId, x.ShowtimeId })
            .HasDatabaseName("UX_Bookings_OnePendingPerUserShowtime")
            .IsUnique()
            .HasFilter("[UserId] IS NOT NULL AND [Status] = 'PendingPayment'");

        b.HasIndex(x => new { x.Status, x.ExpiresAtUtc })
            .HasDatabaseName("IX_Bookings_Status_Expires")
            .IncludeProperties(x => x.ShowtimeId);
        b.HasIndex(x => new { x.UserId, x.CreatedAtUtc })
            .HasDatabaseName("IX_Bookings_User_Created")
            .IsDescending(false, true);

        b.HasOne(x => x.User).WithMany(u => u.Bookings).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.CreatedBy).WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Showtime).WithMany(s => s.Bookings).HasForeignKey(x => x.ShowtimeId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class BookingTicketConfiguration : IEntityTypeConfiguration<BookingTicket>
{
    public void Configure(EntityTypeBuilder<BookingTicket> b)
    {
        b.ToTable("BookingTickets", t =>
        {
            t.HasCheckConstraint("CK_BookingTickets_Price", "[UnitPrice] >= 0");
            t.HasCheckConstraint("CK_BookingTickets_Qr",
                "([Status] = 'Issued' AND [QrTokenHash] IS NOT NULL AND [QrTokenProtected] IS NOT NULL) " +
                "OR ([Status] <> 'Issued' AND [QrTokenHash] IS NULL AND [QrTokenProtected] IS NULL)");
            t.HasCheckConstraint("CK_BookingTickets_CheckIn",
                "([CheckedInAtUtc] IS NULL AND [CheckedInByUserId] IS NULL) " +
                "OR ([CheckedInAtUtc] IS NOT NULL AND [CheckedInByUserId] IS NOT NULL AND [Status] = 'Issued')");
        });
        b.HasKey(x => x.TicketId);
        b.Property(x => x.TicketId).UseIdentityColumn();

        b.Property(x => x.SeatCodeSnapshot).HasColumnType("varchar(10)").IsRequired();
        b.Property(x => x.SeatTypeSnapshot).HasColumnType("varchar(20)").IsRequired();
        b.Property(x => x.UnitPrice).HasColumnType("decimal(18,2)");
        b.Property(x => x.Status).HasConversion<string>().HasColumnType("varchar(24)").IsRequired();
        b.Property(x => x.QrTokenHash).HasColumnType("char(64)");
        b.Property(x => x.QrTokenProtected).HasColumnType("nvarchar(1000)");
        b.Property(x => x.CheckedInAtUtc).HasColumnType("datetime2(3)");

        b.HasIndex(x => new { x.BookingId, x.SeatId }).IsUnique();

        // Hàng rào cuối chống phát hai vé Issued cho cùng một ghế trong một suất.
        b.HasIndex(x => new { x.ShowtimeId, x.SeatId })
            .HasDatabaseName("UX_BookingTickets_IssuedSeat")
            .IsUnique()
            .HasFilter($"[Status] = '{nameof(TicketStatus.Issued)}'");

        b.HasIndex(x => x.QrTokenHash)
            .HasDatabaseName("UX_BookingTickets_QrTokenHash")
            .IsUnique()
            .HasFilter("[QrTokenHash] IS NOT NULL");

        b.HasOne(x => x.Booking)
            .WithMany(x => x.Tickets)
            .HasForeignKey(x => new { x.BookingId, x.ShowtimeId })
            .HasPrincipalKey(x => new { x.BookingId, x.ShowtimeId })
            .OnDelete(DeleteBehavior.Restrict);

        b.HasOne<ShowtimeSeat>()
            .WithMany()
            .HasForeignKey(x => new { x.ShowtimeId, x.SeatId })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class BookingComboConfiguration : IEntityTypeConfiguration<BookingCombo>
{
    public void Configure(EntityTypeBuilder<BookingCombo> b)
    {
        b.ToTable("BookingCombos", t =>
        {
            t.HasCheckConstraint("CK_BookingCombos_Quantity", "[Quantity] BETWEEN 1 AND 10");
            t.HasCheckConstraint("CK_BookingCombos_Price", "[UnitPrice] >= 0");
        });
        b.HasKey(x => new { x.BookingId, x.ComboId });
        b.Property(x => x.ComboNameSnapshot).HasColumnType("nvarchar(100)").IsRequired();
        b.Property(x => x.UnitPrice).HasColumnType("decimal(18,2)");

        b.HasOne(x => x.Booking).WithMany(x => x.Combos).HasForeignKey(x => x.BookingId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Combo).WithMany().HasForeignKey(x => x.ComboId).OnDelete(DeleteBehavior.Restrict);
    }
}
