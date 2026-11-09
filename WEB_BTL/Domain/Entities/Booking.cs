using WEB_BTL.Domain.Enums;

namespace WEB_BTL.Domain.Entities;

public class FoodCombo
{
    public int ComboId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal Price { get; set; }
    public string? ImageUrl { get; set; }
    public bool IsAvailable { get; set; } = true;
    public byte[] RowVersion { get; set; } = [];
}

public class Booking
{
    public long BookingId { get; set; }
    public string BookingCode { get; set; } = string.Empty;

    /// <summary>NULL với khách vãng lai tại quầy.</summary>
    public int? UserId { get; set; }
    public User? User { get; set; }
    public int CreatedByUserId { get; set; }
    public User? CreatedBy { get; set; }

    public BookingChannel Channel { get; set; } = BookingChannel.Online;
    public int ShowtimeId { get; set; }
    public Showtime? Showtime { get; set; }

    public string ContactName { get; set; } = string.Empty;
    public string? ContactEmail { get; set; }
    public string? ContactPhone { get; set; }

    public string MovieTitleSnapshot { get; set; } = string.Empty;
    public string RoomNameSnapshot { get; set; } = string.Empty;
    public DateTime ShowStartAtUtcSnapshot { get; set; }

    public decimal TotalTicketAmount { get; set; }
    public decimal TotalComboAmount { get; set; }
    public decimal FinalAmount { get; set; }

    public BookingStatus Status { get; set; } = BookingStatus.PendingPayment;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? ConfirmedAtUtc { get; set; }
    public DateTime? CancelledAtUtc { get; set; }
    public string? CancellationReason { get; set; }

    public Guid ClientRequestId { get; set; }
    public string RequestHash { get; set; } = string.Empty;
    public byte[] RowVersion { get; set; } = [];

    public ICollection<BookingTicket> Tickets { get; set; } = [];
    public ICollection<BookingCombo> Combos { get; set; } = [];
    public ICollection<Payment> Payments { get; set; } = [];
}

public class BookingTicket
{
    public long TicketId { get; set; }
    public long BookingId { get; set; }
    public Booking? Booking { get; set; }
    public int ShowtimeId { get; set; }
    public int SeatId { get; set; }

    public string SeatCodeSnapshot { get; set; } = string.Empty;
    public string SeatTypeSnapshot { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public TicketStatus Status { get; set; } = TicketStatus.Reserved;

    /// <summary>SHA-256 của token QR; chỉ cấp khi vé Issued.</summary>
    public string? QrTokenHash { get; set; }

    /// <summary>Token QR được Data Protection bảo vệ để hiển thị lại cho đúng chủ vé.</summary>
    public string? QrTokenProtected { get; set; }

    public DateTime? CheckedInAtUtc { get; set; }
    public int? CheckedInByUserId { get; set; }
}

public class BookingCombo
{
    public long BookingId { get; set; }
    public Booking? Booking { get; set; }
    public int ComboId { get; set; }
    public FoodCombo? Combo { get; set; }
    public string ComboNameSnapshot { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
}

/// <summary>Nguồn dữ liệu có thẩm quyền về trạng thái ghế theo suất.</summary>
public class ShowtimeSeat
{
    public int ShowtimeId { get; set; }
    public Showtime? Showtime { get; set; }
    public int SeatId { get; set; }
    public Seat? Seat { get; set; }
    public int RoomId { get; set; }

    public string SeatCodeSnapshot { get; set; } = string.Empty;
    public string SeatTypeSnapshot { get; set; } = string.Empty;
    public int RowIndexSnapshot { get; set; }
    public int ColIndexSnapshot { get; set; }
    public decimal Price { get; set; }

    public ShowtimeSeatState State { get; set; } = ShowtimeSeatState.Available;
    public long? BookingId { get; set; }
    public DateTime? HeldUntilUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];
}
