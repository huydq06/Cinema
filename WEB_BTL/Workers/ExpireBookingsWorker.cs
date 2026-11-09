using Microsoft.Extensions.Options;
using WEB_BTL.Application.Options;
using WEB_BTL.Application.Services;

namespace WEB_BTL.Workers;

/// <summary>
/// Giải phóng ghế của đơn PendingPayment quá hạn. Worker chỉ gọi service nghiệp vụ để
/// mọi thay đổi đi qua cùng app lock và transaction với luồng đặt vé/thanh toán.
/// </summary>
public class ExpireBookingsWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<WorkerOptions> options,
    ILogger<ExpireBookingsWorker> logger) : BackgroundService
{
    private readonly int _intervalSeconds = Math.Max(5, options.Value.ExpireIntervalSeconds);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_intervalSeconds));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var bookings = scope.ServiceProvider.GetRequiredService<IBookingService>();
                var expired = await bookings.ExpireAllDueBookingsAsync(stoppingToken);
                if (expired > 0)
                {
                    logger.LogInformation("Đã xử lý {Count} đơn hết hạn", expired);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Lỗi một vòng không được làm chết worker.
                logger.LogError(ex, "Lỗi khi giải phóng đơn hết hạn");
            }

            if (!await timer.WaitForNextTickAsync(stoppingToken))
            {
                break;
            }
        }
    }
}
