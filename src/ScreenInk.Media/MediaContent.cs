using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ScreenInk.Media;

public enum MediaKind { Image, Video, Definition }
public sealed record ImageSearchResult(string Title, Uri ImageUri, Uri PageUri, string Credit);
public sealed record DefinitionResult(string Word, string Meaning, string Credit, Uri? Source);
public sealed record MediaDragContent(MediaKind Kind, string Title, string? Url, string? Text, string Credit, string? Source)
{
    public const string Format = "ScreenInk.MediaContent.v1";
    public string Serialize() => JsonSerializer.Serialize(this);
    public static MediaDragContent Parse(string json)
    {
        if (json.Length > 20000) { throw new ArgumentException("This drop contains too much text."); }
        var content = JsonSerializer.Deserialize<MediaDragContent>(json) ?? throw new ArgumentException("Empty media drop.");
        if (!Enum.IsDefined(content.Kind)) { throw new ArgumentException("Unsupported media type."); }
        if (content.Title is null || content.Credit is null || content.Title.Length > 500 || content.Credit.Length > 2000) { throw new ArgumentException("Invalid media description."); }
        if (content.Kind == MediaKind.Definition) { _ = MediaContent.NormalizeText(content.Text ?? ""); }
        else { _ = MediaContent.WebUri(content.Url ?? ""); }
        if (!string.IsNullOrEmpty(content.Source)) { _ = MediaContent.WebUri(content.Source); }
        return content;
    }
}

public static partial class MediaContent
{
    public const int MaxImageBytes = 25 * 1024 * 1024;
    public static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp", ".tif", ".tiff"];
    public static readonly string[] VideoExtensions = [".mp4", ".m4v", ".mov", ".wmv", ".avi", ".webm", ".mkv"];
    public static MediaKind? FileKind(string name)
    {
        var extension = Path.GetExtension(name).ToLowerInvariant();
        return ImageExtensions.Contains(extension) ? MediaKind.Image : VideoExtensions.Contains(extension) ? MediaKind.Video : null;
    }
    public static Uri WebUri(string text)
    {
        if (!Uri.TryCreate(text.Trim(), UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(uri.UserInfo)) {
            throw new ArgumentException("Use an HTTP or HTTPS image/video link.");
        }
        return uri;
    }
    public static string NormalizeText(string text)
    {
        text = text.Replace("\r\n", "\n").Replace('\r', '\n').Replace("\0", "").Trim();
        if (text.Length == 0) { throw new ArgumentException("Drop or paste some definition text first."); }
        if (text.Length > 12000) { throw new ArgumentException("Keep each text card under 12,000 characters."); }
        return text;
    }
    public static string PlainText(string html) => WebUtility.HtmlDecode(HtmlTag().Replace(html, " ")).Trim();
    public static Uri? ImageFromHtml(string html)
    {
        if (html.Length > 1000000) { return null; }
        var match = ImageSource().Match(html);
        if (!match.Success) { return null; }
        try { return WebUri(WebUtility.HtmlDecode(match.Groups[1].Value)); } catch (ArgumentException) { return null; }
    }
    [GeneratedRegex("<[^>]*>", RegexOptions.Singleline, 500)] private static partial Regex HtmlTag();
    [GeneratedRegex("<img\\b[^>]*?\\bsrc\\s*=\\s*[\"']([^\"']+)[\"']", RegexOptions.IgnoreCase | RegexOptions.Singleline, 500)] private static partial Regex ImageSource();
}
