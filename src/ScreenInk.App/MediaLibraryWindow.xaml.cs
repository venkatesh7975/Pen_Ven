using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using ScreenInk.App.Services;
using ScreenInk.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;

namespace ScreenInk.App;

public sealed partial class MediaLibraryWindow : Window
{
    private readonly MainWindow _owner;
    private readonly MediaCatalogClient _catalog;
    private readonly CancellationTokenSource _lifetime = new();
    private bool _busy;
    internal MediaLibraryWindow(MainWindow owner, MediaCatalogClient catalog)
    {
        InitializeComponent(); _owner = owner; _catalog = catalog;
        var presenter = OverlappedPresenter.CreateForToolWindow(); presenter.IsAlwaysOnTop = true; presenter.IsMaximizable = false; presenter.IsMinimizable = false;
        AppWindow.SetPresenter(presenter);
        Closed += (_, _) => { _lifetime.Cancel(); _owner.EndMediaDrag(); };
    }
    internal void Report(string text) => Status.Text = text;
    private void OnDragOver(object sender, DragEventArgs args)
    {
        args.AcceptedOperation = MediaDropReader.Supports(args.DataView) ? DataPackageOperation.Copy : DataPackageOperation.None;
        args.DragUIOverride.Caption = "Add to ScreenInk"; args.Handled = true;
    }
    private async void OnDrop(object sender, DragEventArgs args)
    {
        var deferral = args.GetDeferral(); args.Handled = true;
        try {
            var imports = await MediaDropReader.ReadAsync(args.DataView);
            foreach (var import in imports) { await _owner.AddMediaAsync(import); }
            Report("Added. Use Cursor to move cards and Pen to annotate.");
        } catch (Exception error) { Report(error.Message); }
        finally { deferral.Complete(); _owner.EndMediaDrag(); }
    }
    private async void OnFilesClicked(object sender, RoutedEventArgs args)
    {
        try {
            var picker = new Microsoft.Windows.Storage.Pickers.FileOpenPicker(AppWindow.Id);
            foreach (var extension in MediaContent.ImageExtensions.Concat(MediaContent.VideoExtensions)) { picker.FileTypeFilter.Add(extension); }
            var files = await picker.PickMultipleFilesAsync();
            foreach (var file in files) { await _owner.AddMediaAsync(MediaDropReader.File(file.Path)); }
            if (files.Count > 0) { Report("Added. Use Cursor to move cards and Pen to annotate."); }
        } catch (Exception error) { Report(error.Message); }
    }
    private void OnSearchKeyDown(object sender, KeyRoutedEventArgs args) { if (args.Key == VirtualKey.Enter) { args.Handled = true; OnSearchClicked(sender, new()); } }
    private async void OnSearchClicked(object sender, RoutedEventArgs args)
    {
        if (_busy) { return; } SetBusy(true); ImageResults.Children.Clear(); Report("Finding images…");
        try {
            var results = await _catalog.SearchImagesAsync(SearchBox.Text, _lifetime.Token);
            foreach (var result in results) {
                var content = new MediaDragContent(MediaKind.Image, result.Title, result.ImageUri.AbsoluteUri, null, result.Credit, result.PageUri.AbsoluteUri);
                var image = new Image { Source = new BitmapImage(result.ImageUri), Height = 126, Stretch = Stretch.Uniform };
                AddResult(ImageResults, content, image);
            }
            Report(results.Count == 0 ? "No images found. Try another search." : "Drag an image onto the screen, or click Add. Source & license stay with it.");
        } catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception error) { Report(error.Message); }
        finally { SetBusy(false); }
    }
    private async void OnDefineClicked(object sender, RoutedEventArgs args)
    {
        if (_busy) { return; } SetBusy(true); WordResults.Children.Clear(); Report("Looking up the word…");
        try {
            var results = await _catalog.DefineAsync(WordBox.Text, _lifetime.Token);
            foreach (var result in results) {
                AddResult(WordResults, new(MediaKind.Definition, result.Word, null, result.Meaning, result.Credit, result.Source?.AbsoluteUri),
                    new TextBlock { Text = result.Meaning, TextWrapping = TextWrapping.Wrap });
            }
            Report(results.Count == 0 ? "No definition found. Paste or drop text instead." : "Drag a definition onto the screen, or click Add.");
        } catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception error) { Report(error.Message); }
        finally { SetBusy(false); }
    }
    private async void OnTextClicked(object sender, RoutedEventArgs args)
    {
        try { await _owner.AddMediaAsync(MediaDropReader.Text(DefinitionBox.Text, string.IsNullOrWhiteSpace(WordBox.Text) ? "Text" : WordBox.Text.Trim())); Report("Text card added."); }
        catch (Exception error) { Report(error.Message); }
    }
    private void SetBusy(bool busy) { _busy = busy; SearchButton.IsEnabled = DefineButton.IsEnabled = !busy; }
    private void AddResult(StackPanel results, MediaDragContent content, UIElement preview)
    {
        var stack = new StackPanel { Spacing = 6 };
        stack.Children.Add(preview);
        stack.Children.Add(new TextBlock { Text = content.Title, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, MaxHeight = 48 });
        if (content.Credit.Length > 0) { stack.Children.Add(new TextBlock { Text = content.Credit, FontSize = 11, TextWrapping = TextWrapping.Wrap, MaxHeight = 32 }); }
        var add = new Button { Content = "＋ Add", HorizontalAlignment = HorizontalAlignment.Right };
        add.Click += async (_, _) => { add.IsEnabled = false; try { await _owner.AddMediaAsync(new(content)); Report("Added. Use Cursor to move cards and Pen to annotate."); } catch (Exception error) { Report(error.Message); } finally { add.IsEnabled = true; } };
        stack.Children.Add(add);
        var card = new Border { Child = stack, Padding = new(12), CornerRadius = new(12), BorderThickness = new(1), BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 72, 71, 86)), CanDrag = true };
        card.DragStarting += (_, args) => {
            try {
                args.Data.SetData(MediaDragContent.Format, content.Serialize());
                args.Data.SetText(content.Serialize());
                args.Data.RequestedOperation = DataPackageOperation.Copy; _owner.BeginMediaDrag(this);
            } catch (Exception error) { args.Cancel = true; Report(error.Message); }
        };
        card.DropCompleted += (_, _) => _owner.EndMediaDrag();
        results.Children.Add(card);
    }
}
