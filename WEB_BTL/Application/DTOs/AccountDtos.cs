using System.ComponentModel.DataAnnotations;

namespace WEB_BTL.Application.DTOs;

public class RegisterRequest
{
    [Required(ErrorMessage = "Nhập họ tên.")]
    [StringLength(100)]
    public string FullName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Nhập email.")]
    [EmailAddress(ErrorMessage = "Email không hợp lệ.")]
    [StringLength(254)]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Nhập mật khẩu.")]
    [StringLength(128, MinimumLength = 10, ErrorMessage = "Mật khẩu tối thiểu 10 ký tự.")]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    [DataType(DataType.Password)]
    [Compare(nameof(Password), ErrorMessage = "Mật khẩu nhập lại không khớp.")]
    public string ConfirmPassword { get; set; } = string.Empty;

    [StringLength(20)]
    public string? Phone { get; set; }
}

public class LoginRequest
{
    [Required(ErrorMessage = "Nhập email.")]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Nhập mật khẩu.")]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    public bool RememberMe { get; set; }
}

public class ChangePasswordRequest
{
    [Required]
    [DataType(DataType.Password)]
    public string CurrentPassword { get; set; } = string.Empty;

    [Required]
    [StringLength(128, MinimumLength = 10)]
    [DataType(DataType.Password)]
    public string NewPassword { get; set; } = string.Empty;

    [DataType(DataType.Password)]
    [Compare(nameof(NewPassword), ErrorMessage = "Mật khẩu nhập lại không khớp.")]
    public string ConfirmPassword { get; set; } = string.Empty;
}

public record CurrentUserDto(int UserId, string FullName, string Email, string Role, int SecurityVersion);

public class UserListItemDto
{
    public int UserId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
