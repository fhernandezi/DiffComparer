using System.Text;
using DiffComparer.Core;

namespace DiffComparer.Merge;

public sealed record SectionRequest(Guid SessionId, int SectionId, bool Incoming, bool Include);

/// <summary>Selective merge: unchanged text is retained; each difference starts with the original side.</summary>
public sealed class SectionMergeSession
{
    public Guid Id { get; } = Guid.NewGuid();
    public ComparisonResult Comparison { get; }
    private readonly Dictionary<int, MergeChoice> _choices = new();
    private readonly Stack<Dictionary<int, MergeChoice>> _undo = new(), _redo = new();
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public MergeChoice ChoiceFor(int id) => _choices.GetValueOrDefault(id, MergeChoice.Left);
    public SectionMergeSession(ComparisonResult comparison) => Comparison = comparison;

    public void SetIncluded(int id, bool incoming, bool include)
    {
        var current = ChoiceFor(id);
        bool left = current is MergeChoice.Left or MergeChoice.BothLeftFirst or MergeChoice.BothRightFirst;
        bool right = current is MergeChoice.Right or MergeChoice.BothLeftFirst or MergeChoice.BothRightFirst;
        if (incoming) right = include; else left = include;
        Choose(id, (left, right) switch
        {
            (true, true) => current == MergeChoice.BothRightFirst ? MergeChoice.BothRightFirst : MergeChoice.BothLeftFirst,
            (true, false) => MergeChoice.Left,
            (false, true) => MergeChoice.Right,
            _ => MergeChoice.Unresolved
        });
    }

    public void Choose(int id, MergeChoice choice)
    {
        if (!Comparison.Hunks.Any(h => h.Id == id)) throw new ArgumentOutOfRangeException(nameof(id));
        if (choice is not (MergeChoice.Left or MergeChoice.Right or MergeChoice.BothLeftFirst or MergeChoice.BothRightFirst or MergeChoice.Unresolved))
            throw new ArgumentOutOfRangeException(nameof(choice));
        if (ChoiceFor(id) == choice) return;
        _undo.Push(new(_choices)); _redo.Clear(); _choices[id] = choice;
    }

    public string SectionText(DiffHunk hunk) => ChoiceFor(hunk.Id) switch
    {
        MergeChoice.Right => Comparison.Right.Slice(hunk.RightText),
        MergeChoice.BothLeftFirst => Combine(Comparison.Left.Slice(hunk.LeftText), Comparison.Right.Slice(hunk.RightText)),
        MergeChoice.BothRightFirst => Combine(Comparison.Right.Slice(hunk.RightText), Comparison.Left.Slice(hunk.LeftText)),
        MergeChoice.Unresolved => "",
        _ => Comparison.Left.Slice(hunk.LeftText)
    };

    private string Combine(string first, string second)
    {
        if (first.Length == 0 || second.Length == 0 || first[^1] is '\r' or '\n' || second[0] is '\r' or '\n')
            return first + second;
        string ending = Comparison.Left.Lines.FirstOrDefault(l => l.Terminator.Length > 0)?.Terminator
            ?? Comparison.Right.Lines.FirstOrDefault(l => l.Terminator.Length > 0)?.Terminator ?? "\n";
        return first + ending + second;
    }

    public string ResultText
    {
        get
        {
            var result = new StringBuilder();
            int offset = 0;
            foreach (var hunk in Comparison.Hunks)
            {
                result.Append(Comparison.Left.Text.AsSpan(offset, hunk.LeftText.Offset - offset));
                result.Append(SectionText(hunk));
                offset = hunk.LeftText.End;
            }
            return result.Append(Comparison.Left.Text.AsSpan(offset)).ToString();
        }
    }

    public int ResultOffset(int id)
    {
        int delta = 0;
        foreach (var hunk in Comparison.Hunks)
        {
            if (hunk.Id == id) return hunk.LeftText.Offset + delta;
            delta += SectionText(hunk).Length - hunk.LeftText.Length;
        }
        throw new ArgumentOutOfRangeException(nameof(id));
    }

    // A decision survives editing only when both texts and their neighboring lines match uniquely.
    private sealed record Identity(string Left, string Right, string LeftBefore, string LeftAfter, string RightBefore, string RightAfter);
    private Identity Key(DiffHunk h)
    {
        static string Before(TextSnapshot s, LineRange r) => r.Start > 0 ? s.Lines[r.Start - 1].Content + s.Lines[r.Start - 1].Terminator : "<BOF>";
        static string After(TextSnapshot s, LineRange r) => r.End < s.Lines.Length ? s.Lines[r.End].Content + s.Lines[r.End].Terminator : "<EOF>";
        return new(Comparison.Left.Slice(h.LeftText), Comparison.Right.Slice(h.RightText),
            Before(Comparison.Left, h.LeftLines), After(Comparison.Left, h.LeftLines),
            Before(Comparison.Right, h.RightLines), After(Comparison.Right, h.RightLines));
    }
    public void CarryDecisionsFrom(SectionMergeSession? previous)
    {
        if (previous is null) return;
        var old = previous.Comparison.Hunks.GroupBy(previous.Key).Where(g => g.Count() == 1)
            .ToDictionary(g => g.Key, g => previous.ChoiceFor(g.Single().Id));
        foreach (var group in Comparison.Hunks.GroupBy(Key).Where(g => g.Count() == 1))
            if (old.TryGetValue(group.Key, out var choice)) _choices[group.Single().Id] = choice;
    }
    public void Undo() => Restore(_undo, _redo);
    public void Redo() => Restore(_redo, _undo);
    private void Restore(Stack<Dictionary<int, MergeChoice>> from, Stack<Dictionary<int, MergeChoice>> to)
    {
        if (!from.TryPop(out var state)) return;
        to.Push(new(_choices)); _choices.Clear();
        foreach (var item in state) _choices.Add(item.Key, item.Value);
    }
}
