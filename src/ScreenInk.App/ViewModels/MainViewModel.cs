using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Collections.ObjectModel;
using ScreenInk.Core;
using ScreenInk.Core.Models;
using ScreenInk.Overlay;

namespace ScreenInk.App.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private int _confirmationCount;
    private OverlayStatus? _overlayStatus;
    private string _hotkeyStatus = "Initializing recovery shortcuts…";
    private string? _initializationError;
    private StrokeChoice? _selectedStroke;

    public ObservableCollection<StrokeChoice> StrokeChoices { get; } = [];
    public StrokeChoice? SelectedStroke {
        get => _selectedStroke;
        set {
            if (ReferenceEquals(_selectedStroke, value)) { return; }
            _selectedStroke = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanDelete));
        }
    }
    public bool CanDelete => SelectedStroke is not null;
    public bool HasInk => StrokeChoices.Count > 0;
    public bool CanUndo => _overlayStatus?.CanUndo == true;
    public bool CanRedo => _overlayStatus?.CanRedo == true;
    public string UndoTooltip => $"Undo: {_overlayStatus?.UndoDescription ?? "nothing yet"} (Ctrl+Z in draw mode)";
    public string RedoTooltip => $"Redo: {_overlayStatus?.RedoDescription ?? "nothing yet"} (Ctrl+Y in draw mode)";
    public string HistoryShortcuts => _overlayStatus?.HistoryShortcuts ?? "Initializing ink history…";
    public string ToolStatus => _overlayStatus?.Tool == AnnotationTool.StrokeEraser
        ? "Stroke eraser · removes whole annotations" : _overlayStatus?.Tool == AnnotationTool.Laser
            ? "Laser · press and drag to point; lift to hide" : $"Tool: {_overlayStatus?.Tool ?? AnnotationTool.Pen}";
    public string ColorLabel => $"Ink color: {_overlayStatus?.Color.Hex ?? InkColor.Default.Hex}";
    public string PenSizeLabel => $"Pen / shape width: {_overlayStatus?.PenWidth ?? 4:0} px";
    public string EraserSizeLabel => $"Eraser diameter: {_overlayStatus?.EraserDiameter ?? 24:0} physical px";

    public string ApplicationName => ApplicationInfo.Name;
    public string DevelopmentStage => ApplicationInfo.Stage;
    public string LaunchStatus => _confirmationCount == 0
        ? "Ready for your launch check."
        : $"Launch check passed · {_confirmationCount} click{(_confirmationCount == 1 ? string.Empty : "s")}";

    public string OverlayModeLabel => _overlayStatus?.Mode switch {
        OverlayMode.Draw => "Draw mode · desktop input is intercepted",
        OverlayMode.ClickThrough => "Click-through · underlying applications receive input",
        _ => "Overlay disabled"
    };
    public string OverlayDetails => _overlayStatus is null ? "Ready to enable the overlay on this monitor."
        : $"Monitor: {_overlayStatus.Bounds.Width} × {_overlayStatus.Bounds.Height} px at ({_overlayStatus.Bounds.Left}, {_overlayStatus.Bounds.Top}) · Strokes: {_overlayStatus.StrokeCount}";
    public string PointerStatus => _overlayStatus?.PointerStatus ?? "No overlay input yet.";
    public string PenDetails => _overlayStatus?.PenDetails ?? "Pen: waiting for Windows pointer input.";
    public string HotkeyStatus => _hotkeyStatus;
    public string ErrorStatus => _initializationError ?? _overlayStatus?.Error ?? string.Empty;

    public void UpdateOverlay(OverlayStatus status, string hotkeyStatus, IReadOnlyList<StrokeSummary> strokes)
    {
        var previous = _overlayStatus;
        var previousHotkeys = _hotkeyStatus;
        _overlayStatus = status;
        _hotkeyStatus = hotkeyStatus;
        // Avoid resetting unrelated live-region text on each slider or tool change.
        if (previous?.Mode != status.Mode) { OnPropertyChanged(nameof(OverlayModeLabel)); }
        if (previous?.Bounds != status.Bounds || previous?.StrokeCount != status.StrokeCount) { OnPropertyChanged(nameof(OverlayDetails)); }
        if (previous?.PointerStatus != status.PointerStatus) { OnPropertyChanged(nameof(PointerStatus)); }
        if (previous?.PenDetails != status.PenDetails) { OnPropertyChanged(nameof(PenDetails)); }
        if (previousHotkeys != hotkeyStatus) { OnPropertyChanged(nameof(HotkeyStatus)); }
        if (previous is null || previous.Error != status.Error) { OnPropertyChanged(nameof(ErrorStatus)); }
        if (previous?.Tool != status.Tool) { OnPropertyChanged(nameof(ToolStatus)); }
        if (previous?.Color != status.Color) { OnPropertyChanged(nameof(ColorLabel)); }
        if (previous?.PenWidth != status.PenWidth) { OnPropertyChanged(nameof(PenSizeLabel)); }
        if (previous?.EraserDiameter != status.EraserDiameter) { OnPropertyChanged(nameof(EraserSizeLabel)); }
        if (previous?.CanUndo != status.CanUndo) { OnPropertyChanged(nameof(CanUndo)); }
        if (previous?.CanRedo != status.CanRedo) { OnPropertyChanged(nameof(CanRedo)); }
        if (previous is null || previous.UndoDescription != status.UndoDescription) { OnPropertyChanged(nameof(UndoTooltip)); }
        if (previous is null || previous.RedoDescription != status.RedoDescription) { OnPropertyChanged(nameof(RedoTooltip)); }
        if (previous?.HistoryShortcuts != status.HistoryShortcuts) { OnPropertyChanged(nameof(HistoryShortcuts)); }
        var choices = strokes.Select(stroke => new StrokeChoice(stroke.Id,
            $"Stroke {stroke.Number} · {stroke.InputKind} · {stroke.SampleCount} samples")).ToArray();
        if (!StrokeChoices.SequenceEqual(choices)) {
            var selectedId = SelectedStroke?.Id;
            StrokeChoices.Clear();
            foreach (var choice in choices) { StrokeChoices.Add(choice); }
            SelectedStroke = StrokeChoices.FirstOrDefault(stroke => stroke.Id == selectedId) ?? StrokeChoices.LastOrDefault();
            OnPropertyChanged(nameof(HasInk));
        }
    }

    public void ReportInitializationError(Exception error)
    {
        _initializationError = $"Overlay unavailable: {error.Message}";
        OnPropertyChanged(nameof(ErrorStatus));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public void ConfirmLaunch()
    {
        _confirmationCount++;
        OnPropertyChanged(nameof(LaunchStatus));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed record StrokeChoice(Guid Id, string Label);
