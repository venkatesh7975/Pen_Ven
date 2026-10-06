using System.Net;
using System.Net.Sockets;
using ScreenInk.Native;
using ScreenInk.Sharing;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using ZXing;

static void Require(bool condition, string message) { if (!condition) { throw new InvalidOperationException(message); } }
static async Task<(int Width, int Height, byte[] Pixels)> DecodeAsync(byte[] png)
{
    using var stream = new InMemoryRandomAccessStream();
    using (var writer = new DataWriter(stream.GetOutputStreamAt(0))) { writer.WriteBytes(png); await writer.StoreAsync(); await writer.FlushAsync(); }
    stream.Seek(0);
    var decoder = await BitmapDecoder.CreateAsync(stream);
    var data = await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore,
        new BitmapTransform(), ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage);
    return ((int)decoder.PixelWidth, (int)decoder.PixelHeight, data.DetachPixelData());
}

var sample = new CapturedPixels(2, 2, new byte[] {10,20,30,255, 40,50,60,255, 70,80,90,255, 100,110,120,255});
var png = await ScreenInk.App.Services.ScreenshotPng.EncodeAsync(sample);
var decoded = await DecodeAsync(png);
Require(decoded.Width == 2 && decoded.Height == 2 && decoded.Pixels.SequenceEqual(sample.Bgra), "Saved PNG preserves BGRA colors and row order.");

using var client = new HttpClient(new HttpClientHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(5) };
using var share = new ScreenshotShareServer(IPAddress.Loopback, png);
var original = (byte[])png.Clone(); png[20] ^= 0xff;
using var page = await client.GetAsync(share.Address);
Require(page.IsSuccessStatusCode && (await page.Content.ReadAsStringAsync()).Contains("Save image"), "QR URL serves a mobile screenshot page.");
Require(page.Headers.CacheControl?.NoStore == true && page.Headers.Contains("Content-Security-Policy"), "Sharing responses prevent caching and external page resources.");
var imageAddress = new Uri(share.Address.AbsoluteUri + "/screenshot.png");
Require((await client.GetByteArrayAsync(imageAddress)).SequenceEqual(original), "Share owns one immutable snapshot, separate from caller mutation.");
using var missing = await client.GetAsync(new Uri(share.Address.GetLeftPart(UriPartial.Authority) + "/"));
Require(missing.StatusCode == HttpStatusCode.NotFound, "Knowing the server address without its random token does not reveal the screenshot.");
using var wrong = await client.GetAsync(new Uri(share.Address.AbsoluteUri + "/other-file"));
Require(wrong.StatusCode == HttpStatusCode.NotFound, "Server has no arbitrary file routes.");
using var post = await client.PostAsync(share.Address, new StringContent("ignored"));
Require(post.StatusCode == HttpStatusCode.MethodNotAllowed, "Screenshot sharing rejects write methods.");
using var headRequest = new HttpRequestMessage(HttpMethod.Head, imageAddress);
using var head = await client.SendAsync(headRequest);
Require(head.IsSuccessStatusCode && head.Content.Headers.ContentLength == original.Length && (await head.Content.ReadAsByteArrayAsync()).Length == 0,
    "HEAD describes the PNG without returning its bytes.");

var qr = await DecodeAsync(ShareQrCode.Create(share.Address));
var reader = new BarcodeReaderGeneric { Options = new ZXing.Common.DecodingOptions { PossibleFormats = new[] { BarcodeFormat.QR_CODE } } };
var result = reader.Decode(qr.Pixels, qr.Width, qr.Height, RGBLuminanceSource.BitmapFormat.BGRA32);
Require(result?.Text == share.Address.AbsoluteUri, "Independent QR decoder recovers the exact working screenshot URL.");

using var replacement = new ScreenshotShareServer(IPAddress.Loopback, original);
Require(replacement.Address.AbsolutePath != share.Address.AbsolutePath, "New screenshot share gets a fresh random token.");
share.Dispose(); await share.Completion.WaitAsync(TimeSpan.FromSeconds(2));
Require(!share.IsRunning, "Stopping sharing closes the listener and active requests.");
try { await client.GetAsync(imageAddress); throw new InvalidOperationException("Stopped share still responded."); }
catch (HttpRequestException) { }

using var expires = new ScreenshotShareServer(IPAddress.Loopback, original, TimeSpan.FromMilliseconds(120));
using var slow = new TcpClient();
await slow.ConnectAsync(IPAddress.Loopback, expires.Address.Port);
await slow.GetStream().WriteAsync("GET /"u8.ToArray());
await expires.Completion.WaitAsync(TimeSpan.FromSeconds(2));
Require(!expires.IsRunning, "Expiration also cancels clients holding partial HTTP requests.");
Require(ShareNetwork.IsPrivate(IPAddress.Parse("192.168.1.2")) && !ShareNetwork.IsPrivate(IPAddress.Parse("8.8.8.8")), "QR sharing selects private LAN addresses.");
Console.WriteLine("PASS: Screenshot PNG round trip, independently decoded QR URL, HTTP page/image, token isolation, snapshot ownership, stop and expiration.");
