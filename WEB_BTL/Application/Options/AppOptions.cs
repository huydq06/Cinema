namespace WEB_BTL.Application.Options;

public class BookingOptions
{
    public const string SectionName = "Booking";

    public int HoldMinutes { get; set; } = 10;
    public int SalesCloseMinutes { get; set; } = 10;
    public int MaxSeats { get; set; } = 8;
    public int MaxComboQuantity { get; set; } = 10;
    public int MaxPendingBookingsPerMinute { get; set; } = 10;
}

public class ShowtimeOptions
{
    public const string SectionName = "Showtime";

    public int CleaningMinutes { get; set; } = 15;
}

public class PaymentOptions
{
    public const string SectionName = "Payments";

    /// <summary>FakeGateway hoặc Cash (Cash chỉ dùng cho POS).</summary>
    public string Provider { get; set; } = "FakeGateway";
    public string FakeGatewaySecret { get; set; } = "dev-only-change-me-secret";
    public string CallbackBaseUrl { get; set; } = "https://localhost:7001";
    public int PollSeconds { get; set; } = 5;
}

public class UploadOptions
{
    public const string SectionName = "Uploads";

    public string RootPath { get; set; } = "uploads";
    public string PublicBasePath { get; set; } = "/uploads";
    public long MaxBytes { get; set; } = 5 * 1024 * 1024;
    public int MaxPixels { get; set; } = 25_000_000;
}

public class WorkerOptions
{
    public const string SectionName = "Workers";

    public int ExpireIntervalSeconds { get; set; } = 30;
    public int ReconcileIntervalSeconds { get; set; } = 60;
}
