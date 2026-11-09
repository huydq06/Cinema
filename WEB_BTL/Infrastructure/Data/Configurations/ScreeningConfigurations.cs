using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WEB_BTL.Domain.Entities;

namespace WEB_BTL.Infrastructure.Data.Configurations;

public class RoomConfiguration : IEntityTypeConfiguration<Room>
{
    public void Configure(EntityTypeBuilder<Room> b)
    {
        b.ToTable("Rooms");
        b.HasKey(x => x.RoomId);
        b.Property(x => x.RoomId).UseIdentityColumn();
        b.Property(x => x.RoomName).HasColumnType("nvarchar(50)").IsRequired();
        b.HasIndex(x => x.RoomName).IsUnique();
        b.Property(x => x.RoomType).HasConversion<string>().HasColumnType("varchar(20)").IsRequired();
        b.Property(x => x.IsActive).HasDefaultValue(true);
        b.Property(x => x.RowVersion).IsRowVersion();
    }
}

public class SeatConfiguration : IEntityTypeConfiguration<Seat>
{
    public void Configure(EntityTypeBuilder<Seat> b)
    {
        b.ToTable("Seats", t =>
        {
            t.HasCheckConstraint("CK_Seats_RowIndex", "[RowIndex] > 0");
            t.HasCheckConstraint("CK_Seats_ColIndex", "[ColIndex] > 0");
            t.HasCheckConstraint("CK_Seats_Surcharge", "[PriceSurcharge] >= 0");
        });
        b.HasKey(x => x.SeatId);
        b.Property(x => x.SeatId).UseIdentityColumn();

        // Đích cho FK ghép từ ShowtimeSeats bảo đảm ghế và suất cùng phòng.
        b.HasAlternateKey(x => new { x.SeatId, x.RoomId });

        b.Property(x => x.SeatCode).HasColumnType("varchar(10)").IsRequired();
        b.Property(x => x.SeatType).HasConversion<string>().HasColumnType("varchar(20)").IsRequired();
        b.Property(x => x.PriceSurcharge).HasColumnType("decimal(18,2)");
        b.Property(x => x.IsActive).HasDefaultValue(true);

        b.HasIndex(x => new { x.RoomId, x.SeatCode }).IsUnique();
        b.HasIndex(x => new { x.RoomId, x.RowIndex, x.ColIndex }).IsUnique();

        b.HasOne(x => x.Room).WithMany(r => r.Seats).HasForeignKey(x => x.RoomId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class ShowtimeConfiguration : IEntityTypeConfiguration<Showtime>
{
    public void Configure(EntityTypeBuilder<Showtime> b)
    {
        b.ToTable("Showtimes", t =>
        {
            t.HasCheckConstraint("CK_Showtimes_Time", "[StartAtUtc] < [EndAtUtc] AND [EndAtUtc] <= [CleanupEndsAtUtc]");
            t.HasCheckConstraint("CK_Showtimes_SalesClose", "[SalesCloseAtUtc] <= [StartAtUtc]");
            t.HasCheckConstraint("CK_Showtimes_Cleaning", "[CleaningMinutes] BETWEEN 0 AND 60");
            t.HasCheckConstraint("CK_Showtimes_BasePrice", "[BasePrice] >= 0");
        });
        b.HasKey(x => x.ShowtimeId);
        b.Property(x => x.ShowtimeId).UseIdentityColumn();

        // Đích cho FK ghép từ ShowtimeSeats bảo đảm ghế và suất cùng phòng.
        b.HasAlternateKey(x => new { x.ShowtimeId, x.RoomId });

        b.Property(x => x.StartAtUtc).HasColumnType("datetime2(3)");
        b.Property(x => x.EndAtUtc).HasColumnType("datetime2(3)");
        b.Property(x => x.CleanupEndsAtUtc).HasColumnType("datetime2(3)");
        b.Property(x => x.SalesCloseAtUtc).HasColumnType("datetime2(3)");
        b.Property(x => x.BasePrice).HasColumnType("decimal(18,2)");
        b.Property(x => x.Status).HasConversion<string>().HasColumnType("varchar(24)").IsRequired();
        b.Property(x => x.CleaningMinutes).HasDefaultValue(15);
        b.Property(x => x.CreatedAtUtc).HasColumnType("datetime2(3)").HasDefaultValueSql("SYSUTCDATETIME()");
        b.Property(x => x.RowVersion).IsRowVersion();

        b.HasIndex(x => new { x.RoomId, x.StartAtUtc })
            .HasDatabaseName("IX_Showtimes_Room_Start")
            .IncludeProperties(x => new { x.CleanupEndsAtUtc, x.Status });
        b.HasIndex(x => new { x.MovieId, x.StartAtUtc })
            .HasDatabaseName("IX_Showtimes_Movie_Start")
            .IncludeProperties(x => new { x.Status, x.SalesCloseAtUtc });

        b.HasOne(x => x.Movie).WithMany(m => m.Showtimes).HasForeignKey(x => x.MovieId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Room).WithMany(r => r.Showtimes).HasForeignKey(x => x.RoomId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class ShowtimeSeatConfiguration : IEntityTypeConfiguration<ShowtimeSeat>
{
    public void Configure(EntityTypeBuilder<ShowtimeSeat> b)
    {
        b.ToTable("ShowtimeSeats", t =>
        {
            t.HasCheckConstraint("CK_ShowtimeSeats_State",
                "([State] IN ('Available','Blocked') AND [BookingId] IS NULL AND [HeldUntilUtc] IS NULL) " +
                "OR ([State] = 'Held' AND [BookingId] IS NOT NULL AND [HeldUntilUtc] IS NOT NULL) " +
                "OR ([State] = 'Sold' AND [BookingId] IS NOT NULL AND [HeldUntilUtc] IS NULL)");
            t.HasCheckConstraint("CK_ShowtimeSeats_Price", "[Price] >= 0");
        });

        // Mỗi ghế có đúng một dòng trong một suất.
        b.HasKey(x => new { x.ShowtimeId, x.SeatId });

        b.Property(x => x.SeatCodeSnapshot).HasColumnType("varchar(10)").IsRequired();
        b.Property(x => x.SeatTypeSnapshot).HasColumnType("varchar(20)").IsRequired();
        b.Property(x => x.Price).HasColumnType("decimal(18,2)");
        b.Property(x => x.State).HasConversion<string>().HasColumnType("varchar(24)").IsRequired();
        b.Property(x => x.HeldUntilUtc).HasColumnType("datetime2(3)");
        b.Property(x => x.RowVersion).IsRowVersion();

        b.HasIndex(x => new { x.ShowtimeId, x.State, x.HeldUntilUtc })
            .HasDatabaseName("IX_ShowtimeSeats_Showtime_State")
            .IncludeProperties(x => x.BookingId);

        // FK ghép: suất ghế thuộc đúng phòng của suất và đúng phòng của ghế.
        b.HasOne(x => x.Showtime)
            .WithMany(s => s.Seats)
            .HasForeignKey(x => new { x.ShowtimeId, x.RoomId })
            .HasPrincipalKey(s => new { s.ShowtimeId, s.RoomId })
            .OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.Seat)
            .WithMany(s => s.ShowtimeSeats)
            .HasForeignKey(x => new { x.SeatId, x.RoomId })
            .HasPrincipalKey(s => new { s.SeatId, s.RoomId })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class FoodComboConfiguration : IEntityTypeConfiguration<FoodCombo>
{
    public void Configure(EntityTypeBuilder<FoodCombo> b)
    {
        b.ToTable("FoodCombos", t => t.HasCheckConstraint("CK_FoodCombos_Price", "[Price] >= 0"));
        b.HasKey(x => x.ComboId);
        b.Property(x => x.ComboId).UseIdentityColumn();
        b.Property(x => x.Name).HasColumnType("nvarchar(100)").IsRequired();
        b.Property(x => x.Description).HasColumnType("nvarchar(500)");
        b.Property(x => x.Price).HasColumnType("decimal(18,2)");
        b.Property(x => x.ImageUrl).HasColumnType("nvarchar(500)");
        b.Property(x => x.IsAvailable).HasDefaultValue(true);
        b.Property(x => x.RowVersion).IsRowVersion();
    }
}
