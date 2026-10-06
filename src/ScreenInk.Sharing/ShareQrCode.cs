using QRCoder;

namespace ScreenInk.Sharing;

public static class ShareQrCode
{
    public static byte[] Create(Uri address)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (!address.IsAbsoluteUri || address.Scheme != Uri.UriSchemeHttp) { throw new ArgumentException("A screenshot sharing URL is required.", nameof(address)); }
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(address.AbsoluteUri, QRCodeGenerator.ECCLevel.Q);
        using var png = new PngByteQRCode(data);
        return png.GetGraphic(6, drawQuietZones: true);
    }
}
