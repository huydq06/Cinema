using Microsoft.AspNetCore.Http;

namespace WEB_BTL.Application.Services;

public record UploadResult(bool Success, string? Url, string? Error);

public interface IFileStorage
{
    /// <summary>Lưu file ảnh với validation. Trả về URL tương đối hoặc lỗi.</summary>
    Task<UploadResult> SaveImageAsync(IFormFile file, string category, CancellationToken ct = default);

    /// <summary>Xóa file nếu tồn tại. Trả về true nếu xóa thành công hoặc file không tồn tại.</summary>
    bool DeleteFile(string relativeUrl);
}
