using System.ComponentModel.DataAnnotations;

namespace WEB_BTL.Application.DTOs;

public class PagedResult<T>
{
    public IReadOnlyList<T> Items { get; init; } = [];
    public int Page { get; init; }
    public int PageSize { get; init; }
    public int TotalCount { get; init; }
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
    public bool HasPrevious => Page > 1;
    public bool HasNext => Page < TotalPages;
}

public record MovieListItemDto(
    int MovieId,
    string Title,
    string Slug,
    string PosterUrl,
    int DurationMinutes,
    string AgeRating,
    DateOnly ReleaseDate,
    IReadOnlyList<string> Genres,
    bool IsNowShowing);

public record ShowtimeListItemDto(
    int ShowtimeId,
    DateTime StartAtUtc,
    DateTime SalesCloseAtUtc,
    decimal BasePrice,
    string RoomName,
    string MovieTitle,
    string AgeRating);

public record SeatMapSeatDto(
    int SeatId,
    string SeatCode,
    string SeatType,
    int Row,
    int Column,
    decimal Price,
    string State);

public record SeatMapDto(
    int ShowtimeId,
    string MovieTitle,
    string RoomName,
    DateTime StartAtUtc,
    DateTime SalesCloseAtUtc,
    DateTime ServerNowUtc,
    decimal BasePrice,
    IReadOnlyList<SeatMapSeatDto> Seats);

public record BookingTicketDto(
    long TicketId,
    int SeatId,
    string SeatCode,
    string SeatType,
    decimal UnitPrice,
    string Status,
    string? QrToken,
    DateTime? CheckedInAtUtc);

public record BookingComboDto(int ComboId, string Name, int Quantity, decimal UnitPrice, decimal LineTotal);

public record BookingDto(
    long BookingId,
    string BookingCode,
    string Status,
    string Channel,
    int ShowtimeId,
    string MovieTitle,
    string RoomName,
    DateTime ShowStartAtUtc,
    string ContactName,
    string? ContactEmail,
    string? ContactPhone,
    decimal TicketAmount,
    decimal ComboAmount,
    decimal FinalAmount,
    string Currency,
    DateTime CreatedAtUtc,
    DateTime ExpiresAtUtc,
    DateTime? ConfirmedAtUtc,
    DateTime? CancelledAtUtc,
    IReadOnlyList<BookingTicketDto> Tickets,
    IReadOnlyList<BookingComboDto> Combos);

public class CreateBookingRequest
{
    [Range(1, int.MaxValue)]
    public int ShowtimeId { get; set; }

    [Required]
    [MinLength(1, ErrorMessage = "Vui lòng chọn ít nhất một ghế.")]
    [MaxLength(8, ErrorMessage = "Mỗi đơn tối đa 8 ghế.")]
    public List<int> SeatIds { get; set; } = [];

    public List<ComboSelection> Combos { get; set; } = [];
}

public class ComboSelection
{
    [Range(1, int.MaxValue)]
    public int ComboId { get; set; }

    [Range(1, 10)]
    public int Quantity { get; set; } = 1;
}

public class UpdateSelectionRequest
{
    [Required]
    [MinLength(1)]
    [MaxLength(8)]
    public List<int> SeatIds { get; set; } = [];

    public List<ComboSelection> Combos { get; set; } = [];
}

public class PosBookingRequest : CreateBookingRequest
{
    [Required(ErrorMessage = "Nhập tên khách.")]
    [StringLength(100)]
    public string ContactName { get; set; } = string.Empty;

    [StringLength(20)]
    public string? ContactPhone { get; set; }

    [EmailAddress]
    [StringLength(254)]
    public string? ContactEmail { get; set; }
}

public class CashPaymentRequest
{
    [Required]
    public Guid IdempotencyKey { get; set; }

    /// <summary>Số tiền khách đưa; server chỉ dùng để đối chiếu, không quyết định số tiền thu.</summary>
    public decimal? AmountReceived { get; set; }
}

public class CheckInRequest
{
    [Required(ErrorMessage = "Nhập mã vé.")]
    [StringLength(128)]
    public string Token { get; set; } = string.Empty;
}

public class CheckInResultDto
{
    public long TicketId { get; set; }
    public string SeatCode { get; set; } = string.Empty;
    public string MovieTitle { get; set; } = string.Empty;
    public string RoomName { get; set; } = string.Empty;
    public DateTime CheckedInAtUtc { get; set; }
}
