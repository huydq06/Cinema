using System.Diagnostics;
using System.Text.Json;
using WEB_BTL.Domain.Entities;
using WEB_BTL.Infrastructure.Data;

namespace WEB_BTL.Application.Services;

public interface IAuditService
{
    Task LogAsync(int? actorUserId, string action, string entityType, string entityId,
        object? summary = null, CancellationToken ct = default);
}

public class AuditService(CinemaDbContext db) : IAuditService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task LogAsync(int? actorUserId, string action, string entityType, string entityId,
        object? summary = null, CancellationToken ct = default)
    {
        db.AuditLogs.Add(new AuditLog
        {
            ActorUserId = actorUserId,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            OccurredAtUtc = DateTime.UtcNow,
            TraceId = Activity.Current?.TraceId.ToString() ?? "n/a",
            SummaryJson = summary is null ? null : JsonSerializer.Serialize(summary, JsonOptions)
        });

        await Task.CompletedTask;
    }
}
