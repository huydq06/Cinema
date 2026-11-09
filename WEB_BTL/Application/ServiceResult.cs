namespace WEB_BTL.Application;

/// <summary>Kết quả nghiệp vụ có mã lỗi ổn định theo contract API (mục 8.4 đặc tả).</summary>
public class ServiceResult
{
    public bool Succeeded { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    public IReadOnlyList<int> ConflictingSeatIds { get; init; } = [];

    public static ServiceResult Ok() => new() { Succeeded = true };

    public static ServiceResult Fail(string code, string message, IReadOnlyList<int>? seatIds = null) =>
        new() { Succeeded = false, ErrorCode = code, ErrorMessage = message, ConflictingSeatIds = seatIds ?? [] };
}

public class ServiceResult<T> : ServiceResult
{
    public T? Value { get; init; }

    public static ServiceResult<T> Ok(T value) => new() { Succeeded = true, Value = value };

    public new static ServiceResult<T> Fail(string code, string message, IReadOnlyList<int>? seatIds = null) =>
        new() { Succeeded = false, ErrorCode = code, ErrorMessage = message, ConflictingSeatIds = seatIds ?? [] };
}

/// <summary>Mã lỗi nghiệp vụ dùng chung giữa service, MVC và API.</summary>
public static class ErrorCodes
{
    public const string ValidationFailed = "VALIDATION_FAILED";
    public const string InvalidSelection = "INVALID_SELECTION";
    public const string Unauthenticated = "UNAUTHENTICATED";
    public const string Forbidden = "FORBIDDEN";
    public const string NotFound = "RESOURCE_NOT_FOUND";
    public const string SeatUnavailable = "SEAT_UNAVAILABLE";
    public const string BookingExpired = "BOOKING_EXPIRED";
    public const string InvalidState = "INVALID_STATE";
    public const string IdempotencyConflict = "IDEMPOTENCY_CONFLICT";
    public const string AlreadyCheckedIn = "ALREADY_CHECKED_IN";
    public const string ShowtimeOverlap = "SHOWTIME_OVERLAP";
    public const string ResourceBusy = "RESOURCE_BUSY";
    public const string PaymentProviderUnavailable = "PAYMENT_PROVIDER_UNAVAILABLE";
    public const string CashPaymentRequired = "INVALID_SELECTION";

    public static int ToHttpStatus(string code) => code switch
    {
        ValidationFailed or InvalidSelection => 400,
        Unauthenticated => 401,
        Forbidden => 403,
        NotFound => 404,
        SeatUnavailable or BookingExpired or InvalidState or IdempotencyConflict
            or AlreadyCheckedIn or ShowtimeOverlap => 409,
        ResourceBusy or PaymentProviderUnavailable => 503,
        _ => 400
    };
}
