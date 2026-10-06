using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using ScreenInk.App.Services;
using ScreenInk.Media;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage;
using Windows.Graphics;

namespace ScreenInk.App;

internal sealed class MediaCardWindow : Window
{
    private readonly Border _root;
    private readonly Grid _body;
    private readonly TextBlock _notice;
    private MediaPlayer? _player;
    private MediaPlayerElement? _video;
    private bool _closed;
    internal MediaCardWindow(MediaDragContent content)
    {
        Title = "ScreenInk · " + content.Title;
        var presenter = OverlappedPresenter.CreateForToolWindow();
        presenter.IsAlwaysOnTop = true; presenter.IsMaximizable = false; presenter.IsMinimizable = false;
        AppWindow.SetPresenter(presenter);
        _body = new Grid();
        _notice = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new(12), Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 230, 130, 130)), Visibility = Visibility.Collapsed };
        var layout = new Grid { RowSpacing = 8, Padding = new(12) };
        layout.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new() { Height = GridLength.Auto });
        layout.Children.Add(_body);
        var footer = new StackPanel { Spacing = 4 }; Grid.SetRow(footer, 1); layout.Children.Add(footer);
        if (content.Credit.Length > 0) { footer.Children.Add(new TextBlock { Text = content.Credit, FontSize = 11, TextWrapping = TextWrapping.Wrap, MaxHeight = 48 }); }
        if (!string.IsNullOrEmpty(content.Source)) { footer.Children.Add(new HyperlinkButton { Content = "Source & license ↗", NavigateUri = MediaContent.WebUri(content.Source), Padding = new(0), FontSize = 11 }); }
        footer.Children.Add(_notice);
        _root = new Border { Child = layout, Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 30, 30, 39)), CornerRadius = new(12), RequestedTheme = ElementTheme.Dark };
        Content = _root;
        Closed += (_, _) => { _closed = true; if (_player is not null) { _player.MediaFailed -= OnMediaFailed; _video?.SetMediaPlayer(null); _player.Dispose(); _player = null; } };
    }
    internal async Task LoadAsync(MediaImport import, MediaCatalogClient catalog, CancellationToken token)
    {
        var content = import.Content;
        switch (content.Kind) {
            case MediaKind.Image:
                byte[] bytes;
                if (import.Image is { } imageBytes) { bytes = imageBytes; }
                else if (import.Path is { } path) { await using var file = System.IO.File.OpenRead(path); bytes = await MediaCatalogClient.ReadImageAsync(file, token); }
                else { bytes = await catalog.DownloadImageAsync(MediaContent.WebUri(content.Url ?? ""), token); }
                var bitmap = await ScreenshotImage.PreviewAsync(bytes);
                if (_closed) { return; }
                _body.Children.Add(new Image { Source = bitmap, Stretch = Stretch.Uniform });
                break;
            case MediaKind.Video:
                _player = new MediaPlayer { AutoPlay = false };
                _player.MediaFailed += OnMediaFailed;
                var source = import.Path is { } videoPath ? MediaSource.CreateFromStorageFile(await StorageFile.GetFileFromPathAsync(videoPath)) : MediaSource.CreateFromUri(MediaContent.WebUri(content.Url ?? ""));
                if (_closed) { source.Dispose(); return; }
                _player.Source = source;
                _video = new MediaPlayerElement { AreTransportControlsEnabled = true, AutoPlay = false, Stretch = Stretch.Uniform };
                _video.SetMediaPlayer(_player); _body.Children.Add(_video);
                break;
            case MediaKind.Definition:
                _body.Children.Add(new ScrollViewer { Content = new TextBlock { Text = MediaContent.NormalizeText(content.Text ?? ""), FontSize = 18, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true }, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
                break;
        }
    }
    private void OnMediaFailed(MediaPlayer sender, MediaPlayerFailedEventArgs args) => DispatcherQueue.TryEnqueue(() => {
        if (_closed) { return; } _notice.Text = "This video could not play. Try MP4 (H.264) or another format supported by Windows."; _notice.Visibility = Visibility.Visible;
    });
    internal void Place(int x, int y, DisplayArea display, double scale)
    {
        var work = display.WorkArea;
        var width = Math.Min((int)(420 * scale), work.Width); var height = Math.Min((int)(320 * scale), work.Height);
        AppWindow.MoveAndResize(new RectInt32(Math.Clamp(x, work.X, work.X + work.Width - width), Math.Clamp(y, work.Y, work.Y + work.Height - height), width, height));
    }
    internal void HideCard() { _player?.Pause(); AppWindow.Hide(); }
    internal void ShowCard() { AppWindow.Show(); }
}
