using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WEB_BTL.Infrastructure.Data;

namespace WEB_BTL.Workers;

/// <summary>
/// Chạy migration và seed một lần khi khởi động ở môi trường phát triển/demo.
/// Không dùng cho môi trường thật: đặc tả yêu cầu deploy identity chạy migration trước khi nhận traffic.
/// </summary>
public class DatabaseStartupWorker(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    IWebHostEnvironment environment,
    ILogger<DatabaseStartupWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<CinemaDbContext>();
            var pending = (await db.Database.GetPendingMigrationsAsync(stoppingToken)).ToList();
            if (pending.Count > 0)
            {
                logger.LogInformation("Áp dụng {Count} migration: {Names}", pending.Count, string.Join(", ", pending));
                await db.Database.MigrateAsync(stoppingToken);
            }

            if (configuration.GetValue("Database:SeedOnStartup", environment.IsDevelopment()))
            {
                await CinemaDbSeeder.SeedAsync(scope.ServiceProvider, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Ứng dụng đang dừng.
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "Không chuẩn bị được cơ sở dữ liệu khi khởi động.");
        }
    }
}
