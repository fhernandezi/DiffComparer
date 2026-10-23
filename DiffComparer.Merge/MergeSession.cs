namespace DiffComparer.Merge;

/// <summary>Explicit conflict decisions, undo/redo and result assembly; independent of comparison sessions.</summary>
public sealed class MergeSession(MergePlan plan)
{
    private sealed record Resolution(MergeChoice Choice, string ManualText);
    private readonly Dictionary<int, Resolution> _choices = new();
    private readonly Stack<Dictionary<int, Resolution>> _undo = new(), _redo = new();
    public MergePlan Plan { get; } = plan;
    public int UnresolvedCount => Plan.Blocks.Count(b => b.IsConflict && ChoiceFor(b.Id) == MergeChoice.Unresolved);
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public MergeChoice ChoiceFor(int blockId) => _choices.TryGetValue(blockId, out var value) ? value.Choice : MergeChoice.Unresolved;

    public void Resolve(int blockId, MergeChoice choice, string manualText = "")
    {
        if (!Enum.IsDefined(choice)) throw new ArgumentOutOfRangeException(nameof(choice));
        if (!Plan.Blocks.Any(b => b.Id == blockId && b.IsConflict)) throw new ArgumentException("Unknown conflict.", nameof(blockId));
        var next = new Resolution(choice, manualText);
        if (_choices.GetValueOrDefault(blockId) == next) return;
        _undo.Push(new(_choices));
        _redo.Clear();
        _choices[blockId] = next;
    }

    public void Undo() => Restore(_undo, _redo);
    public void Redo() => Restore(_redo, _undo);
    private void Restore(Stack<Dictionary<int, Resolution>> from, Stack<Dictionary<int, Resolution>> to)
    {
        if (!from.TryPop(out var state)) return;
        to.Push(new(_choices));
        _choices.Clear();
        foreach (var pair in state) _choices.Add(pair.Key, pair.Value);
    }

    public string PreviewText => string.Concat(Plan.Blocks.Select(b =>
    {
        if (!b.IsConflict) return b.AutomaticText;
        var choice = _choices.GetValueOrDefault(b.Id);
        return choice?.Choice switch
        {
            MergeChoice.Left => b.LeftText,
            MergeChoice.Right => b.RightText,
            MergeChoice.BothLeftFirst => b.LeftText + b.RightText,
            MergeChoice.BothRightFirst => b.RightText + b.LeftText,
            MergeChoice.Manual => choice.ManualText,
            _ => b.BaseText
        };
    }));

    public string Complete() => UnresolvedCount == 0 ? PreviewText
        : throw new InvalidOperationException("Resolve every conflict before completing the merge.");
}
