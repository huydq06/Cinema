using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using WEB_BTL.Domain.Entities;

namespace WEB_BTL.Infrastructure.Security;

/// <summary>Băm và kiểm tra mật khẩu bằng <see cref="PasswordHasher{TUser}"/> của ASP.NET Core.</summary>
public interface IPasswordService
{
    string Hash(User user, string password);
    bool Verify(User user, string password, out bool needsRehash);
}

public class PasswordService(IPasswordHasher<User> hasher) : IPasswordService
{
    public string Hash(User user, string password) => hasher.HashPassword(user, password);

    public bool Verify(User user, string password, out bool needsRehash)
    {
        var result = hasher.VerifyHashedPassword(user, user.PasswordHash, password);
        needsRehash = result == PasswordVerificationResult.SuccessRehashNeeded;
        return result != PasswordVerificationResult.Failed;
    }
}

/// <summary>
/// Sinh token QR 256 bit: DB lưu SHA-256 để tra cứu, token gốc được Data Protection bảo vệ
/// để hiển thị lại cho đúng chủ vé. QR không chứa thông tin cá nhân.
/// </summary>
public interface IQrTokenService
{
    (string Token, string Hash, string Protected) Create();
    string HashOf(string token);
    string? Unprotect(string protectedToken);
}

public class QrTokenService(IDataProtectionProvider protectionProvider) : IQrTokenService
{
    private readonly IDataProtector _protector = protectionProvider.CreateProtector("CinemaTicket.QrToken.v1");

    public (string Token, string Hash, string Protected) Create()
    {
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        return (token, HashOf(token), _protector.Protect(token));
    }

    public string HashOf(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    public string? Unprotect(string protectedToken)
    {
        try
        {
            return _protector.Unprotect(protectedToken);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }
}

/// <summary>Hash nội dung payload chuẩn hóa để hỗ trợ idempotency và phát hiện replay khác nội dung.</summary>
public static class RequestHash
{
    public static string Compute(params object?[] parts)
    {
        var normalized = string.Join('|', parts.Select(p => p?.ToString() ?? string.Empty));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
    }
}
