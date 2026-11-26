using System.IO;
using System.Windows.Media.Imaging;
using QRCoder;

namespace EduFocus.WPF.Services;

/// <summary>
/// Renders the pairing payload to a frozen in-memory bitmap. The QR image is
/// never written to disk; the payload only exists in memory while pairing.
/// </summary>
public static class CloudQrCode
{
    public const int PixelsPerModule = 10;

    public static BitmapImage? TryRender(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
            return null;

        try
        {
            using var generator = new QRCodeGenerator();
            using var data = generator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.Q);
            using var qrCode = new PngByteQRCode(data);
            var png = qrCode.GetGraphic(PixelsPerModule);

            using var stream = new MemoryStream(png);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or NotSupportedException)
        {
            return null;
        }
    }
}
