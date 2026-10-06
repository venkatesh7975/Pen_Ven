using System.Net;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using ScreenInk.App.Services;
using ScreenInk.Media;
using ScreenInk.Native;
using Windows.ApplicationModel.DataTransfer;

var requests = new List<Uri>();
var handler = new FakeHttp(async request => {
    requests.Add(request.RequestUri!);
    await Task.Yield();
    var uri = request.RequestUri!.AbsoluteUri;
    if (uri.Contains("commons.wikimedia.org")) { return Json("""
    {"query":{"pages":[
      {"index":2,"title":"File:second.jpg","imageinfo":[{"url":"https://images.example/second.jpg","descriptionurl":"https://commons.wikimedia.org/wiki/File:second.jpg"}]},
      {"index":1,"title":"File:Earth.png","imageinfo":[{"url":"https://images.example/original.png","thumburl":"https://images.example/1280.png","descriptionurl":"https://commons.wikimedia.org/wiki/File:Earth.png","extmetadata":{"Artist":{"value":"<a href='https://example.com'>NASA &amp; team</a>"},"LicenseShortName":{"value":"Public domain"}}}]},
      {"index":3,"title":"File:unsafe.png","imageinfo":[{"url":"file:///local.png","descriptionurl":"https://example.com"}]}
    ]}}
    """); }
    if (uri.Contains("dictionaryapi.dev")) { return Json("""
    [{"word":"planet","license":{"name":"CC BY-SA 3.0"},"sourceUrls":["https://en.wiktionary.org/wiki/planet"],"meanings":[{"partOfSpeech":"noun","definitions":[{"definition":"A body orbiting a star."}]}]}]
    """); }
    if (uri.EndsWith("page")) { return new(HttpStatusCode.OK) { Content = new StringContent("<html/>", Encoding.UTF8, "text/html") }; }
    if (uri.EndsWith("unavailable")) { return new((HttpStatusCode)522); }
    if (uri.EndsWith("oversize")) { var content = new ByteArrayContent([1]); content.Headers.ContentLength = MediaContent.MaxImageBytes + 1; return new(HttpStatusCode.OK) { Content = content }; }
    return new(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3]) };
});
using var http = new HttpClient(handler);
using var catalog = new MediaCatalogClient(http);
var images = await catalog.SearchImagesAsync("Earth & moon");
Require(images.Count == 2 && images[0].Title == "Earth.png" && images[0].ImageUri.AbsoluteUri.EndsWith("1280.png"), "Commons results use search rank and thumbnail, discarding non-web sources.");
Require(images[0].Credit.Contains("NASA & team") && images[0].Credit.Contains("Public domain") && !images[0].Credit.Contains("<a"), "Artist/license attribution is decoded as plain text.");
Require(requests[0].Query.Contains("Earth%20%26%20moon"), "Search terms are URI encoded.");
var definitions = await catalog.DefineAsync("planet");
Require(definitions.Single().Meaning == "noun · A body orbiting a star." && definitions[0].Source?.Host == "en.wiktionary.org", "Definitions retain part of speech, license and source.");
await Fails(() => catalog.DownloadImageAsync(new("https://example.com/page")), "Webpages are not decoded as images.");
await Fails(() => catalog.DownloadImageAsync(new("https://example.com/oversize")), "Oversized image headers are rejected before reading.");
await Fails(() => catalog.DownloadImageAsync(new("https://example.com/unavailable")), "Provider outage is surfaced for paste/drop fallback.");
await Fails(() => MediaCatalogClient.ReadImageAsync(new MemoryStream()), "Empty images are rejected.");
await Fails(() => MediaCatalogClient.ReadImageAsync(new MemoryStream(new byte[MediaContent.MaxImageBytes + 1])), "Streaming images enforce the size limit even without a header.");
var cancel = new CancellationToken(true);
await Fails(() => catalog.SearchImagesAsync("Earth", cancel), "Cancelled searches stop before network access.");
Require(MediaContent.ImageFromHtml("<img alt='test' src=\"https://example.com/a.png?x=1&amp;y=2\">")?.Query == "?x=1&y=2", "Browser image HTML extracts and decodes source links.");
Require(MediaContent.ImageFromHtml("<img src='javascript:alert(1)'>") is null, "Active/non-web image URLs are rejected.");
Require(MediaContent.FileKind("clip.MP4") == MediaKind.Video && MediaContent.FileKind("picture.PNG") == MediaKind.Image && MediaContent.FileKind("program.exe") is null, "File imports accept supported media types only.");
var package = new DataPackage(); package.SetText("  A planet\r\norbits a star.  ");
var imported = (await MediaDropReader.ReadAsync(package.GetView())).Single();
Require(imported.Content.Text == "A planet\norbits a star.", "Selected text becomes a normalized card.");
var link = new DataPackage(); link.SetWebLink(new("https://example.com/clip.mp4"));
Require((await MediaDropReader.ReadAsync(link.GetView())).Single().Content.Kind == MediaKind.Video, "Direct video links are imported as playable media.");
var custom = new MediaDragContent(MediaKind.Definition, "planet", null, "A body orbiting a star.", "Source", "https://example.com");
var drag = new DataPackage(); drag.SetData(MediaDragContent.Format, custom.Serialize());
Require((await MediaDropReader.ReadAsync(drag.GetView())).Single().Content == custom, "Library drag data preserves media and attribution.");
await Fails(() => Task.FromResult(MediaDragContent.Parse("{\"Kind\":0,\"Title\":null,\"Credit\":null}")), "Malformed drag descriptions are rejected.");
CheckDesktopDrop(custom);
if (args.Contains("--fixtures")) {
    var folder = Path.GetFullPath(Path.Combine(".tools", "media-fixtures")); Directory.CreateDirectory(folder);
    var directory = await Windows.Storage.StorageFolder.GetFolderFromPathAsync(folder);
    var video = await directory.CreateFileAsync("color-test.mp4", Windows.Storage.CreationCollisionOption.ReplaceExisting);
    var composition = new Windows.Media.Editing.MediaComposition();
    composition.Clips.Add(Windows.Media.Editing.MediaClip.CreateFromColor(Windows.UI.Color.FromArgb(255, 119, 117, 214), TimeSpan.FromSeconds(4)));
    var result = await composition.RenderToFileAsync(video, Windows.Media.Editing.MediaTrimmingPreference.Precise,
        Windows.Media.MediaProperties.MediaEncodingProfile.CreateMp4(Windows.Media.MediaProperties.VideoEncodingQuality.Vga));
    Require(result == Windows.Media.Transcoding.TranscodeFailureReason.None, "Synthetic MP4 fixture renders using Windows Media Foundation.");
    Console.WriteLine("Video fixture: " + video.Path);
}
if (args.Contains("--live")) {
    using var live = new MediaCatalogClient();
    var results = await live.SearchImagesAsync("solar system");
    Require(results.Count > 0, "Live Commons image search returns results.");
    var bytes = await live.DownloadImageAsync(results[0].ImageUri);
    Require(bytes.Length > 100, "Live image is fetched successfully.");
    Console.WriteLine($"Live image search: {results.Count} results; image {bytes.Length:N0} bytes.");
}
Console.WriteLine("All media checks passed.");

static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
static void Require(bool result, string message) { if (!result) { throw new InvalidOperationException(message); } Console.WriteLine("PASS: " + message); }
static async Task Fails(Func<Task> action, string message) { try { await action(); } catch (Exception error) when (error is ArgumentException or HttpRequestException or OperationCanceledException) { Require(true, message); return; } throw new InvalidOperationException(message); }
static void CheckDesktopDrop(MediaDragContent content)
{
    Exception? failure = null;
    var thread = new Thread(() => {
        try {
            using var target = new DesktopDropTarget(MediaDragContent.Format);
            var data = new OleText(content.Serialize());
            DesktopMediaDrop? received = null; target.Dropped += drop => received = drop;
            uint effect = 3;
            target.DragEnter(data, 0, new() { X = 42, Y = 84 }, ref effect);
            Require(effect == 1, "Native drag target advertises copy without moving source content.");
            target.Drop(data, 0, new() { X = 42, Y = 84 }, ref effect);
            Require(received?.X == 42 && received.Y == 84 && MediaDragContent.Parse(received.Content) == content, "OLE Unicode drag payload and signed desktop coordinates survive the drop.");
            target.DragLeave(); target.Hide();
        } catch (Exception error) { failure = error; }
    });
    thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
    if (failure is not null) { throw new InvalidOperationException("Native drag check failed.", failure); }
}
sealed class FakeHttp(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) { token.ThrowIfCancellationRequested(); return respond(request); }
}
[ComVisible(true), ClassInterface(ClassInterfaceType.None)]
sealed class OleText(string text) : IDataObject
{
    public void GetData(ref FORMATETC format, out STGMEDIUM medium) {
        var bytes = Encoding.Unicode.GetBytes(text + "\0"); var memory = GlobalAlloc(0x42, (nuint)bytes.Length);
        var pointer = GlobalLock(memory); try { Marshal.Copy(bytes, 0, pointer, bytes.Length); } finally { GlobalUnlock(memory); }
        medium = new() { tymed = TYMED.TYMED_HGLOBAL, unionmember = memory, pUnkForRelease = null };
    }
    public int QueryGetData(ref FORMATETC format) => format.cfFormat == 13 ? 0 : unchecked((int)0x80040064);
    public void GetDataHere(ref FORMATETC format, ref STGMEDIUM medium) => throw new NotImplementedException();
    public int GetCanonicalFormatEtc(ref FORMATETC input, out FORMATETC output) { output = input; return 1; }
    public void SetData(ref FORMATETC format, ref STGMEDIUM medium, bool release) => throw new NotImplementedException();
    public IEnumFORMATETC EnumFormatEtc(DATADIR direction) => throw new NotImplementedException();
    public int DAdvise(ref FORMATETC format, ADVF flags, IAdviseSink sink, out int connection) { connection = 0; return unchecked((int)0x80040003); }
    public void DUnadvise(int connection) => throw new NotImplementedException();
    public int EnumDAdvise(out IEnumSTATDATA enumerator) { enumerator = null!; return unchecked((int)0x80040003); }
    [DllImport("kernel32.dll")] private static extern nint GlobalAlloc(uint flags, nuint bytes);
    [DllImport("kernel32.dll")] private static extern nint GlobalLock(nint memory);
    [DllImport("kernel32.dll")] private static extern bool GlobalUnlock(nint memory);
}
