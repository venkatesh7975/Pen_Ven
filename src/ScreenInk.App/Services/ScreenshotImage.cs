using ScreenInk.Native;
using Windows.Storage.Streams;
using Microsoft.UI.Xaml.Media.Imaging;

namespace ScreenInk.App.Services;

internal static class ScreenshotImage
{
    internal static Task<byte[]> EncodeAsync(CapturedPixels capture) => ScreenshotPng.EncodeAsync(capture);

    internal static async Task<BitmapImage> PreviewAsync(byte[] png)
    {
        using var stream = await StreamAsync(png);
        var image = new BitmapImage();
        await image.SetSourceAsync(stream);
        return image;
    }

    internal static Task<InMemoryRandomAccessStream> StreamAsync(byte[] bytes) => ScreenshotPng.StreamAsync(bytes);
}
