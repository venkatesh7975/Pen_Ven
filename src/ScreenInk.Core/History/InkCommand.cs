using ScreenInk.Core.Models;

namespace ScreenInk.Core.History;

public interface IInkCommand
{
    string Description { get; }
    long SampleCost { get; }
    void Undo(IList<InkStroke> strokes);
    void Redo(IList<InkStroke> strokes);
}

public sealed record IndexedStroke(int Index, InkStroke Stroke);

// Commands retain stroke objects and list positions, never copies of every point on the canvas.
public sealed class StrokeEditCommand : IInkCommand
{
    private readonly IndexedStroke[] _entries;
    private readonly bool _addition;
    public string Description { get; }
    public long SampleCost { get; }

    private StrokeEditCommand(string description, IEnumerable<IndexedStroke> entries, bool addition)
    {
        Description = description;
        _entries = entries.OrderBy(entry => entry.Index).ToArray();
        if (_entries.Length == 0 || _entries.Any(entry => entry.Index < 0 || entry.Stroke is null) ||
            _entries.Select(entry => entry.Index).Distinct().Count() != _entries.Length) {
            throw new ArgumentException("An edit needs distinct nonnegative stroke positions.", nameof(entries));
        }
        _addition = addition;
        SampleCost = _entries.Sum(entry => (long)entry.Stroke.Samples.Count);
    }

    public static StrokeEditCommand Added(int index, InkStroke stroke) =>
        new("Draw stroke", [new IndexedStroke(index, stroke)], true);
    public static StrokeEditCommand Removed(string description, IEnumerable<IndexedStroke> entries) =>
        new(description, entries, false);

    public void Undo(IList<InkStroke> strokes) { if (_addition) { Remove(strokes); } else { Insert(strokes); } }
    public void Redo(IList<InkStroke> strokes) { if (_addition) { Insert(strokes); } else { Remove(strokes); } }

    private void Insert(IList<InkStroke> strokes)
    {
        for (var i = 0; i < _entries.Length; i++) {
            if (_entries[i].Index > strokes.Count + i) { throw new InvalidOperationException("Ink history no longer matches the canvas."); }
        }
        foreach (var entry in _entries) { strokes.Insert(entry.Index, entry.Stroke); }
    }

    private void Remove(IList<InkStroke> strokes)
    {
        foreach (var entry in _entries) {
            if (entry.Index >= strokes.Count || !ReferenceEquals(strokes[entry.Index], entry.Stroke)) {
                throw new InvalidOperationException("Ink history no longer matches the canvas.");
            }
        }
        for (var i = _entries.Length - 1; i >= 0; i--) { strokes.RemoveAt(_entries[i].Index); }
    }
}

// Each eraser sample's indices refer to that sample's canvas. Reverse batch order on undo.
public sealed class CompositeInkCommand : IInkCommand
{
    private readonly IInkCommand[] _commands;
    public string Description { get; }
    public long SampleCost { get; }

    public CompositeInkCommand(string description, IEnumerable<IInkCommand> commands)
    {
        Description = description;
        _commands = commands.ToArray();
        if (_commands.Length == 0) { throw new ArgumentException("A group must contain an edit.", nameof(commands)); }
        SampleCost = _commands.Sum(command => command.SampleCost);
    }
    public void Undo(IList<InkStroke> strokes)
    {
        for (var i = _commands.Length - 1; i >= 0; i--) { _commands[i].Undo(strokes); }
    }
    public void Redo(IList<InkStroke> strokes)
    {
        foreach (var command in _commands) { command.Redo(strokes); }
    }
}
