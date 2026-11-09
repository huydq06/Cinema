using WEB_BTL.Domain.Enums;

namespace WEB_BTL.Domain.Entities;

public class Payment
{
    public long PaymentId { get; set; }
    public long BookingId { get; set; }
    public Booking? Booking { get; set; }

    public string Provider { get; set; } = string.Empty;
    public string MerchantReference { get; set; } = string.Empty;
    public string? ProviderTransactionId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "VND";

    public PaymentStatus Status { get; set; } = PaymentStatus.Created;
    public bool IsAccepted { get; set; }

    public Guid ClientRequestId { get; set; }
    public string RequestHash { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }
    public DateTime? CapturedAtUtc { get; set; }

    public decimal RefundAmount { get; set; }
    public DateTime? RefundRequestedAtUtc { get; set; }
    public DateTime? RefundedAtUtc { get; set; }
    public string? ProviderRefundReference { get; set; }

    public int? RecordedByUserId { get; set; }
    public string? FailureCode { get; set; }
    public byte[] RowVersion { get; set; } = [];

    public ICollection<PaymentEvent> Events { get; set; } = [];
}

public class PaymentEvent
{
    public long PaymentEventId { get; set; }
    public long PaymentId { get; set; }
    public Payment? Payment { get; set; }

    public string Provider { get; set; } = string.Empty;

    /// <summary>ID sự kiện tin cậy từ cổng hoặc hash payload nghiệp vụ đã chuẩn hóa.</summary>
    public string EventKey { get; set; } = string.Empty;
    public string PayloadHash { get; set; } = string.Empty;
    public DateTime ReceivedAtUtc { get; set; }
    public DateTime? ProcessedAtUtc { get; set; }
    public PaymentEventStatus ProcessingStatus { get; set; } = PaymentEventStatus.Received;
    public string? ResultCode { get; set; }
}

public class AuditLog
{
    public long AuditLogId { get; set; }
    public int? ActorUserId { get; set; }
    public User? Actor { get; set; }
    public string Action { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public DateTime OccurredAtUtc { get; set; }
    public string TraceId { get; set; } = string.Empty;
    public string? SummaryJson { get; set; }
}
