using System.Net;
using System.Text.Json;

namespace ScreenInk.Media;

public sealed class MediaCatalogClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly bool _ownsClient;
    private readonly SemaphoreSlim _requests = new(1, 1);
    public MediaCatalogClient(HttpClient? client = null)
    {
        _ownsClient = client is null;
        _http = client ?? new HttpClient(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All }) { Timeout = TimeSpan.FromSeconds(25) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("ScreenInk/0.1 (local Windows annotation app; Wikimedia Commons media lookup)");
    }

    public async Task<IReadOnlyList<ImageSearchResult>> SearchImagesAsync(string query, CancellationToken token = default)
    {
        query = query.Trim();
        if (query.Length is 0 or > 150) { throw new ArgumentException("Enter an image search of up to 150 characters."); }
        var uri = new Uri("https://commons.wikimedia.org/w/api.php?action=query&generator=search&gsrnamespace=6&gsrlimit=12&prop=imageinfo&iiprop=url%7Cextmetadata&iiurlwidth=1280&iiextmetadatafilter=Artist%7CLicenseShortName%7CAttribution&format=json&formatversion=2&gsrsearch=" + Uri.EscapeDataString(query + " filetype:bitmap"));
        using var response = await GetAsync(uri, token);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
        var root = document.RootElement;
        if (root.TryGetProperty("error", out _)) { throw new HttpRequestException("Image search is temporarily unavailable. Try a different search."); }
        if (!root.TryGetProperty("query", out var results) || !results.TryGetProperty("pages", out var pages)) { return []; }
        var output = new List<ImageSearchResult>();
        foreach (var page in pages.EnumerateArray().OrderBy(p => p.TryGetProperty("index", out var index) ? index.GetInt32() : int.MaxValue)) {
            if (!page.TryGetProperty("imageinfo", out var info) || info.GetArrayLength() == 0) { continue; }
            var image = info[0];
            var url = String(image, "thumburl");
            if (string.IsNullOrEmpty(url)) { url = String(image, "url"); }
            if (string.IsNullOrEmpty(url)) { continue; }
            var title = String(page, "title").Replace("File:", "", StringComparison.Ordinal);
            var pageUrl = String(image, "descriptionurl");
            if (string.IsNullOrEmpty(pageUrl)) { continue; }
            var credit = "Wikimedia Commons";
            if (image.TryGetProperty("extmetadata", out var metadata)) {
                var artist = Metadata(metadata, "Artist"); var license = Metadata(metadata, "LicenseShortName");
                credit = string.Join(" · ", new[] { artist, license, "Wikimedia Commons" }.Where(s => !string.IsNullOrWhiteSpace(s)));
            }
            try { output.Add(new(title.Length > 500 ? title[..500] : title, MediaContent.WebUri(url), MediaContent.WebUri(pageUrl), credit.Length > 2000 ? credit[..2000] : credit)); }
            catch (ArgumentException) { }
        }
        return output;
    }

    public async Task<IReadOnlyList<DefinitionResult>> DefineAsync(string word, CancellationToken token = default)
    {
        word = word.Trim();
        if (word.Length is 0 or > 80) { throw new ArgumentException("Enter an English word of up to 80 characters."); }
        using var response = await GetAsync(new Uri("https://api.dictionaryapi.dev/api/v2/entries/en/" + Uri.EscapeDataString(word)), token);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
        if (document.RootElement.ValueKind != JsonValueKind.Array) { return []; }
        var output = new List<DefinitionResult>();
        foreach (var entry in document.RootElement.EnumerateArray()) {
            if (!entry.TryGetProperty("meanings", out var meanings)) { continue; }
            Uri? source = null;
            if (entry.TryGetProperty("sourceUrls", out var sources) && sources.GetArrayLength() > 0) {
                try { source = MediaContent.WebUri(sources[0].GetString() ?? ""); } catch (ArgumentException) { }
            }
            var credit = "Free Dictionary API";
            if (entry.TryGetProperty("license", out var license)) { credit += " · " + String(license, "name"); }
            foreach (var meaning in meanings.EnumerateArray()) {
                if (!meaning.TryGetProperty("definitions", out var definitions)) { continue; }
                foreach (var definition in definitions.EnumerateArray()) {
                    var text = String(definition, "definition");
                    if (string.IsNullOrWhiteSpace(text)) { continue; }
                    output.Add(new(String(entry, "word"), String(meaning, "partOfSpeech") + " · " + text, credit, source));
                    if (output.Count == 6) { return output; }
                }
            }
        }
        return output;
    }

    public async Task<byte[]> DownloadImageAsync(Uri uri, CancellationToken token = default)
    {
        _ = MediaContent.WebUri(uri.AbsoluteUri);
        using var response = await GetAsync(uri, token);
        if (response.Content.Headers.ContentType?.MediaType is { } type && !type.StartsWith("image/", StringComparison.OrdinalIgnoreCase)) {
            throw new ArgumentException("This link points to a webpage. Drop an image file or a direct image link.");
        }
        if (response.Content.Headers.ContentLength > MediaContent.MaxImageBytes) { throw new ArgumentException("Choose an image smaller than 25 MB."); }
        await using var stream = await response.Content.ReadAsStreamAsync(token);
        return await ReadImageAsync(stream, token);
    }
    public static async Task<byte[]> ReadImageAsync(Stream stream, CancellationToken token = default)
    {
        using var output = new MemoryStream();
        var block = new byte[65536];
        int count;
        while ((count = await stream.ReadAsync(block, token)) > 0) {
            if (output.Length + count > MediaContent.MaxImageBytes) { throw new ArgumentException("Choose an image smaller than 25 MB."); }
            output.Write(block, 0, count);
        }
        if (output.Length == 0) { throw new ArgumentException("The image was empty."); }
        return output.ToArray();
    }
    private async Task<HttpResponseMessage> GetAsync(Uri uri, CancellationToken token)
    {
        await _requests.WaitAsync(token);
        try {
            var response = await _http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token);
            if (!response.IsSuccessStatusCode) { var status = (int)response.StatusCode; response.Dispose(); throw new HttpRequestException($"The source is unavailable ({status}). Try again, or drag/paste content instead."); }
            return response;
        } finally { _requests.Release(); }
    }
    private static string String(JsonElement element, string name) => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
    private static string Metadata(JsonElement metadata, string name) => metadata.TryGetProperty(name, out var property) ? MediaContent.PlainText(String(property, "value")) : "";
    public void Dispose() { if (_ownsClient) { _http.Dispose(); } }
}
