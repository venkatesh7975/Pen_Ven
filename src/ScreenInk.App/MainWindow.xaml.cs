using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Windowing;
using ScreenInk.App.ViewModels;
using ScreenInk.Core.Models;
using ScreenInk.Overlay;
using ScreenInk.Native;
using ScreenInk.App.Services;
using Windows.Graphics;
using Windows.UI;

namespace ScreenInk.App;

public sealed partial class MainWindow : Window
{
    private OverlayController? _overlay;
    private Windows.Foundation.Point? _dragStart;
    private PointInt32 _dragWindowStart;
    private uint? _dragPointer;
    private double _scale = 1;
    private bool _placing;
    private bool _collapsed, _changingToolbar, _capturing, _closed, _screenshotPickerOpen;
    private OverlayMode _resumeMode = OverlayMode.ClickThrough;
    private OverlayMode _lastMode = OverlayMode.Disabled;
    private OverlayMode? _screenshotMode;
    private Flyout? _screenshotFlyout;
    private ScreenshotPanel? _screenshotPanel;
    public MainViewModel ViewModel { get; } = new();

    public MainWindow()
    {
        InitializeComponent();
        ShortcutHelp.Text = FeatureShortcuts.HelpText;
        var presenter = OverlappedPresenter.CreateForToolWindow();
        presenter.SetBorderAndTitleBar(false, false);
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.IsAlwaysOnTop = true;
        AppWindow.SetPresenter(presenter);
        Closed += OnWindowClosed;
    }

    private void OnContentLoaded(object sender, RoutedEventArgs args)
    {
        ToolbarRoot.Loaded -= OnContentLoaded;
        _scale = ToolbarRoot.XamlRoot.RasterizationScale;
        PlaceToolbar(true);
        ToolbarRoot.XamlRoot.Changed += OnXamlRootChanged;
        AppWindow.Changed += OnAppWindowChanged;
        try {
            _overlay = new OverlayController(WinRT.Interop.WindowNative.GetWindowHandle(this), keepControlTopmost: true);
            _overlay.StatusChanged += OnOverlayStatusChanged;
            _overlay.FeatureShortcutInvoked += OnFeatureShortcutInvoked;
            ShortcutStatus.Text = _overlay.FeatureShortcutStatus;
            Refresh(_overlay.Status);
        } catch (Exception error) { ViewModel.ReportInitializationError(error); }
    }

    private void PlaceToolbar(bool initial = false)
    {
        if (_placing) { return; }
        _placing = true;
        try {
            var work = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest).WorkArea;
            var width = Math.Min((int)Math.Ceiling((_collapsed ? 56 : 72) * _scale), work.Width);
            var height = Math.Min((int)Math.Ceiling((_collapsed ? 56 : 716) * _scale), work.Height);
            var x = initial ? work.X + work.Width - width - (int)(16 * _scale) : AppWindow.Position.X;
            var y = initial ? work.Y + (work.Height - height) / 2 : AppWindow.Position.Y;
            AppWindow.MoveAndResize(new RectInt32(Math.Clamp(x, work.X, work.X + work.Width - width),
                Math.Clamp(y, work.Y, work.Y + work.Height - height), width, height));
        } finally { _placing = false; }
    }

    private void OnXamlRootChanged(XamlRoot sender, XamlRootChangedEventArgs args)
    {
        if (_scale == sender.RasterizationScale) { return; }
        _scale = sender.RasterizationScale;
        PlaceToolbar();
    }
    private void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (!_placing && args.DidPositionChange) { PlaceToolbar(); }
    }

    private Windows.Foundation.Point DesktopPoint(PointerRoutedEventArgs args)
    {
        var local = args.GetCurrentPoint(ToolbarRoot).Position;
        return new(AppWindow.Position.X + local.X * _scale, AppWindow.Position.Y + local.Y * _scale);
    }
    private void OnGripPressed(object sender, PointerRoutedEventArgs args)
    {
        if (_dragPointer.HasValue || !args.GetCurrentPoint(DragGrip).IsInContact) { return; }
        _dragStart = DesktopPoint(args);
        _dragWindowStart = AppWindow.Position;
        _dragPointer = args.Pointer.PointerId;
        if (!DragGrip.CapturePointer(args.Pointer)) { _dragStart = null; _dragPointer = null; }
        args.Handled = true;
    }
    private void OnGripMoved(object sender, PointerRoutedEventArgs args)
    {
        if (_dragStart is not { } origin || _dragPointer != args.Pointer.PointerId) { return; }
        var current = DesktopPoint(args);
        AppWindow.Move(new(_dragWindowStart.X + (int)Math.Round(current.X - origin.X),
            _dragWindowStart.Y + (int)Math.Round(current.Y - origin.Y)));
        args.Handled = true;
    }
    private void OnGripReleased(object sender, PointerRoutedEventArgs args)
    {
        if (_dragPointer != args.Pointer.PointerId) { return; }
        _dragStart = null; _dragPointer = null;
        DragGrip.ReleasePointerCapture(args.Pointer);
        args.Handled = true;
    }
    private void OnGripCaptureLost(object sender, PointerRoutedEventArgs args) { _dragStart = null; _dragPointer = null; }

    private void SelectTool(AnnotationTool tool)
    {
        if (_overlay is null) { return; }
        _overlay.SetTool(tool);
        _overlay.SetMode(OverlayMode.Draw);
    }
    private void OnPenClicked(object sender, RoutedEventArgs args) => SelectTool(AnnotationTool.Pen);
    private void OnEraserClicked(object sender, RoutedEventArgs args) => SelectTool(AnnotationTool.StrokeEraser);
    private void OnLaserClicked(object sender, RoutedEventArgs args) => SelectTool(AnnotationTool.Laser);
    private void OnShapeClicked(object sender, RoutedEventArgs args)
    {
        if (Enum.TryParse<AnnotationTool>((string)((MenuFlyoutItem)sender).Tag, out var tool) && tool.IsShape()) { SelectTool(tool); }
    }
    private void OnClickThroughClicked(object sender, RoutedEventArgs args) => _overlay?.SetMode(OverlayMode.ClickThrough);
    private void OnFeatureShortcutInvoked(FeatureAction action)
    {
        if (_closed || _capturing || _screenshotPickerOpen || _changingToolbar || _overlay is null) { return; }
        _screenshotFlyout?.Hide();
        RestoreScreenshotMode();
        ColorButton.Flyout?.Hide();
        SizeButton.Flyout?.Hide();
        MoreButton.Flyout?.Hide();
        ShapesButton.Flyout?.Hide();
        if (_overlay.ApplyFeatureShortcut(action)) { return; }
        if (action == FeatureAction.DeleteSelected) { OnDeleteSelectedClicked(this, new()); return; }
        ExpandToolbar(false);
        if (action == FeatureAction.Screenshot) { Activate(); TakeNewScreenshot(); return; }
        _overlay.SetMode(OverlayMode.ClickThrough);
        Activate();
        switch (action) {
            case FeatureAction.Media: OnMediaClicked(this, new()); break;
            case FeatureAction.Color: ColorButton.Flyout?.ShowAt(ColorButton); break;
            case FeatureAction.Size: SizeButton.Flyout?.ShowAt(SizeButton); break;
            case FeatureAction.Help: MoreButton.Flyout?.ShowAt(MoreButton); break;
        }
    }
    private void OnHideOverlayClicked(object sender, RoutedEventArgs args) => CollapseToolbar();
    private void CollapseToolbar()
    {
        if (_collapsed || _changingToolbar || _capturing) { return; }
        _changingToolbar = true;
        try {
            _screenshotFlyout?.Hide();
            HideMedia();
            _resumeMode = _overlay?.Mode is { } mode && mode != OverlayMode.Disabled ? mode : OverlayMode.ClickThrough;
            _overlay?.SetMode(OverlayMode.Disabled);
            _collapsed = true;
            ToolbarRoot.CornerRadius = new CornerRadius(28);
            ToolbarContent.Visibility = Visibility.Collapsed;
            OpenToolbarButton.Visibility = Visibility.Visible;
            PlaceToolbar();
        } finally { _changingToolbar = false; }
    }
    private void OnOpenToolbarClicked(object sender, RoutedEventArgs args) => ExpandToolbar(true);
    private void ExpandToolbar(bool restoreMode)
    {
        if (!_collapsed || _changingToolbar) { return; }
        _changingToolbar = true;
        try {
            _collapsed = false;
            ToolbarRoot.CornerRadius = new CornerRadius(14);
            OpenToolbarButton.Visibility = Visibility.Collapsed;
            ToolbarContent.Visibility = Visibility.Visible;
            PlaceToolbar();
            ShowMedia();
            if (restoreMode) { _overlay?.SetMode(_resumeMode); }
        } finally { _changingToolbar = false; }
    }

    private void OnScreenshotClicked(object sender, RoutedEventArgs args)
    {
        if (_capturing || _closed) { return; }
        if (_screenshotPanel is not null && _screenshotFlyout is not null) {
            _screenshotMode = _overlay?.Mode ?? OverlayMode.Disabled;
            if (_screenshotMode == OverlayMode.Draw) { _overlay?.SetMode(OverlayMode.ClickThrough); }
            _screenshotFlyout.ShowAt(ScreenshotButton);
            return;
        }
        TakeNewScreenshot();
    }
    internal async void TakeNewScreenshot()
    {
        if (_capturing || _closed) { return; }
        _capturing = true; ScreenshotButton.IsEnabled = false;
        _screenshotFlyout?.Hide();
        _screenshotPanel?.Dispose(); _screenshotPanel = null;
        var mode = _overlay?.Mode ?? OverlayMode.Disabled;
        _screenshotMode = mode;
        var hidden = false;
        try {
            var bounds = DesktopCapture.MonitorForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this));
            if (mode == OverlayMode.Draw) { _overlay?.SetMode(OverlayMode.ClickThrough); }
            // Keep the retained ink visible; remove only this toolbar before the desktop copy.
            AppWindow.Hide(); hidden = true;
            _mediaLibrary?.AppWindow.Hide();
            await Task.Delay(120);
            var pixels = await Task.Run(() => DesktopCapture.Capture(bounds));
            if (_closed) { return; }
            AppWindow.Show(); hidden = false;
            var png = await ScreenshotImage.EncodeAsync(pixels);
            if (_closed) { return; }
            var panel = new ScreenshotPanel(this, png);
            panel.SharingChanged += OnSharingChanged;
            _screenshotPanel = panel;
            await panel.LoadPreviewAsync();
            if (_closed) { return; }
            var flyout = new Flyout { Content = panel, Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.Left,
                ShouldConstrainToRootBounds = false };
            flyout.Closed += OnScreenshotClosed;
            _screenshotFlyout = flyout;
            flyout.ShowAt(ScreenshotButton);
        } catch (Exception error) {
            if (!_closed) {
                AppWindow.Show(); hidden = false;
                RestoreScreenshotMode();
                var errorFlyout = new Flyout { ShouldConstrainToRootBounds = false,
                    Content = new TextBlock { Text = $"Could not capture: {error.Message}", Width = 280, TextWrapping = TextWrapping.Wrap } };
                errorFlyout.ShowAt(ScreenshotButton);
            }
        } finally {
            if (!_closed) {
                if (hidden) { AppWindow.Show(); RestoreScreenshotMode(); }
                if (!_collapsed) { _mediaLibrary?.AppWindow.Show(); }
                ScreenshotButton.IsEnabled = true;
            }
            _capturing = false;
        }
    }
    private void OnScreenshotClosed(object? sender, object args)
    {
        if (!_screenshotPickerOpen) { RestoreScreenshotMode(); }
    }
    internal void SetScreenshotPickerOpen(bool open)
    {
        _screenshotPickerOpen = open;
        if (open) {
            if (_overlay?.Mode == OverlayMode.Draw) { _screenshotMode ??= OverlayMode.Draw; _overlay.SetMode(OverlayMode.ClickThrough); }
        } else { RestoreScreenshotMode(); }
    }
    private void RestoreScreenshotMode()
    {
        var mode = _screenshotMode;
        _screenshotMode = null;
        if (!_closed && mode.HasValue) { _overlay?.SetMode(mode.Value); }
    }
    private void OnSharingChanged(bool sharing)
    {
        if (_closed) { return; }
        ScreenshotButton.Background = new SolidColorBrush(sharing ? Color.FromArgb(255, 101, 94, 188) : Color.FromArgb(0, 0, 0, 0));
        ToolTipService.SetToolTip(ScreenshotButton, sharing ? "Screenshot · QR sharing is on (Ctrl+Alt+S: new)" : "Screenshot · save, copy or share with QR (Ctrl+Alt+S: new)");
        ToolTipService.SetToolTip(OpenToolbarButton, sharing ? "Open ScreenInk · screenshot sharing is on" : "Open ScreenInk");
    }
    private void OnBoundaryChanged(object sender, RoutedEventArgs args) => _overlay?.SetBoundaryVisible(((CheckBox)sender).IsChecked == true);
    private void OnThemeChanged(object sender, RoutedEventArgs args) => ToolbarRoot.RequestedTheme =
        ((CheckBox)sender).IsChecked == true ? ElementTheme.Light : ElementTheme.Dark;
    private void OnEraserSizeChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs args)
        => _overlay?.SetEraserDiameter((float)args.NewValue);
    private void OnPenSizeChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs args)
        => _overlay?.SetPenWidth((float)args.NewValue);
    private void OnColorChanged(ColorPicker sender, ColorChangedEventArgs args)
        => _overlay?.SetColor(new(args.NewColor.R, args.NewColor.G, args.NewColor.B));
    private void OnQuickColorClicked(object sender, RoutedEventArgs args)
    {
        var hex = (string)((Button)sender).Tag;
        InkColorPicker.Color = Color.FromArgb(255, byte.Parse(hex[..2], NumberStyles.HexNumber),
            byte.Parse(hex[2..4], NumberStyles.HexNumber), byte.Parse(hex[4..6], NumberStyles.HexNumber));
    }
    private void OnDeleteSelectedClicked(object sender, RoutedEventArgs args)
    {
        if (ViewModel.SelectedStroke is { } stroke) { _overlay?.DeleteStroke(stroke.Id); }
    }
    private void OnClearAllClicked(object sender, RoutedEventArgs args) => _overlay?.ClearAll();
    private void OnUndoClicked(object sender, RoutedEventArgs args) => _overlay?.Undo();
    private void OnRedoClicked(object sender, RoutedEventArgs args) => _overlay?.Redo();
    private void OnUndoShortcutInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args) { _overlay?.Undo(); args.Handled = true; }
    private void OnRedoShortcutInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args) { _overlay?.Redo(); args.Handled = true; }
    private void OnCloseClicked(object sender, RoutedEventArgs args) => Close();

    private void OnOverlayStatusChanged(object? sender, OverlayStatus status)
    {
        var previous = _lastMode;
        _lastMode = status.Mode;
        if (!_changingToolbar && !_capturing) {
            if (status.Mode == OverlayMode.Disabled && previous != OverlayMode.Disabled) {
                _resumeMode = previous;
                CollapseToolbar();
                _resumeMode = previous;
            } else if (status.Mode != OverlayMode.Disabled && _collapsed) { ExpandToolbar(false); }
        }
        Refresh(status);
    }
    private void Refresh(OverlayStatus status)
    {
        if (_overlay is null) { return; }
        ViewModel.UpdateOverlay(status, _overlay.HotkeyStatus, _overlay.GetStrokeSummaries());
        var selected = new SolidColorBrush(Color.FromArgb(255, 101, 94, 188));
        var transparent = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
        CursorButton.Background = status.Mode == OverlayMode.ClickThrough ? selected : transparent;
        PenButton.Background = status.Mode == OverlayMode.Draw && status.Tool == AnnotationTool.Pen ? selected : transparent;
        EraserButton.Background = status.Mode == OverlayMode.Draw && status.Tool == AnnotationTool.StrokeEraser ? selected : transparent;
        ShapesButton.Background = status.Mode == OverlayMode.Draw && status.Tool.IsShape() ? selected : transparent;
        LaserButton.Background = status.Mode == OverlayMode.Draw && status.Tool == AnnotationTool.Laser ? selected : transparent;
        ColorSwatch.Fill = new SolidColorBrush(Color.FromArgb(255, status.Color.Red, status.Color.Green, status.Color.Blue));
        PenSizeSlider.Value = status.PenWidth;
        EraserSizeSlider.Value = status.EraserDiameter;
    }

    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        _closed = true;
        CloseMedia();
        _screenshotPanel?.Dispose();
        AppWindow.Changed -= OnAppWindowChanged;
        if (ToolbarRoot.XamlRoot is { } root) { root.Changed -= OnXamlRootChanged; }
        if (_overlay is null) { return; }
        _overlay.StatusChanged -= OnOverlayStatusChanged;
        _overlay.FeatureShortcutInvoked -= OnFeatureShortcutInvoked;
        _overlay.Dispose();
        _overlay = null;
    }
}
