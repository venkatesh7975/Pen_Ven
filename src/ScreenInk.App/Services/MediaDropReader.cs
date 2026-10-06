using ScreenInk.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

namespace ScreenInk.App.Services;

internal sealed record MediaImport(MediaDragContent Content, string? Path = null, byte[]? Image = null);
internal static class MediaDropReader
{
    internal static bool Supports(DataPackageView data) => data.Contains(MediaDragContent.Format) ||
        data.Contains(StandardDataFormats.StorageItems) || data.Contains(StandardDataFormats.Bitmap) ||
        data.Contains(StandardDataFormats.Html) || data.Contains(StandardDataFormats.Text) || data.Contains(StandardDataFormats.WebLink);

    internal static async Task<IReadOnlyList<MediaImport>> ReadAsync(DataPackageView data)
    {
        if (data.Contains(MediaDragContent.Format)) {
            return [new(MediaDragContent.Parse((string)await data.GetDataAsync(MediaDragContent.Format)))];
        }
        if (data.Contains(StandardDataFormats.StorageItems)) {
            var imports = new List<MediaImport>();
            foreach (var file in (await data.GetStorageItemsAsync()).OfType<StorageFile>()) {
                if (MediaContent.FileKind(file.Name) is not { } kind) { continue; }
                imports.Add(new(new(kind, file.DisplayName, null, null, "Local file", null), file.Path));
                if (imports.Count == 20) { break; }
            }
            if (imports.Count == 0) { throw new ArgumentException("Drop an image or video file, or selected definition text."); }
            return imports;
        }
        if (data.Contains(StandardDataFormats.Bitmap)) {
            var reference = await data.GetBitmapAsync();
            using var stream = await reference.OpenReadAsync();
            var bytes = await MediaCatalogClient.ReadImageAsync(stream.AsStreamForRead());
            return [new(new(MediaKind.Image, "Dropped image", null, null, "", null), Image: bytes)];
        }
        if (data.Contains(StandardDataFormats.Html)) {
            var html = await data.GetHtmlFormatAsync();
            if (MediaContent.ImageFromHtml(html) is { } image) {
                return [new(new(MediaKind.Image, "Dropped image", image.AbsoluteUri, null, "", null))];
            }
            if (!data.Contains(StandardDataFormats.Text) && !data.Contains(StandardDataFormats.WebLink)) {
                return [Text(MediaContent.PlainText(HtmlFormatHelper.GetStaticFragment(html)))];
            }
        }
        var text = data.Contains(StandardDataFormats.WebLink) ? (await data.GetWebLinkAsync()).AbsoluteUri : await data.GetTextAsync();
        if (Uri.TryCreate(text.Trim(), UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https") {
            var kind = MediaContent.FileKind(uri.AbsolutePath);
            if (kind is { }) { return [new(new(kind.Value, "Dropped " + kind.Value.ToString().ToLowerInvariant(), uri.AbsoluteUri, null, "", null))]; }
            throw new ArgumentException("Drop a direct image/video link, an image from the page, or selected text.");
        }
        return [Text(text)];
    }
    internal static MediaImport Text(string text, string title = "Definition", string credit = "", Uri? source = null) =>
        new(new(MediaKind.Definition, title, null, MediaContent.NormalizeText(text), credit, source?.AbsoluteUri));
    internal static MediaImport File(string path) => new(new(MediaContent.FileKind(path) ?? throw new ArgumentException("Choose an image or video file."),
        System.IO.Path.GetFileNameWithoutExtension(path), null, null, "Local file", null), path);
}
