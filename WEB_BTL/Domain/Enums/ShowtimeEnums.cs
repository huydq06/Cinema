namespace WEB_BTL.Domain.Enums;

/// <summary>Trạng thái suất chiếu.</summary>
public enum ShowtimeStatus
{
    Draft,
    OnSale,
    Closed,
    Completed,
    Cancelled
}

/// <summary>Trạng thái ghế theo suất; nguồn có thẩm quyền về khả năng cấp ghế.</summary>
public enum ShowtimeSeatState
{
    Available,
    Held,
    Sold,
    Blocked
}

/// <summary>Loại ghế trong phòng.</summary>
public enum SeatType
{
    Standard,
    VIP
}

/// <summary>Loại phòng chiếu.</summary>
public enum RoomType
{
    Standard2D,
    ThreeD
}
