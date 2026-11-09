using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using WEB_BTL.Application.Options;
using WEB_BTL.Application.Services;

namespace WEB_BTL.Infrastructure.Storage;

/// <summary>
/// Lưu ảnh upload vào thư mục nội dung. Kiểm tra bằng "magic bytes" của file thay vì tin
/// vào Content-Type hay phần mở rộng do client gửi, nên file giả đuôi ảnh bị từ chối.
/// </summary>
public class LocalFileStorage(IOptions<UploadOptions> options, IWebHostEnvironment env, ILogger<LocalFileStorage> logger) : IFileStorage
{
    private readonly UploadOptions _options = options.Value;

    public async Task<UploadResult> SaveImageAsync(IFormFile file, string category, CancellationToken ct = default)
    {
        if (file.Length == 0)
        {
            return new UploadResult(false, null, "File rỗng.");
        }

        if (file.Length > _options.MaxBytes)
        {
            return new UploadResult(false, null, $"File vượt quá {_options.MaxBytes / (1024 * 1024)} MB.");
        }

        await using var buffer = new MemoryStream((int)Math.Min(file.Length, int.MaxValue));
        await file.CopyToAsync(buffer, ct);
        buffer.Position = 0;

        var format = DetectImageFormat(buffer);
        if (format is null)
        {
            return new UploadResult(false, null, "File không phải ảnh JPG, PNG hoặc WEBP hợp lệ.");
        }

        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var uniqueId = Guid.NewGuid().ToString("N")[..10];
        var fileName = $"{timestamp}_{uniqueId}{format.Value.Extension}";

        var subFolder = $"{SanitizeSegment(category)}/{DateTime.UtcNow:yyyy-MM}";
        var relativeDir = Path.Combine(_options.RootPath, subFolder.Replace('/', Path.DirectorySeparatorChar));
        var fullDir = Path.Combine(env.ContentRootPath, relativeDir);

        Directory.CreateDirectory(fullDir);

        var fullPath = Path.Combine(fullDir, fileName);
        buffer.Position = 0;
        await using (var output = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
        {
            await buffer.CopyToAsync(output, ct);
        }

        var publicUrl = $"{_options.PublicBasePath}/{subFolder}/{fileName}";
        logger.LogInformation("Đã lưu ảnh {Url} ({Bytes} byte)", publicUrl, file.Length);

        return new UploadResult(true, publicUrl, null);
    }

    public bool DeleteFile(string relativeUrl)
    {
        if (string.IsNullOrWhiteSpace(relativeUrl)
            || !relativeUrl.StartsWith(_options.PublicBasePath, StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            var relativePath = relativeUrl[_options.PublicBasePath.Length..]
                .TrimStart('/')
                .Replace('/', Path.DirectorySeparatorChar);

            var root = Path.GetFullPath(Path.Combine(env.ContentRootPath, _options.RootPath));
            var fullPath = Path.GetFullPath(Path.Combine(root, relativePath));

            // Chặn path traversal: file phải nằm trong thư mục upload.
            if (!fullPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
                logger.LogInformation("Đã xóa ảnh {Url}", relativeUrl);
            }

            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Không xóa được ảnh {Url}", relativeUrl);
            return false;
        }
    }

    /// <summary>Nhận dạng theo header file; null nghĩa là không phải ảnh được phép.</summary>
    private static (string Extension, string Mime)? DetectImageFormat(Stream stream)
    {
        Span<byte> header = stackalloc byte[12];
        var read = stream.Read(header);
        if (read < 12)
        {
            return null;
        }

        // JPEG: FF D8 FF
        if (header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
        {
            return (".jpg", "image/jpeg");
        }

        // PNG: 89 50 4E 47 0D 0A 1A 0A
        if (header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47
            && header[4] == 0x0D && header[5] == 0x0A && header[6] == 0x1A && header[7] == 0x0A)
        {
            return (".png", "image/png");
        }

        // WEBP: "RIFF" .... "WEBP"
        if (header[0] == 'R' && header[1] == 'I' && header[2] == 'F' && header[3] == 'F'
            && header[8] == 'W' && header[9] == 'E' && header[10] == 'B' && header[11] == 'P')
        {
            return (".webp", "image/webp");
        }

        return null;
    }

    private static string SanitizeSegment(string value)
    {
        var cleaned = new string(value.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_').ToArray());
        return cleaned.Length == 0 ? "misc" : cleaned.ToLowerInvariant();
    }
}
