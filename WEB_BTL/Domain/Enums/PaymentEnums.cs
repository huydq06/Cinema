namespace WEB_BTL.Domain.Enums;

/// <summary>Trạng thái một lần thanh toán / hoàn toàn phần.</summary>
public enum PaymentStatus
{
    Created,
    Pending,
    Unknown,
    Failed,
    Succeeded,
    RefundPending,
    Refunded
}

/// <summary>Trạng thái xử lý một sự kiện callback từ cổng thanh toán.</summary>
public enum PaymentEventStatus
{
    Received,
    Processed,
    ReviewRequired
}

/// <summary>Trạng thái xuất bản của phim; trạng thái "đang chiếu" được suy ra từ suất chiếu.</summary>
public enum MoviePublicationStatus
{
    Draft,
    Published,
    Archived
}
