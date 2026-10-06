using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace ScreenInk.Sharing;

// A single immutable screenshot, explicitly shared for a short session. No file-system routes.
public sealed class ScreenshotShareServer : IDisposable
{
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _stop = new();
    private readonly byte[] _png;
    private readonly string _path;
    private readonly byte[] _page;
    private int _stopped;
    private int _disposed;
    public Uri Address { get; }
    public DateTimeOffset ExpiresAt { get; }
    public bool IsRunning => Volatile.Read(ref _stopped) == 0;
    public Task Completion { get; }

    public ScreenshotShareServer(IPAddress bindAddress, byte[] png, TimeSpan? lifetime = null)
    {
        ArgumentNullException.ThrowIfNull(bindAddress);
        ArgumentNullException.ThrowIfNull(png);
        if (!ShareNetwork.IsPrivate(bindAddress) && !IPAddress.IsLoopback(bindAddress)) { throw new ArgumentException("Choose a local Wi-Fi or Ethernet address.", nameof(bindAddress)); }
        if (png.Length < 8 || !png.AsSpan(0, 8).SequenceEqual(new byte[] {137,80,78,71,13,10,26,10})) { throw new ArgumentException("A PNG screenshot is required.", nameof(png)); }
        var duration = lifetime ?? TimeSpan.FromMinutes(10);
        if (duration <= TimeSpan.Zero || duration > TimeSpan.FromMinutes(10)) { throw new ArgumentOutOfRangeException(nameof(lifetime)); }
        _png = (byte[])png.Clone();
        _path = "/" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        _listener = new TcpListener(bindAddress, 0);
        _listener.Start(8);
        var port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        Address = new Uri($"http://{bindAddress}:{port}{_path}");
        ExpiresAt = DateTimeOffset.UtcNow + duration;
        _page = Encoding.UTF8.GetBytes($$"""
            <!doctype html><html lang="en"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
            <title>ScreenInk screenshot</title><style>body{margin:0;padding:20px;background:#17191f;color:#fff;font:16px system-ui;text-align:center}header{display:flex;justify-content:space-between;align-items:center;max-width:1100px;margin:0 auto 16px}a{color:#b7baff;padding:10px;text-decoration:none}img{max-width:100%;height:auto;border-radius:12px}p{color:#bfc1cc;font-size:13px}</style>
            <header><b>ScreenInk</b><a href="{{_path}}/screenshot.png" download="ScreenInk.png">Save image ↓</a></header>
            <img src="{{_path}}/screenshot.png" alt="Shared screenshot"><p>A snapshot · shared from the presenter's computer</p></html>
            """);
        _stop.CancelAfter(duration);
        _stop.Token.Register(() => { Interlocked.Exchange(ref _stopped, 1); _listener.Stop(); });
        Completion = ServeAsync();
    }

    private async Task ServeAsync()
    {
        var active = new List<Task>();
        try {
            while (!_stop.IsCancellationRequested) {
                var client = await _listener.AcceptTcpClientAsync(_stop.Token).ConfigureAwait(false);
                active.RemoveAll(t => t.IsCompleted);
                if (active.Count >= 8) { client.Dispose(); continue; }
                active.Add(RespondAsync(client));
            }
        } catch (Exception e) when (e is OperationCanceledException or SocketException or ObjectDisposedException) {
            // Closing/expiration interrupts Accept. No uncaught background failures.
        } finally {
            Interlocked.Exchange(ref _stopped, 1);
            _stop.Cancel();
            _listener.Stop();
            await Task.WhenAll(active).ConfigureAwait(false);
        }
    }

    private async Task RespondAsync(TcpClient client)
    {
        using (client)
        using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token)) {
            deadline.CancelAfter(TimeSpan.FromSeconds(5));
            try {
                var stream = client.GetStream();
                var request = new byte[8192];
                var count = 0;
                while (count < request.Length) {
                    var read = await stream.ReadAsync(request.AsMemory(count), deadline.Token).ConfigureAwait(false);
                    if (read == 0) { return; }
                    count += read;
                    if (Encoding.ASCII.GetString(request, 0, count).Contains("\r\n\r\n", StringComparison.Ordinal)) { break; }
                }
                var header = Encoding.ASCII.GetString(request, 0, count);
                if (!header.Contains("\r\n\r\n", StringComparison.Ordinal)) { return; }
                var lineEnd = header.IndexOf("\r\n", StringComparison.Ordinal);
                var first = header[..lineEnd].Split(' ');
                if (first.Length != 3 || first[2] is not ("HTTP/1.1" or "HTTP/1.0")) { return; }
                var method = first[0];
                var path = first[1];
                var allowed = method is "GET" or "HEAD";
                var found = path == _path || path == _path + "/screenshot.png";
                var status = !allowed ? "405 Method Not Allowed" : !found ? "404 Not Found" : "200 OK";
                var body = !allowed || !found ? Encoding.UTF8.GetBytes("This share is unavailable.") : path == _path ? _page : _png;
                var type = allowed && found ? path == _path ? "text/html; charset=utf-8" : "image/png" : "text/plain; charset=utf-8";
                if (!IsRunning || DateTimeOffset.UtcNow >= ExpiresAt) { return; }
                var response = Encoding.ASCII.GetBytes($"HTTP/1.1 {status}\r\nContent-Type: {type}\r\nContent-Length: {body.Length}\r\nConnection: close\r\nCache-Control: no-store\r\nX-Content-Type-Options: nosniff\r\nReferrer-Policy: no-referrer\r\nContent-Security-Policy: default-src 'none'; img-src 'self'; style-src 'unsafe-inline'; frame-ancestors 'none'\r\n\r\n");
                deadline.CancelAfter(TimeSpan.FromSeconds(30));
                await stream.WriteAsync(response, deadline.Token).ConfigureAwait(false);
                if (method != "HEAD") { await stream.WriteAsync(body, deadline.Token).ConfigureAwait(false); }
            } catch (Exception e) when (e is IOException or SocketException or OperationCanceledException or ObjectDisposedException) { }
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) { return; }
        Interlocked.Exchange(ref _stopped, 1);
        _stop.Cancel();
        _listener.Stop();
        // Completion owns cancellation until any in-flight request has finished.
        _ = Completion.ContinueWith(_ => _stop.Dispose(), TaskScheduler.Default);
    }
}
