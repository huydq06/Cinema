using WEB_BTL.Domain.Enums;

namespace WEB_BTL.Domain.Entities;

public class Room
{
    public int RoomId { get; set; }
    public string RoomName { get; set; } = string.Empty;
    public RoomType RoomType { get; set; } = RoomType.Standard2D;
    public bool IsActive { get; set; } = true;
    public byte[] RowVersion { get; set; } = [];

    public ICollection<Seat> Seats { get; set; } = [];
    public ICollection<Showtime> Showtimes { get; set; } = [];
}

/// <summary>
/// Ghế vật lý của phòng. Snapshot theo suất nằm ở <see cref="ShowtimeSeat"/>.
/// </summary>
public class Seat
{
    public int SeatId { get; set; }
    public int RoomId { get; set; }
    public Room? Room { get; set; }

    public string SeatCode { get; set; } = string.Empty;
    public int RowIndex { get; set; }
    public int ColIndex { get; set; }
    public SeatType SeatType { get; set; } = SeatType.Standard;
    public decimal PriceSurcharge { get; set; }
    public bool IsActive { get; set; } = true;

    public ICollection<ShowtimeSeat> ShowtimeSeats { get; set; } = [];
}

public class Showtime
{
    public int ShowtimeId { get; set; }
    public int MovieId { get; set; }
    public Movie? Movie { get; set; }
    public int RoomId { get; set; }
    public Room? Room { get; set; }

    public DateTime StartAtUtc { get; set; }
    public DateTime EndAtUtc { get; set; }
    public int CleaningMinutes { get; set; } = 15;
    public DateTime CleanupEndsAtUtc { get; set; }
    public DateTime SalesCloseAtUtc { get; set; }
    public decimal BasePrice { get; set; }
    public ShowtimeStatus Status { get; set; } = ShowtimeStatus.Draft;

    public DateTime CreatedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];

    public ICollection<ShowtimeSeat> Seats { get; set; } = [];
    public ICollection<Booking> Bookings { get; set; } = [];

    public bool IsSalesOpen(DateTime utcNow) =>
        Status == ShowtimeStatus.OnSale && utcNow < SalesCloseAtUtc;
}
