namespace ScreenInk.Overlay;

public enum FeatureAction
{
    Pen, Eraser, Laser, Line, Arrow, Rectangle, Ellipse, Cursor,
    Screenshot, Media, Color, Size, Help, ClearAll, Undo, Redo,
    IncreaseSize, DecreaseSize, DeleteSelected
}

public sealed record FeatureShortcut(FeatureAction Action, uint VirtualKey, string Key, string Label)
{
    public int Id => 100 + (int)Action;
    public string Gesture => $"Ctrl+Alt+{Key}";
}

public static class FeatureShortcuts
{
    // MOD_CONTROL | MOD_ALT | MOD_NOREPEAT: one action per key press.
    internal const uint Modifiers = 0x4003;
    public static IReadOnlyList<FeatureShortcut> All { get; } = Array.AsReadOnly<FeatureShortcut>([
        new(FeatureAction.Pen, 0x50, "P", "Pen"),
        new(FeatureAction.Eraser, 0x45, "E", "Stroke eraser"),
        new(FeatureAction.Laser, 0x4C, "L", "Laser pointer"),
        new(FeatureAction.Line, 0x31, "1", "Line"),
        new(FeatureAction.Arrow, 0x32, "2", "Arrow"),
        new(FeatureAction.Rectangle, 0x33, "3", "Rectangle"),
        new(FeatureAction.Ellipse, 0x34, "4", "Ellipse"),
        new(FeatureAction.Cursor, 0x43, "C", "Cursor mode"),
        new(FeatureAction.Screenshot, 0x53, "S", "New screenshot"),
        new(FeatureAction.Media, 0x4D, "M", "Media and definitions"),
        new(FeatureAction.Color, 0x4B, "K", "Ink color"),
        new(FeatureAction.Size, 0x57, "W", "Tool size controls"),
        new(FeatureAction.IncreaseSize, 0x26, "Up", "Increase current tool size"),
        new(FeatureAction.DecreaseSize, 0x28, "Down", "Decrease current tool size"),
        new(FeatureAction.ClearAll, 0x2E, "Delete", "Clear all ink (undoable)"),
        new(FeatureAction.DeleteSelected, 0x44, "D", "Delete selected annotation"),
        new(FeatureAction.Undo, 0x5A, "Z", "Undo ink in any mode"),
        new(FeatureAction.Redo, 0x59, "Y", "Redo ink in any mode"),
        new(FeatureAction.Help, 0x48, "H", "Help and options")
    ]);

    public static string HelpText => string.Join("\n", All.Select(shortcut => $"{shortcut.Gesture} · {shortcut.Label}"));
}
