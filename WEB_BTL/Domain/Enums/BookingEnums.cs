namespace WEB_BTL.Domain.Enums;

/// <summary>Kênh bán vé.</summary>
public enum BookingChannel
{
    Online,
    POS
}

/// <summary>Trạng thái đơn đặt vé.</summary>
public enum BookingStatus
{
    PendingPayment,
    Confirmed,
    Cancelled,
    Expired
}

/// <summary>Trạng thái dòng ghế trong một đơn.</summary>
public enum TicketStatus
{
    Reserved,
    Issued,
    Void
}
