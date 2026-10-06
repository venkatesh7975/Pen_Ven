using ScreenInk.Native;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace ScreenInk.App.Services;

internal static class ScreenshotPng
{
    internal static async Task<byte[]> EncodeAsync(CapturedPixels capture)
    {
        using var stream = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore,
            (uint)capture.Width, (uint)capture.Height, 96, 96, capture.Bgra);
        await encoder.FlushAsync();
        stream.Seek(0);
        using var reader = new DataReader(stream.GetInputStreamAt(0));
        var bytes = new byte[checked((int)stream.Size)];
        await reader.LoadAsync((uint)bytes.Length);
        reader.ReadBytes(bytes);
        return bytes;
    }

    internal static async Task<InMemoryRandomAccessStream> StreamAsync(byte[] bytes)
    {
        var stream = new InMemoryRandomAccessStream();
        try {
            using (var writer = new DataWriter(stream.GetOutputStreamAt(0))) {
                writer.WriteBytes(bytes);
                await writer.StoreAsync();
                await writer.FlushAsync();
            }
            stream.Seek(0);
            return stream;
        } catch { stream.Dispose(); throw; }
    }
}
