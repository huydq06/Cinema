using Microsoft.EntityFrameworkCore;
using WEB_BTL.Application;
using WEB_BTL.Application.DTOs;
using WEB_BTL.Domain.Constants;
using WEB_BTL.Domain.Entities;
using WEB_BTL.Infrastructure.Data;
using WEB_BTL.Infrastructure.Security;

namespace WEB_BTL.Application.Services;

public interface IAccountService
{
    Task<ServiceResult<int>> RegisterAsync(RegisterRequest request, CancellationToken ct = default);
    Task<ServiceResult<CurrentUserDto>> ValidateCredentialsAsync(string email, string password, CancellationToken ct = default);
    Task<ServiceResult> ChangePasswordAsync(int userId, ChangePasswordRequest request, CancellationToken ct = default);
    Task<CurrentUserDto?> GetCurrentAsync(int userId, CancellationToken ct = default);
    Task<bool> IsStillValidAsync(int userId, string role, int securityVersion, CancellationToken ct = default);
    Task<ServiceResult> SetActiveAsync(int userId, bool isActive, int actorUserId, CancellationToken ct = default);
    Task<ServiceResult> SetRoleAsync(int userId, string roleName, int actorUserId, CancellationToken ct = default);
    Task<IReadOnlyList<UserListItemDto>> ListAsync(CancellationToken ct = default);
    Task<IReadOnlyList<string>> ListRoleNamesAsync(CancellationToken ct = default);
}

public class AccountService(
    CinemaDbContext db,
    IPasswordService passwordService,
    ILogger<AccountService> logger) : IAccountService
{
    private const int MaxFailedLogins = 5;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    public static string NormalizeEmail(string email) => email.Trim().ToUpperInvariant();

    public async Task<ServiceResult<int>> RegisterAsync(RegisterRequest request, CancellationToken ct = default)
    {
        var normalized = NormalizeEmail(request.Email);
        if (await db.Users.AnyAsync(u => u.NormalizedEmail == normalized, ct))
        {
            return ServiceResult<int>.Fail(ErrorCodes.ValidationFailed,
                "Email này đã được đăng ký. Vui lòng đăng nhập hoặc dùng email khác.");
        }

        var customerRoleId = await db.Roles
            .Where(r => r.RoleName == Roles.Customer)
            .Select(r => r.RoleId)
            .SingleAsync(ct);

        var user = new User
        {
            RoleId = customerRoleId, // Luôn gán Customer ở server, bỏ qua role từ request.
            FullName = request.FullName.Trim(),
            Email = request.Email.Trim(),
            NormalizedEmail = normalized,
            Phone = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim(),
            IsActive = true,
            SecurityVersion = 1,
            CreatedAtUtc = DateTime.UtcNow
        };
        user.PasswordHash = passwordService.Hash(user, request.Password);

        db.Users.Add(user);
        await db.SaveChangesAsync(ct);
        return ServiceResult<int>.Ok(user.UserId);
    }

    public async Task<ServiceResult<CurrentUserDto>> ValidateCredentialsAsync(
        string email, string password, CancellationToken ct = default)
    {
        var normalized = NormalizeEmail(email);
        var user = await db.Users.Include(u => u.Role)
            .SingleOrDefaultAsync(u => u.NormalizedEmail == normalized, ct);

        if (user is null)
        {
            // Không tiết lộ email có tồn tại hay không.
            return ServiceResult<CurrentUserDto>.Fail(ErrorCodes.Unauthenticated,
                "Email hoặc mật khẩu không đúng.");
        }

        var utcNow = DateTime.UtcNow;
        if (user.IsLockedOut(utcNow))
        {
            return ServiceResult<CurrentUserDto>.Fail(ErrorCodes.Unauthenticated,
                "Tài khoản tạm khóa do đăng nhập sai nhiều lần. Vui lòng thử lại sau 15 phút.");
        }

        if (!passwordService.Verify(user, password, out var needsRehash) || !user.IsActive)
        {
            user.FailedLoginCount++;
            if (user.FailedLoginCount >= MaxFailedLogins)
            {
                user.LockoutEndUtc = utcNow.Add(LockoutDuration);
                user.FailedLoginCount = 0;
            }
            await db.SaveChangesAsync(ct);

            return ServiceResult<CurrentUserDto>.Fail(ErrorCodes.Unauthenticated,
                user.IsActive
                    ? "Email hoặc mật khẩu không đúng."
                    : "Tài khoản đã bị khóa. Vui lòng liên hệ quản trị viên.");
        }

        user.FailedLoginCount = 0;
        user.LockoutEndUtc = null;
        if (needsRehash)
        {
            user.PasswordHash = passwordService.Hash(user, password);
        }
        await db.SaveChangesAsync(ct);

        return ServiceResult<CurrentUserDto>.Ok(
            new CurrentUserDto(user.UserId, user.FullName, user.Email,
                user.Role?.RoleName ?? Roles.Customer, user.SecurityVersion));
    }

    public async Task<ServiceResult> ChangePasswordAsync(
        int userId, ChangePasswordRequest request, CancellationToken ct = default)
    {
        var user = await db.Users.SingleOrDefaultAsync(u => u.UserId == userId, ct);
        if (user is null)
        {
            return ServiceResult.Fail(ErrorCodes.NotFound, "Không tìm thấy tài khoản.");
        }

        if (!passwordService.Verify(user, request.CurrentPassword, out _))
        {
            return ServiceResult.Fail(ErrorCodes.ValidationFailed, "Mật khẩu hiện tại không đúng.");
        }

        user.PasswordHash = passwordService.Hash(user, request.NewPassword);
        user.SecurityVersion++; // Thu hồi mọi cookie cũ.
        user.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Người dùng {UserId} đổi mật khẩu", userId);
        return ServiceResult.Ok();
    }

    public async Task<CurrentUserDto?> GetCurrentAsync(int userId, CancellationToken ct = default)
    {
        return await db.Users.AsNoTracking()
            .Where(u => u.UserId == userId && u.IsActive)
            .Select(u => new CurrentUserDto(
                u.UserId, u.FullName, u.Email,
                u.Role!.RoleName, u.SecurityVersion))
            .SingleOrDefaultAsync(ct);
    }

    public async Task<bool> IsStillValidAsync(
        int userId, string role, int securityVersion, CancellationToken ct = default)
    {
        return await db.Users.AsNoTracking().AnyAsync(
            u => u.UserId == userId && u.IsActive
                 && u.SecurityVersion == securityVersion
                 && u.Role!.RoleName == role, ct);
    }

    public async Task<ServiceResult> SetActiveAsync(
        int userId, bool isActive, int actorUserId, CancellationToken ct = default)
    {
        var user = await db.Users.Include(u => u.Role).SingleOrDefaultAsync(u => u.UserId == userId, ct);
        if (user is null)
        {
            return ServiceResult.Fail(ErrorCodes.NotFound, "Không tìm thấy tài khoản.");
        }

        if (!isActive && user.Role?.RoleName == Roles.Admin)
        {
            var activeAdmins = await db.Users.CountAsync(
                u => u.IsActive && u.Role!.RoleName == Roles.Admin, ct);
            if (activeAdmins <= 1)
            {
                return ServiceResult.Fail(ErrorCodes.InvalidState,
                    "Không thể khóa quản trị viên đang hoạt động cuối cùng.");
            }
        }

        user.IsActive = isActive;
        user.SecurityVersion++;
        user.UpdatedAtUtc = DateTime.UtcNow;

        db.AuditLogs.Add(new AuditLog
        {
            ActorUserId = actorUserId,
            Action = isActive ? "User.Activated" : "User.Deactivated",
            EntityType = nameof(User),
            EntityId = userId.ToString(),
            OccurredAtUtc = DateTime.UtcNow,
            TraceId = System.Diagnostics.Activity.Current?.Id ?? "n/a"
        });

        await db.SaveChangesAsync(ct);
        return ServiceResult.Ok();
    }

    public async Task<ServiceResult> SetRoleAsync(
        int userId, string roleName, int actorUserId, CancellationToken ct = default)
    {
        var role = await db.Roles.SingleOrDefaultAsync(r => r.RoleName == roleName, ct);
        if (role is null)
        {
            return ServiceResult.Fail(ErrorCodes.ValidationFailed, "Vai trò không hợp lệ.");
        }

        var user = await db.Users.Include(u => u.Role).SingleOrDefaultAsync(u => u.UserId == userId, ct);
        if (user is null)
        {
            return ServiceResult.Fail(ErrorCodes.NotFound, "Không tìm thấy tài khoản.");
        }

        if (user.Role?.RoleName == Roles.Admin && roleName != Roles.Admin && user.IsActive)
        {
            var activeAdmins = await db.Users.CountAsync(
                u => u.IsActive && u.Role!.RoleName == Roles.Admin, ct);
            if (activeAdmins <= 1)
            {
                return ServiceResult.Fail(ErrorCodes.InvalidState,
                    "Không thể hạ quyền quản trị viên đang hoạt động cuối cùng.");
            }
        }

        user.RoleId = role.RoleId;
        user.SecurityVersion++;
        user.UpdatedAtUtc = DateTime.UtcNow;

        db.AuditLogs.Add(new AuditLog
        {
            ActorUserId = actorUserId,
            Action = "User.RoleChanged",
            EntityType = nameof(User),
            EntityId = userId.ToString(),
            OccurredAtUtc = DateTime.UtcNow,
            TraceId = System.Diagnostics.Activity.Current?.Id ?? "n/a",
            SummaryJson = $$"""{"role":"{{roleName}}"}"""
        });

        await db.SaveChangesAsync(ct);
        return ServiceResult.Ok();
    }

    public async Task<IReadOnlyList<UserListItemDto>> ListAsync(CancellationToken ct = default)
    {
        return await db.Users.AsNoTracking()
            .OrderBy(u => u.UserId)
            .Select(u => new UserListItemDto
            {
                UserId = u.UserId,
                FullName = u.FullName,
                Email = u.Email,
                Role = u.Role!.RoleName,
                IsActive = u.IsActive,
                CreatedAtUtc = u.CreatedAtUtc
            })
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<string>> ListRoleNamesAsync(CancellationToken ct = default) =>
        await db.Roles.AsNoTracking().OrderBy(r => r.RoleName).Select(r => r.RoleName).ToListAsync(ct);
}
