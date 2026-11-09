namespace WEB_BTL.Domain.Entities;

public class Role
{
    public int RoleId { get; set; }
    public string RoleName { get; set; } = string.Empty;
    public string? Description { get; set; }

    public ICollection<User> Users { get; set; } = [];
}

public class User
{
    public int UserId { get; set; }
    public int RoleId { get; set; }
    public Role? Role { get; set; }

    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string NormalizedEmail { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? AvatarUrl { get; set; }

    public bool IsActive { get; set; } = true;
    public int SecurityVersion { get; set; } = 1;
    public int FailedLoginCount { get; set; }
    public DateTime? LockoutEndUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];

    public ICollection<Booking> Bookings { get; set; } = [];

    public bool IsLockedOut(DateTime utcNow) => LockoutEndUtc is { } end && end > utcNow;
}
