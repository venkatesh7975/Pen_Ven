using ScreenInk.Core.Models;

namespace ScreenInk.Core.History;

public sealed class InkHistory
{
    private readonly LinkedList<IInkCommand> _undo = [];
    private readonly LinkedList<IInkCommand> _redo = [];
    private readonly int _maximumCommands;
    private readonly long _sampleBudget;
    public long RetainedSampleCost { get; private set; }
    public int UndoCount => _undo.Count;
    public int RedoCount => _redo.Count;
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public string? UndoDescription => _undo.Last?.Value.Description;
    public string? RedoDescription => _redo.Last?.Value.Description;

    public InkHistory(int maximumCommands = 256, long sampleBudget = 250_000)
    {
        if (maximumCommands <= 0 || sampleBudget <= 0) { throw new ArgumentOutOfRangeException(nameof(maximumCommands)); }
        _maximumCommands = maximumCommands;
        _sampleBudget = sampleBudget;
    }

    // The caller has already applied this edit. Empty gestures must not call this method.
    public void RecordApplied(IInkCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        foreach (var discarded in _redo) { RetainedSampleCost -= discarded.SampleCost; }
        _redo.Clear();
        _undo.AddLast(command);
        RetainedSampleCost += command.SampleCost;
        // Always retain the newest edit, even if one very large clear exceeds the soft budget.
        while (_undo.Count > _maximumCommands || (_undo.Count > 1 && RetainedSampleCost > _sampleBudget)) {
            RetainedSampleCost -= _undo.First!.Value.SampleCost;
            _undo.RemoveFirst();
        }
    }

    public bool Undo(IList<InkStroke> strokes)
    {
        if (_undo.Last is not { } node) { return false; }
        node.Value.Undo(strokes);
        _undo.Remove(node);
        _redo.AddLast(node);
        return true;
    }

    public bool Redo(IList<InkStroke> strokes)
    {
        if (_redo.Last is not { } node) { return false; }
        node.Value.Redo(strokes);
        _redo.Remove(node);
        _undo.AddLast(node);
        return true;
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
        RetainedSampleCost = 0;
    }
}
