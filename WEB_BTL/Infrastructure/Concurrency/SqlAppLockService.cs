using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using WEB_BTL.Infrastructure.Data;

namespace WEB_BTL.Infrastructure.Concurrency;

/// <summary>
/// Khóa nghiệp vụ theo transaction của SQL Server. Toàn bộ luồng sửa đơn/ghế/vé/payment của cùng
/// suất phải lấy cùng khóa này trên cùng connection và transaction trước khi đọc dữ liệu để quyết định.
/// Khóa không thay thế unique/FK/CHECK: các ràng buộc đó là lớp bảo vệ cuối.
/// </summary>
public interface IAppLockService
{
    /// <summary>Lấy khóa Exclusive; trả về false khi hết thời gian chờ (không được tiếp tục như đã có khóa).</summary>
    Task<bool> AcquireAsync(string resource, CancellationToken ct = default);

    /// <summary>Lấy nhiều khóa theo đúng thứ tự đã cho (room theo ID tăng dần, sau đó showtime).</summary>
    Task<bool> AcquireManyAsync(IEnumerable<string> resources, CancellationToken ct = default);
}

public class SqlAppLockService(CinemaDbContext db, ILogger<SqlAppLockService> logger) : IAppLockService
{
    private const int LockTimeoutMilliseconds = 2_000;

    public static string ShowtimeResource(int showtimeId) => $"showtime:{showtimeId}";

    public static string RoomResource(int roomId) => $"room:{roomId}";

    public async Task<bool> AcquireAsync(string resource, CancellationToken ct = default) =>
        await AcquireManyAsync([resource], ct);

    public async Task<bool> AcquireManyAsync(IEnumerable<string> resources, CancellationToken ct = default)
    {
        var connection = (SqlConnection)db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            throw new InvalidOperationException("App lock phải được lấy bên trong một transaction đang mở.");
        }

        foreach (var resource in resources)
        {
            var parameters = new[]
            {
                new SqlParameter("@Resource", resource),
                new SqlParameter("@LockMode", "Exclusive"),
                new SqlParameter("@LockOwner", "Transaction"),
                new SqlParameter("@LockTimeout", LockTimeoutMilliseconds)
            };

            await using var command = connection.CreateCommand();
            command.Transaction = (SqlTransaction?)db.Database.CurrentTransaction?.GetDbTransaction();
            command.CommandText =
                "DECLARE @Result int; " +
                "EXEC @Result = sys.sp_getapplock @Resource = @Resource, @LockMode = @LockMode, " +
                "@LockOwner = @LockOwner, @LockTimeout = @LockTimeout; " +
                "SELECT @Result;";
            command.Parameters.AddRange(parameters);

            var result = Convert.ToInt32(await command.ExecuteScalarAsync(ct));
            if (result < 0)
            {
                // Timeout (-1), deadlock (-2), lỗi tham số (-3), hoặc lỗi khác (-999).
                logger.LogWarning("Không lấy được app lock {Resource}; mã trả về {Code}", resource, result);
                return false;
            }
        }

        return true;
    }
}
