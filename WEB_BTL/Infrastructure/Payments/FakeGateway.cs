using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using WEB_BTL.Application.Options;

namespace WEB_BTL.Infrastructure.Payments;

public record PaymentInitRequest(
    long PaymentId,
    string MerchantReference,
    string BookingCode,
    decimal Amount,
    string Currency);

public record PaymentInitResult(
    bool Accepted,
    string? RedirectUrl,
    string? ProviderTransactionId,
    string? FailureCode);

public record NormalizedCallback(
    string Provider,
    string EventKey,
    string MerchantReference,
    string? ProviderTransactionId,
    decimal Amount,
    string Currency,
    bool Succeeded,
    string? FailureCode);

public class CallbackVerifyResult
{
    public bool IsValid { get; init; }
    public NormalizedCallback? Callback { get; init; }
    public string? Error { get; init; }

    public static CallbackVerifyResult Invalid(string error) => new() { IsValid = false, Error = error };
    public static CallbackVerifyResult Valid(NormalizedCallback callback) => new() { IsValid = true, Callback = callback };
}

/// <summary>
/// Cổng thanh toán. Adapter chịu trách nhiệm ký/xác minh và chuẩn hóa callback;
/// tầng nghiệp vụ chỉ nhận <see cref="NormalizedCallback"/> đã xác minh.
/// </summary>
public interface IPaymentGateway
{
    string Name { get; }

    Task<PaymentInitResult> CreatePaymentAsync(PaymentInitRequest request, CancellationToken ct = default);

    CallbackVerifyResult VerifyCallback(string payloadBase64, string signature);

    /// <summary>Ký payload callback; dùng để mô phỏng cổng ở môi trường phát triển.</summary>
    string Sign(string payloadBase64);

    string BuildSimulationPayload(NormalizedCallback callback);
}

public class FakeGateway(
    IOptions<PaymentOptions> options,
    IWebHostEnvironment environment,
    ILogger<FakeGateway> logger) : IPaymentGateway
{
    public const string ProviderName = "FakeGateway";
    private readonly PaymentOptions _options = options.Value;

    public string Name => ProviderName;

    public Task<PaymentInitResult> CreatePaymentAsync(PaymentInitRequest request, CancellationToken ct = default)
    {
        // Giả lập độ trễ cổng; không giữ SQL lock trong lúc chờ.
        var url = $"/payments/fake/checkout?reference={Uri.EscapeDataString(request.MerchantReference)}";
        logger.LogInformation("FakeGateway tạo thanh toán {Reference} cho đơn {BookingCode}",
            request.MerchantReference, request.BookingCode);

        return Task.FromResult(new PaymentInitResult(true, url, null, null));
    }

    public string Sign(string payloadBase64)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_options.FakeGatewaySecret));
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(payloadBase64))).ToLowerInvariant();
    }

    public string BuildSimulationPayload(NormalizedCallback callback)
    {
        if (!environment.IsDevelopment())
        {
            throw new InvalidOperationException("Mô phỏng thanh toán chỉ dùng ở môi trường Development.");
        }

        var json = JsonSerializer.Serialize(callback);
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
    }

    public CallbackVerifyResult VerifyCallback(string payloadBase64, string signature)
    {
        if (string.IsNullOrWhiteSpace(payloadBase64) || string.IsNullOrWhiteSpace(signature))
        {
            return CallbackVerifyResult.Invalid("Thiếu payload hoặc chữ ký.");
        }

        var expected = Sign(payloadBase64);
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        var actualBytes = Encoding.UTF8.GetBytes(signature.Trim().ToLowerInvariant());

        if (expectedBytes.Length != actualBytes.Length ||
            !CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes))
        {
            return CallbackVerifyResult.Invalid("Chữ ký không hợp lệ.");
        }

        try
        {
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(payloadBase64));
            var callback = JsonSerializer.Deserialize<NormalizedCallback>(json);
            if (callback is null || string.IsNullOrWhiteSpace(callback.MerchantReference))
            {
                return CallbackVerifyResult.Invalid("Payload không hợp lệ.");
            }

            return CallbackVerifyResult.Valid(callback);
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            return CallbackVerifyResult.Invalid("Không đọc được payload.");
        }
    }
}
