using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WEB_BTL.Infrastructure.Data;

namespace WEB_BTL.Controllers;

[ApiController]
[Route("health")]
[AllowAnonymous]
public class HealthController(CinemaDbContext db, ILogger<HealthController> logger) : ControllerBase
{
    /// <summary>
    /// Liveness probe: process đang chạy, nhận request được.
    /// </summary>
    [HttpGet("live")]
    public IActionResult Live()
    {
        return Ok(new { status = "healthy", timestamp = DateTime.UtcNow });
    }

    /// <summary>
    /// Readiness probe: DB kết nối được, sẵn sàng phục vụ traffic.
    /// </summary>
    [HttpGet("ready")]
    public async Task<IActionResult> Ready(CancellationToken ct)
    {
        try
        {
            // Kiểm tra DB connection bằng query đơn giản
            await db.Database.ExecuteSqlRawAsync("SELECT 1", ct);

            return Ok(new
            {
                status = "ready",
                timestamp = DateTime.UtcNow,
                database = "connected"
            });
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Health check ready failed");
            return StatusCode(503, new
            {
                status = "not_ready",
                timestamp = DateTime.UtcNow,
                database = "disconnected",
                error = ex.Message
            });
        }
    }
}
