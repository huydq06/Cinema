using QRCoder;

namespace WEB_BTL.Infrastructure.QrCode;

public interface IQrCodeGenerator
{
    /// <summary>Tạo QR code dạng PNG base64 data URI từ text.</summary>
    string GenerateDataUri(string text);
}

public class QrCodeGenerator : IQrCodeGenerator
{
    public string GenerateDataUri(string text)
    {
        using var qrGenerator = new QRCodeGenerator();
        using var qrCodeData = qrGenerator.CreateQrCode(text, QRCodeGenerator.ECCLevel.M);
        using var qrCode = new PngByteQRCode(qrCodeData);

        var pngBytes = qrCode.GetGraphic(pixelsPerModule: 10);
        var base64 = Convert.ToBase64String(pngBytes);

        return $"data:image/png;base64,{base64}";
    }
}
