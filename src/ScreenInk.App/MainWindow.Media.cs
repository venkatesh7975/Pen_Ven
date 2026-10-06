using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using ScreenInk.App.Services;
using ScreenInk.Core.Models;
using ScreenInk.Media;
using ScreenInk.Native;
using Windows.Graphics;

namespace ScreenInk.App;

public sealed partial class MainWindow
{
    private readonly MediaCatalogClient _mediaCatalog = new();
    private readonly CancellationTokenSource _mediaLifetime = new();
    private readonly List<MediaCardWindow> _mediaCards = [];
    private MediaLibraryWindow? _mediaLibrary;
    private DesktopDropTarget? _mediaDrop;
    private int _mediaLoading;
    private bool _libraryHidden;

    private void OnMediaClicked(object sender, RoutedEventArgs args)
    {
        _overlay?.SetMode(OverlayMode.ClickThrough);
        if (_mediaLibrary is null) {
            _mediaLibrary = new(this, _mediaCatalog);
            _mediaLibrary.Closed += (_, _) => { _mediaLibrary = null; _libraryHidden = false; };
            var work = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest).WorkArea;
            var width = Math.Min((int)(440 * _scale), work.Width);
            var height = Math.Min((int)(650 * _scale), work.Height);
            _mediaLibrary.AppWindow.MoveAndResize(new RectInt32(Math.Clamp(AppWindow.Position.X - width - 12, work.X, work.X + work.Width - width),
                Math.Clamp(AppWindow.Position.Y, work.Y, work.Y + work.Height - height), width, height));
        }
        _mediaLibrary.Activate();
    }
    internal async Task AddMediaAsync(MediaImport import, int? x = null, int? y = null)
    {
        if (_closed) { return; }
        if (_mediaCards.Count + _mediaLoading >= 20) { throw new InvalidOperationException("Close a card first. ScreenInk supports up to 20 media cards."); }
        _mediaLoading++;
        var card = new MediaCardWindow(import.Content);
        try {
            await card.LoadAsync(import, _mediaCatalog, _mediaLifetime.Token);
            if (_closed) { card.Close(); return; }
            _mediaCards.Add(card); card.Closed += (_, _) => _mediaCards.Remove(card);
            var display = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest);
            var offset = (_mediaCards.Count - 1) % 8 * (int)(24 * _scale);
            card.Place(x ?? display.WorkArea.X + (int)(72 * _scale) + offset, y ?? display.WorkArea.Y + (int)(72 * _scale) + offset, display, _scale);
            _overlay?.SetMode(OverlayMode.ClickThrough);
            if (_collapsed) { card.HideCard(); } else { card.Activate(); }
        } catch { card.Close(); throw; }
        finally { _mediaLoading--; }
    }
    internal void BeginMediaDrag(MediaLibraryWindow library)
    {
        _overlay?.SetMode(OverlayMode.ClickThrough);
        if (_mediaDrop is null) {
            _mediaDrop = new(MediaDragContent.Format);
            _mediaDrop.Dropped += OnDesktopMediaDropped;
            _mediaDrop.Failed += error => _mediaLibrary?.Report(error.Message);
        }
        _mediaDrop.Show(DesktopCapture.MonitorForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)),
            WinRT.Interop.WindowNative.GetWindowHandle(this), WinRT.Interop.WindowNative.GetWindowHandle(library));
    }
    internal void EndMediaDrag() => _mediaDrop?.Hide();
    private async void OnDesktopMediaDropped(DesktopMediaDrop drop)
    {
        try { await AddMediaAsync(new(MediaDragContent.Parse(drop.Content)), drop.X, drop.Y); _mediaLibrary?.Report("Added where you dropped it. Use Pen to annotate."); }
        catch (Exception error) { if (!_closed) { _mediaLibrary?.Report(error.Message); } }
    }
    private void HideMedia()
    {
        EndMediaDrag();
        foreach (var card in _mediaCards) { card.HideCard(); }
        _libraryHidden = _mediaLibrary is not null;
        _mediaLibrary?.AppWindow.Hide();
    }
    private void ShowMedia()
    {
        foreach (var card in _mediaCards) { card.ShowCard(); }
        if (_libraryHidden) { _mediaLibrary?.AppWindow.Show(); _libraryHidden = false; }
    }
    private void CloseMedia()
    {
        _mediaLifetime.Cancel(); _mediaDrop?.Dispose(); _mediaDrop = null;
        _mediaLibrary?.Close();
        foreach (var card in _mediaCards.ToArray()) { card.Close(); }
        _mediaCatalog.Dispose();
    }
}
