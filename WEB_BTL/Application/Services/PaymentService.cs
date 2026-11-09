using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WEB_BTL.Application.DTOs;
using WEB_BTL.Application.Options;
using WEB_BTL.Domain.Entities;
using WEB_BTL.Domain.Enums;
using WEB_BTL.Infrastructure.Concurrency;
using WEB_BTL.Infrastructure.Data;
using WEB_BTL.Infrastructure.Payments;
using WEB_BTL.Infrastructure.Security;

namespace WEB_BTL.Application.Services;

public record PaymentStartDto(
    long PaymentId,
    string MerchantReference,
    string Provider,
    decimal Amount,
    string Status,
    string? RedirectUrl,
    string? SimulationPayload,
    string? SimulationSignature);

public record CallbackOutcome(bool Ack, string Code, string Message, long? BookingId);

public interface IPaymentService
{
    Task<ServiceResult<PaymentStartDto>> StartAsync(
        long bookingId, int userId, Guid idempotencyKey, CancellationToken ct = default);

    Task<CallbackOutcome> HandleCallbackAsync(
        CallbackVerifyResult verified, string rawPayloadHash, CancellationToken ct = default);

    Task<ServiceResult<PaymentStartDto>> CashPaymentAsync(
        long bookingId, int staffUserId, CashPaymentRequest request, CancellationToken ct = default);

    Task<ServiceResult> RefundAsync(long paymentId, int adminUserId, CancellationToken ct = default);

    Task<PaymentStatusDto?> GetStatusAsync(long bookingId, int userId, CancellationToken ct = default);

    Task<PaymentStatusDto?> GetStatusByReferenceAsync(
        string merchantReference, int userId, CancellationToken ct = default);

    /// <summary>Dữ liệu mô phỏng cổng cho môi trường phát triển; null nếu không phải FakeGateway.</summary>
    Task<FakeCheckoutDto?> GetSimulationAsync(
        string merchantReference, int userId, CancellationToken ct = default);

    /// <summary>
    /// Mô phỏng cổng gọi callback. Đi đúng luồng xác minh chữ ký và idempotency như callback thật.
    /// </summary>
    Task<CallbackOutcome?> SimulateCallbackAsync(
        string merchantReference, int userId, CancellationToken ct = default);
}

public record FakeCheckoutDto(
    PaymentStatusDto Status,
    string Reference,
    string PayloadBase64,
    string Signature);

public record PaymentStatusDto(
    long BookingId,
    string BookingCode,
    string BookingStatus,
    decimal FinalAmount,
    string PaymentStatus,
    decimal CapturedAmount);

public class PaymentService(
    CinemaDbContext db,
    IAppLockService appLock,
    IPaymentGateway gateway,
    IQrTokenService qrTokens,
    IOptions<BookingOptions> options,
    IOptions<PaymentOptions> paymentOptions,
    IAuditService audit,
    ILogger<PaymentService> logger) : IPaymentService
{
    private readonly BookingOptions _options = options.Value;
    private readonly PaymentOptions _paymentOptions = paymentOptions.Value;

    public async Task<ServiceResult<PaymentStartDto>> StartAsync(
        long bookingId, int userId, Guid idempotencyKey, CancellationToken ct = default)
    {
        var reference = await db.Bookings.AsNoTracking()
            .Where(b => b.BookingId == bookingId)
            .Select(b => new { b.ShowtimeId, b.UserId, b.Status, b.ExpiresAtUtc, b.FinalAmount })
            .SingleOrDefaultAsync(ct);

        if (reference is null || reference.UserId != userId)
        {
            return ServiceResult<PaymentStartDto>.Fail(ErrorCodes.NotFound, "Không tìm thấy đơn.");
        }

        if (reference.Status != BookingStatus.PendingPayment)
        {
            return ServiceResult<PaymentStartDto>.Fail(ErrorCodes.InvalidState,
                "Đơn không ở trạng thái có thể thanh toán.");
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        if (!await appLock.AcquireAsync(SqlAppLockService.ShowtimeResource(reference.ShowtimeId), ct))
        {
            return ServiceResult<PaymentStartDto>.Fail(ErrorCodes.ResourceBusy, "Suất đang bận. Vui lòng thử lại.");
        }

        var utcNow = await db.GetDatabaseUtcNowAsync(ct);
        var booking = await db.Bookings.SingleAsync(b => b.BookingId == bookingId, ct);

        if (booking.Status != BookingStatus.PendingPayment)
        {
            return ServiceResult<PaymentStartDto>.Fail(ErrorCodes.InvalidState, "Đơn không thể thanh toán.");
        }

        if (booking.ExpiresAtUtc <= utcNow)
        {
            return ServiceResult<PaymentStartDto>.Fail(ErrorCodes.BookingExpired,
                "Đơn đã hết hạn giữ ghế. Vui lòng chọn lại ghế.");
        }

        if (await db.Payments.AnyAsync(p => p.BookingId == bookingId && p.IsAccepted, ct))
        {
            return ServiceResult<PaymentStartDto>.Fail(ErrorCodes.InvalidState, "Đơn đã được thanh toán.");
        }

        // Idempotency theo key của lần thanh toán.
        var byKey = await db.Payments.AsNoTracking()
            .SingleOrDefaultAsync(p => p.ClientRequestId == idempotencyKey, ct);
        if (byKey is not null)
        {
            if (byKey.BookingId != bookingId)
            {
                return ServiceResult<PaymentStartDto>.Fail(ErrorCodes.IdempotencyConflict,
                    "Mã yêu cầu đã dùng cho giao dịch khác.");
            }

            return ServiceResult<PaymentStartDto>.Ok(
                await ToStartDtoAsync(byKey, ct));
        }

        // Chỉ một attempt đang mở cho mỗi đơn.
        var open = await db.Payments
            .Where(p => p.BookingId == bookingId
                        && (p.Status == PaymentStatus.Created
                            || p.Status == PaymentStatus.Pending
                            || p.Status == PaymentStatus.Unknown))
            .OrderByDescending(p => p.CreatedAtUtc)
            .FirstOrDefaultAsync(ct);
        if (open is not null)
        {
            return ServiceResult<PaymentStartDto>.Ok(await ToStartDtoAsync(open, ct));
        }

        var payment = new Payment
        {
            BookingId = bookingId,
            Provider = gateway.Name,
            MerchantReference = $"MR-{Guid.NewGuid():N}".ToUpperInvariant()[..35],
            Amount = booking.FinalAmount,
            Currency = "VND",
            Status = PaymentStatus.Created,
            ClientRequestId = idempotencyKey,
            RequestHash = RequestHash.Compute(bookingId, booking.FinalAmount, "VND"),
            CreatedAtUtc = utcNow
        };
        db.Payments.Add(payment);
        await db.SaveChangesAsync(ct);

        // Commit trước khi gọi cổng: không giữ SQL lock qua mạng.
        await tx.CommitAsync(ct);

        PaymentInitResult init;
        try
        {
            init = await gateway.CreatePaymentAsync(
                new PaymentInitRequest(payment.PaymentId, payment.MerchantReference,
                    booking.BookingCode, payment.Amount, payment.Currency), ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Không gọi được cổng thanh toán cho {Reference}", payment.MerchantReference);
            await MarkOpenAttemptAsync(payment.PaymentId, PaymentStatus.Unknown, null, "PROVIDER_TIMEOUT", ct);
            return ServiceResult<PaymentStartDto>.Fail(ErrorCodes.PaymentProviderUnavailable,
                "Cổng thanh toán không phản hồi. Hệ thống sẽ đối soát lại giao dịch.");
        }

        if (!init.Accepted)
        {
            await MarkOpenAttemptAsync(payment.PaymentId, PaymentStatus.Failed, null, init.FailureCode, ct);
            return ServiceResult<PaymentStartDto>.Fail(ErrorCodes.PaymentProviderUnavailable,
                "Cổng thanh toán từ chối giao dịch.");
        }

        await MarkOpenAttemptAsync(payment.PaymentId, PaymentStatus.Pending,
            init.ProviderTransactionId, null, ct);

        var fresh = await db.Payments.AsNoTracking().SingleAsync(p => p.PaymentId == payment.PaymentId, ct);
        var dto = await ToStartDtoAsync(fresh, ct);

        if (gateway is FakeGateway fake && init.RedirectUrl is not null)
        {
            var callback = new NormalizedCallback(
                fake.Name,
                $"INIT-{fresh.MerchantReference}",
                fresh.MerchantReference,
                init.ProviderTransactionId ?? $"FT-{fresh.PaymentId}",
                fresh.Amount,
                fresh.Currency,
                true,
                null);
            var payload = fake.BuildSimulationPayload(callback);
            dto = dto with { SimulationPayload = payload, SimulationSignature = fake.Sign(payload) };
        }

        return ServiceResult<PaymentStartDto>.Ok(dto);
    }

    /// <summary>
    /// Xử lý callback đã xác minh chữ ký. Idempotent theo (Provider, EventKey) và
    /// nguyên tử theo khóa suất: hoặc cấp vé, hoặc chuyển RefundPending.
    /// </summary>
    public async Task<CallbackOutcome> HandleCallbackAsync(
        CallbackVerifyResult verified, string rawPayloadHash, CancellationToken ct = default)
    {
        if (!verified.IsValid || verified.Callback is null)
        {
            logger.LogWarning("Callback không hợp lệ: {Error}", verified.Error);
            return new CallbackOutcome(false, "INVALID_SIGNATURE",
                verified.Error ?? "Callback không hợp lệ.", null);
        }

        var callback = verified.Callback;
        var paymentRef = await db.Payments.AsNoTracking()
            .Where(p => p.MerchantReference == callback.MerchantReference && p.Provider == callback.Provider)
            .Select(p => new { p.PaymentId, p.BookingId, p.Amount, p.Currency, p.Status })
            .SingleOrDefaultAsync(ct);

        if (paymentRef is null)
        {
            return new CallbackOutcome(false, "PAYMENT_NOT_FOUND", "Không tìm thấy giao dịch.", null);
        }

        if (paymentRef.Amount != callback.Amount || !string.Equals(paymentRef.Currency, callback.Currency,
                StringComparison.OrdinalIgnoreCase))
        {
            return new CallbackOutcome(false, "AMOUNT_MISMATCH", "Số tiền hoặc tiền tệ không khớp.", null);
        }

        var bookingShowtimeId = await db.Bookings.AsNoTracking()
            .Where(b => b.BookingId == paymentRef.BookingId)
            .Select(b => b.ShowtimeId)
            .SingleAsync(ct);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        if (!await appLock.AcquireAsync(SqlAppLockService.ShowtimeResource(bookingShowtimeId), ct))
        {
            // Không ACK như đã xử lý xong; cổng sẽ retry.
            return new CallbackOutcome(false, "RESOURCE_BUSY", "Hệ thống đang bận, thử lại sau.", null);
        }

        // Idempotent theo sự kiện.
        var duplicate = await db.PaymentEvents.AsNoTracking()
            .SingleOrDefaultAsync(e => e.Provider == callback.Provider && e.EventKey == callback.EventKey, ct);
        if (duplicate is not null)
        {
            if (duplicate.PayloadHash != rawPayloadHash)
            {
                db.PaymentEvents.Add(new PaymentEvent
                {
                    PaymentId = paymentRef.PaymentId,
                    Provider = callback.Provider,
                    EventKey = $"{callback.EventKey}-{Guid.NewGuid():N}"[..Math.Min(128, callback.EventKey.Length)],
                    PayloadHash = rawPayloadHash,
                    ReceivedAtUtc = DateTime.UtcNow,
                    ProcessedAtUtc = DateTime.UtcNow,
                    ProcessingStatus = PaymentEventStatus.ReviewRequired,
                    ResultCode = "EVENT_PAYLOAD_MISMATCH"
                });
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                logger.LogError("EventKey {EventKey} lặp với payload khác - cần đối soát", callback.EventKey);
                return new CallbackOutcome(true, "REVIEW_REQUIRED",
                    "Sự kiện đã ghi nhận để đối soát.", paymentRef.BookingId);
            }

            await tx.CommitAsync(ct);
            return new CallbackOutcome(true, "DUPLICATE", "Sự kiện đã xử lý.", paymentRef.BookingId);
        }

        var utcNow = await db.GetDatabaseUtcNowAsync(ct);
        var payment = await db.Payments.SingleAsync(p => p.PaymentId == paymentRef.PaymentId, ct);
        var booking = await db.Bookings
            .Include(b => b.Tickets)
            .SingleAsync(b => b.BookingId == payment.BookingId, ct);

        var eventRow = new PaymentEvent
        {
            PaymentId = payment.PaymentId,
            Provider = callback.Provider,
            EventKey = callback.EventKey,
            PayloadHash = rawPayloadHash,
            ReceivedAtUtc = utcNow,
            ProcessingStatus = PaymentEventStatus.Processed,
            ResultCode = callback.Succeeded ? "SUCCESS" : (callback.FailureCode ?? "FAILED")
        };
        db.PaymentEvents.Add(eventRow);

        if (!callback.Succeeded)
        {
            if (payment.Status is PaymentStatus.Created or PaymentStatus.Pending or PaymentStatus.Unknown)
            {
                payment.Status = PaymentStatus.Failed;
                payment.FailureCode = callback.FailureCode;
            }

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return new CallbackOutcome(true, "RECEIVED_FAILED", "Đã ghi nhận giao dịch thất bại.", booking.BookingId);
        }

        if (payment.Status is PaymentStatus.Succeeded or PaymentStatus.RefundPending or PaymentStatus.Refunded)
        {
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return new CallbackOutcome(true, "ALREADY_SETTLED", "Giao dịch đã được ghi nhận.", booking.BookingId);
        }

        var alreadyAccepted = await db.Payments.AnyAsync(
            p => p.BookingId == booking.BookingId && p.IsAccepted && p.PaymentId != payment.PaymentId, ct);

        var seatsHeldByBooking = await db.ShowtimeSeats
            .Where(s => s.ShowtimeId == booking.ShowtimeId && s.BookingId == booking.BookingId
                        && s.State == ShowtimeSeatState.Held)
            .ToListAsync(ct);

        var canConfirm = booking.Status == BookingStatus.PendingPayment
                         && booking.ExpiresAtUtc > utcNow
                         && !alreadyAccepted
                         && seatsHeldByBooking.Count == booking.Tickets.Count
                         && seatsHeldByBooking.Count > 0;

        if (!canConfirm)
        {
            // Tiền về trễ hoặc đơn đã có lần thu khác: ghi nhận và chuyển hoàn toàn phần.
            payment.Status = PaymentStatus.RefundPending;
            payment.IsAccepted = false;
            payment.CapturedAtUtc = utcNow;
            payment.ProviderTransactionId ??= callback.ProviderTransactionId;
            payment.RefundRequestedAtUtc = utcNow;
            payment.FailureCode = alreadyAccepted ? "SECOND_CAPTURE" : "LATE_CAPTURE";

            eventRow.ResultCode = "REFUND_PENDING";
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            logger.LogWarning("Thanh toán {PaymentId} tới khi đơn {BookingId} không còn hợp lệ - chờ hoàn",
                payment.PaymentId, booking.BookingId);
            return new CallbackOutcome(true, "REFUND_PENDING", "Giao dịch cần hoàn tiền.", booking.BookingId);
        }

        payment.Status = PaymentStatus.Succeeded;
        payment.IsAccepted = true;
        payment.CapturedAtUtc = utcNow;
        payment.ProviderTransactionId ??= callback.ProviderTransactionId;

        foreach (var seat in seatsHeldByBooking)
        {
            seat.State = ShowtimeSeatState.Sold;
            seat.HeldUntilUtc = null;
        }

        foreach (var ticket in booking.Tickets)
        {
            if (ticket.Status != TicketStatus.Reserved)
            {
                continue;
            }

            var qr = qrTokens.Create();
            ticket.Status = TicketStatus.Issued;
            ticket.QrTokenHash = qr.Hash;
            ticket.QrTokenProtected = qr.Protected;
        }

        booking.Status = BookingStatus.Confirmed;
        booking.ConfirmedAtUtc = utcNow;

        await audit.LogAsync(null, "Payment.Succeeded", nameof(Payment), payment.PaymentId.ToString(),
            new { booking.BookingCode, payment.Amount }, ct);

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        logger.LogInformation("Đơn {BookingCode} đã xác nhận thanh toán", booking.BookingCode);
        return new CallbackOutcome(true, "CONFIRMED", "Đơn đã được xác nhận.", booking.BookingId);
    }

    public async Task<ServiceResult<PaymentStartDto>> CashPaymentAsync(
        long bookingId, int staffUserId, CashPaymentRequest request, CancellationToken ct = default)
    {
        var reference = await db.Bookings.AsNoTracking()
            .Where(b => b.BookingId == bookingId)
            .Select(b => new { b.ShowtimeId, b.Channel })
            .SingleOrDefaultAsync(ct);

        if (reference is null)
        {
            return ServiceResult<PaymentStartDto>.Fail(ErrorCodes.NotFound, "Không tìm thấy đơn.");
        }

        if (reference.Channel != BookingChannel.POS)
        {
            return ServiceResult<PaymentStartDto>.Fail(ErrorCodes.InvalidState,
                "Thu tiền mặt chỉ áp dụng cho đơn tại quầy.");
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        if (!await appLock.AcquireAsync(SqlAppLockService.ShowtimeResource(reference.ShowtimeId), ct))
        {
            return ServiceResult<PaymentStartDto>.Fail(ErrorCodes.ResourceBusy, "Suất đang bận. Vui lòng thử lại.");
        }

        var existing = await db.Payments.AsNoTracking()
            .SingleOrDefaultAsync(p => p.ClientRequestId == request.IdempotencyKey, ct);
        if (existing is not null)
        {
            return existing.BookingId == bookingId
                ? ServiceResult<PaymentStartDto>.Ok(await ToStartDtoAsync(existing, ct))
                : ServiceResult<PaymentStartDto>.Fail(ErrorCodes.IdempotencyConflict,
                    "Mã yêu cầu đã dùng cho giao dịch khác.");
        }

        var utcNow = await db.GetDatabaseUtcNowAsync(ct);
        var booking = await db.Bookings
            .Include(b => b.Tickets)
            .SingleAsync(b => b.BookingId == bookingId, ct);

        if (booking.Status != BookingStatus.PendingPayment)
        {
            return ServiceResult<PaymentStartDto>.Fail(ErrorCodes.InvalidState, "Đơn không chờ thanh toán.");
        }

        if (booking.ExpiresAtUtc <= utcNow)
        {
            return ServiceResult<PaymentStartDto>.Fail(ErrorCodes.BookingExpired, "Đơn đã hết hạn giữ ghế.");
        }

        var seats = await db.ShowtimeSeats
            .Where(s => s.ShowtimeId == booking.ShowtimeId && s.BookingId == booking.BookingId
                        && s.State == ShowtimeSeatState.Held)
            .ToListAsync(ct);

        if (seats.Count != booking.Tickets.Count || seats.Count == 0)
        {
            return ServiceResult<PaymentStartDto>.Fail(ErrorCodes.InvalidState,
                "Ghế của đơn không còn được giữ hợp lệ.");
        }

        var payment = new Payment
        {
            BookingId = bookingId,
            Provider = "Cash",
            MerchantReference = $"CASH-{Guid.NewGuid():N}".ToUpperInvariant()[..40],
            Amount = booking.FinalAmount,
            Currency = "VND",
            Status = PaymentStatus.Succeeded,
            IsAccepted = true,
            ClientRequestId = request.IdempotencyKey,
            RequestHash = RequestHash.Compute(bookingId, booking.FinalAmount, "CASH"),
            CreatedAtUtc = utcNow,
            CapturedAtUtc = utcNow,
            RecordedByUserId = staffUserId,
            ProviderTransactionId = $"POS-{booking.BookingCode}"
        };
        db.Payments.Add(payment);

        foreach (var seat in seats)
        {
            seat.State = ShowtimeSeatState.Sold;
            seat.HeldUntilUtc = null;
        }

        foreach (var ticket in booking.Tickets.Where(t => t.Status == TicketStatus.Reserved))
        {
            var qr = qrTokens.Create();
            ticket.Status = TicketStatus.Issued;
            ticket.QrTokenHash = qr.Hash;
            ticket.QrTokenProtected = qr.Protected;
        }

        booking.Status = BookingStatus.Confirmed;
        booking.ConfirmedAtUtc = utcNow;

        await audit.LogAsync(staffUserId, "Payment.CashConfirmed", nameof(Payment), payment.PaymentId.ToString(),
            new { booking.BookingCode, payment.Amount, booking.BookingId }, ct);

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return ServiceResult<PaymentStartDto>.Ok(await ToStartDtoAsync(payment, ct));
    }

    public async Task<ServiceResult> RefundAsync(long paymentId, int adminUserId, CancellationToken ct = default)
    {
        var payment = await db.Payments.Include(p => p.Booking)
            .SingleOrDefaultAsync(p => p.PaymentId == paymentId, ct);

        if (payment is null)
        {
            return ServiceResult.Fail(ErrorCodes.NotFound, "Không tìm thấy giao dịch.");
        }

        if (payment.Status != PaymentStatus.RefundPending)
        {
            return ServiceResult.Fail(ErrorCodes.InvalidState,
                "Chỉ xử lý hoàn cho giao dịch đang ở trạng thái RefundPending.");
        }

        var utcNow = DateTime.UtcNow;

        if (payment.Provider.Equals("Cash", StringComparison.OrdinalIgnoreCase))
        {
            // Tiền mặt: người có quyền xác nhận đã thực chi.
            payment.RefundAmount = payment.Amount;
            payment.RefundedAtUtc = utcNow;
            payment.ProviderRefundReference = $"CASH-REFUND-{Guid.NewGuid():N}"[..40].ToUpperInvariant();
            payment.Status = PaymentStatus.Refunded;
        }
        else
        {
            // Cổng mô phỏng: xác nhận hoàn ngay. Adapter thật phải xác minh với cổng trước khi ghi Refunded.
            payment.RefundAmount = payment.Amount;
            payment.RefundedAtUtc = utcNow;
            payment.ProviderRefundReference = $"REF-{Guid.NewGuid():N}"[..40].ToUpperInvariant();
            payment.Status = PaymentStatus.Refunded;
        }

        await audit.LogAsync(adminUserId, "Payment.Refunded", nameof(Payment), paymentId.ToString(),
            new { payment.Amount, payment.Provider }, ct);

        await db.SaveChangesAsync(ct);
        return ServiceResult.Ok();
    }

    public async Task<PaymentStatusDto?> GetStatusAsync(long bookingId, int userId, CancellationToken ct = default)
    {
        var row = await db.Bookings.AsNoTracking()
            .Where(b => b.BookingId == bookingId && b.UserId == userId)
            .Select(b => new
            {
                b.BookingId,
                b.BookingCode,
                b.Status,
                b.FinalAmount,
                Captured = b.Payments.Where(p => p.IsAccepted)
                    .Select(p => p.Amount).FirstOrDefault()
            })
            .SingleOrDefaultAsync(ct);

        if (row is null)
        {
            return null;
        }

        var latest = await db.Payments.AsNoTracking()
            .Where(p => p.BookingId == bookingId)
            .OrderByDescending(p => p.CreatedAtUtc)
            .Select(p => p.Status.ToString())
            .FirstOrDefaultAsync(ct) ?? "None";

        return new PaymentStatusDto(row.BookingId, row.BookingCode, row.Status.ToString(),
            row.FinalAmount, latest, row.Captured);
    }

    /// <summary>Tra trạng thái theo mã tham chiếu cổng; trả null nếu giao dịch không thuộc người gọi.</summary>
    public async Task<PaymentStatusDto?> GetStatusByReferenceAsync(
        string merchantReference, int userId, CancellationToken ct = default)
    {
        var bookingId = await db.Payments.AsNoTracking()
            .Where(p => p.MerchantReference == merchantReference)
            .Select(p => (long?)p.BookingId)
            .SingleOrDefaultAsync(ct);

        return bookingId is null ? null : await GetStatusAsync(bookingId.Value, userId, ct);
    }

    public async Task<FakeCheckoutDto?> GetSimulationAsync(
        string merchantReference, int userId, CancellationToken ct = default)
    {
        var payment = await db.Payments.AsNoTracking()
            .Where(p => p.MerchantReference == merchantReference)
            .Select(p => new
            {
                p.PaymentId,
                p.MerchantReference,
                p.Provider,
                p.ProviderTransactionId,
                p.Amount,
                p.Currency,
                p.BookingId,
                UserId = p.Booking!.UserId,
                BookingCode = p.Booking.BookingCode
            })
            .SingleOrDefaultAsync(ct);

        if (payment is null || payment.UserId != userId || payment.Provider != FakeGateway.ProviderName)
        {
            return null;
        }

        var status = await GetStatusAsync(payment.BookingId, userId, ct);
        if (status is null)
        {
            return null;
        }

        // Chỉ dựng payload mô phỏng trong Development (BuildSimulationPayload tự chặn môi trường khác).
        NormalizedCallback callback = new(
            payment.Provider,
            $"INIT-{payment.MerchantReference}",
            payment.MerchantReference,
            payment.ProviderTransactionId ?? $"FT-{payment.PaymentId}",
            payment.Amount,
            payment.Currency,
            true,
            null);

        if (gateway is not FakeGateway fake)
        {
            return null;
        }

        var payload = fake.BuildSimulationPayload(callback);
        return new FakeCheckoutDto(status, payment.MerchantReference, payload, fake.Sign(payload));
    }

    public async Task<CallbackOutcome?> SimulateCallbackAsync(
        string merchantReference, int userId, CancellationToken ct = default)
    {
        var simulation = await GetSimulationAsync(merchantReference, userId, ct);
        if (simulation is null)
        {
            return null;
        }

        var verified = gateway.VerifyCallback(simulation.PayloadBase64, simulation.Signature);
        var hash = RequestHash.Compute(simulation.PayloadBase64);
        return await HandleCallbackAsync(verified, hash, ct);
    }

    private async Task MarkOpenAttemptAsync(long paymentId, PaymentStatus status,
        string? providerTransactionId, string? failureCode, CancellationToken ct)
    {
        var payment = await db.Payments.SingleAsync(p => p.PaymentId == paymentId, ct);
        if (payment.Status is PaymentStatus.Succeeded or PaymentStatus.RefundPending or PaymentStatus.Refunded)
        {
            return; // Callback có thể đã tới trước response; không hạ trạng thái đã xác nhận.
        }

        payment.Status = status;
        payment.ProviderTransactionId ??= providerTransactionId;
        payment.FailureCode = failureCode;
        await db.SaveChangesAsync(ct);
    }

    private async Task<PaymentStartDto> ToStartDtoAsync(Payment payment, CancellationToken ct)
    {
        string? redirectUrl = null;
        if (payment.Status is PaymentStatus.Created or PaymentStatus.Pending && payment.Provider != "Cash")
        {
            redirectUrl = $"/payments/fake/checkout?reference={Uri.EscapeDataString(payment.MerchantReference)}";
        }

        await Task.CompletedTask;
        return new PaymentStartDto(payment.PaymentId, payment.MerchantReference, payment.Provider,
            payment.Amount, payment.Status.ToString(), redirectUrl, null, null);
    }
}
