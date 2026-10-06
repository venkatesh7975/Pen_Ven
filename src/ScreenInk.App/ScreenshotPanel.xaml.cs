using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.Windows.Storage.Pickers;
using ScreenInk.App.Services;
using ScreenInk.Sharing;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Streams;

namespace ScreenInk.App;

public sealed partial class ScreenshotPanel : UserControl, IDisposable
{
    private readonly MainWindow _owner;
    private readonly byte[] _png;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private ScreenshotShareServer? _share;
    private bool _disposed, _starting;
    internal event Action<bool>? SharingChanged;

    internal ScreenshotPanel(MainWindow owner, byte[] png)
    {
        InitializeComponent();
        _owner = owner; _png = png;
        _timer.Tick += OnTimerTick;
    }

    internal async Task LoadPreviewAsync() => Preview.Source = await ScreenshotImage.PreviewAsync(_png);
    private void OnNewClicked(object sender, RoutedEventArgs args) => _owner.TakeNewScreenshot();
    private void OnSaveShortcut(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        if (!_disposed && SaveButton.IsEnabled) { OnSaveClicked(this, new()); }
    }
    private void OnCopyShortcut(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        if (!_disposed && CopyButton.IsEnabled) { OnCopyClicked(this, new()); }
    }
    private void OnShareShortcut(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        if (!_disposed && ShareButton.IsEnabled) { OnShareClicked(this, new()); }
    }
    private void OnNewShortcut(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        if (!_disposed && SaveButton.IsEnabled) { OnNewClicked(this, new()); }
    }

    private async void OnSaveClicked(object sender, RoutedEventArgs args)
    {
        SaveButton.IsEnabled = false;
        _owner.SetScreenshotPickerOpen(true);
        try {
            var picker = new FileSavePicker(_owner.AppWindow.Id) {
                SuggestedFileName = $"ScreenInk-{DateTime.Now:yyyy-MM-dd-HHmmss}",
                DefaultFileExtension = ".png",
                SuggestedStartLocation = PickerLocationId.PicturesLibrary
            };
            picker.FileTypeChoices.Add("PNG image", new List<string> { ".png" });
            var file = await picker.PickSaveFileAsync();
            if (file is not null) { await File.WriteAllBytesAsync(file.Path, _png); SetStatus("Saved ♡"); }
        } catch (Exception e) { SetStatus($"Could not save: {e.Message}"); }
        finally { _owner.SetScreenshotPickerOpen(false); if (!_disposed) { SaveButton.IsEnabled = true; } }
    }

    private async void OnCopyClicked(object sender, RoutedEventArgs args)
    {
        CopyButton.IsEnabled = false;
        try {
            using var stream = await ScreenshotImage.StreamAsync(_png);
            if (_disposed) { return; }
            var package = new DataPackage();
            package.SetBitmap(RandomAccessStreamReference.CreateFromStream(stream));
            Clipboard.SetContent(package);
            Clipboard.Flush();
            SetStatus("Copied ♡");
        } catch (Exception e) { SetStatus($"Could not copy: {e.Message}"); }
        finally { if (!_disposed) { CopyButton.IsEnabled = true; } }
    }

    private async void OnShareClicked(object sender, RoutedEventArgs args)
    {
        if (_starting || _disposed) { return; }
        if (_share?.IsRunning == true) { ShareArea.Visibility = Visibility.Visible; return; }
        try {
            var networks = ShareNetwork.Available();
            if (networks.Count == 0) { SetStatus("Connect to Wi-Fi or Ethernet, then try Share QR."); return; }
            NetworkChoice.ItemsSource = networks;
            NetworkChoice.SelectedIndex = 0;
            NetworkChoice.Visibility = networks.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
            await StartShareAsync();
        } catch (Exception e) { SetStatus($"Could not share: {e.Message}"); }
    }

    private async Task StartShareAsync()
    {
        if (_starting || _disposed || NetworkChoice.SelectedItem is not ShareNetwork network) { return; }
        _starting = true;
        ShareButton.IsEnabled = false;
        NetworkChoice.IsEnabled = false;
        StopShare();
        ScreenshotShareServer? session = null;
        try {
            session = new ScreenshotShareServer(network.Address, _png);
            var qr = await ScreenshotImage.PreviewAsync(ShareQrCode.Create(session.Address));
            if (_disposed) { session.Dispose(); return; }
            _share = session;
            QrImage.Source = qr;
            PanelTitle.Text = "Scan to share ♡";
            PreviewBorder.Visibility = Visibility.Collapsed;
            ShareArea.Visibility = Visibility.Visible;
            CopyLinkButton.IsEnabled = true; StopButton.IsEnabled = true;
            Status.Text = "If the link won't open, allow ScreenInk on your private network.";
            _timer.Start(); OnTimerTick(this, null!);
            SharingChanged?.Invoke(true);
        } catch (Exception e) { session?.Dispose(); SetStatus($"Could not share: {e.Message}"); }
        finally { _starting = false; if (!_disposed) { ShareButton.IsEnabled = true; NetworkChoice.IsEnabled = true; } }
    }

    private async void OnNetworkChanged(object sender, SelectionChangedEventArgs args)
    {
        if (_share?.IsRunning == true) { await StartShareAsync(); }
    }
    private void OnCopyLinkClicked(object sender, RoutedEventArgs args)
    {
        if (_share?.IsRunning != true) { return; }
        try { var package = new DataPackage(); package.SetText(_share.Address.AbsoluteUri); Clipboard.SetContent(package); Clipboard.Flush(); SetStatus("Link copied ♡"); }
        catch (Exception e) { SetStatus($"Could not copy link: {e.Message}"); }
    }
    private void OnStopClicked(object sender, RoutedEventArgs args) { StopShare(); SetStatus("Sharing stopped."); }
    private void OnTimerTick(object? sender, object args)
    {
        if (_share?.IsRunning != true) { StopShare(); SetStatus("Share ended. Tap Share QR to start again."); return; }
        var seconds = Math.Max(0, (int)(_share.ExpiresAt - DateTimeOffset.UtcNow).TotalSeconds);
        ShareStatus.Text = $"Ends in {seconds / 60}:{seconds % 60:00} · {(NetworkChoice.SelectedItem is ShareNetwork n ? n.Name : "local network")}";
    }
    private void StopShare()
    {
        var wasSharing = _share is not null;
        _timer.Stop(); _share?.Dispose(); _share = null;
        QrImage.Source = null; ShareArea.Visibility = Visibility.Collapsed;
        PreviewBorder.Visibility = Visibility.Visible; PanelTitle.Text = "Little snapshot";
        CopyLinkButton.IsEnabled = false; StopButton.IsEnabled = false;
        if (wasSharing) { SharingChanged?.Invoke(false); }
    }
    private void SetStatus(string message) { if (!_disposed) { Status.Text = message; } }
    public void Dispose()
    {
        if (_disposed) { return; }
        _disposed = true; StopShare(); _timer.Tick -= OnTimerTick;
        Preview.Source = null;
    }
}
