using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WEB_BTL.Application.Options;
using WEB_BTL.Domain.Entities;
using WEB_BTL.Domain.Enums;
using WEB_BTL.Infrastructure.Data;

namespace WEB_BTL.Workers;

/// <summary>
/// Đối soát định kỳ các giao dịch treo. FakeGateway không có API truy vấn trạng thái,
/// nên worker chỉ đánh dấu để vận hành xử lý thay vì tự suy đoán kết quả:
/// - Payment <c>Unknown</c> quá hạn: ghi PaymentEvent ReviewRequired và log cảnh báo.
/// - Payment <c>RefundPending</c> tồn đọng: log cảnh báo để Admin hoàn tiền tay.
/// Không có nhánh nào tự phát hành vé — vé chỉ được cấp qua callback đã xác minh hoặc thu tiền mặt.
/// </summary>
public class PaymentReconciliationWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<WorkerOptions> options,
    ILogger<PaymentReconciliationWorker> logger) : BackgroundService
{
    private readonly int _intervalSeconds = Math.Max(30, options.Value.ReconcileIntervalSeconds);
    private static readonly TimeSpan StuckAfter = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_intervalSeconds));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ReconcileAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Lỗi khi đối soát giao dịch");
            }

            if (!await timer.WaitForNextTickAsync(stoppingToken))
            {
                break;
            }
        }
    }

    private async Task ReconcileAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CinemaDbContext>();

        var utcNow = await db.GetDatabaseUtcNowAsync(ct);
        var cutoff = utcNow - StuckAfter;

        var unknown = await db.Payments
            .Where(p => p.Status == PaymentStatus.Unknown && p.CreatedAtUtc < cutoff)
            .Where(p => !db.PaymentEvents.Any(e => e.PaymentId == p.PaymentId
                                                   && e.ResultCode == "RECONCILIATION_PENDING"))
            .Take(50)
            .ToListAsync(ct);

        foreach (var payment in unknown)
        {
            db.PaymentEvents.Add(new PaymentEvent
            {
                PaymentId = payment.PaymentId,
                Provider = payment.Provider,
                EventKey = $"RECON-{payment.PaymentId}",
                PayloadHash = payment.RequestHash,
                ReceivedAtUtc = utcNow,
                ProcessingStatus = PaymentEventStatus.ReviewRequired,
                ResultCode = "RECONCILIATION_PENDING"
            });

            logger.LogWarning(
                "Giao dịch {Reference} ở trạng thái Unknown quá {Minutes} phút - cần đối soát tay",
                payment.MerchantReference, StuckAfter.TotalMinutes);
        }

        if (unknown.Count > 0)
        {
            await db.SaveChangesAsync(ct);
        }

        var stuckRefunds = await db.Payments
            .CountAsync(p => p.Status == PaymentStatus.RefundPending
                             && p.RefundRequestedAtUtc != null
                             && p.RefundRequestedAtUtc < cutoff, ct);

        if (stuckRefunds > 0)
        {
            logger.LogWarning("Có {Count} khoản hoàn tiền chờ xử lý quá 5 phút", stuckRefunds);
        }
    }
}
