using System.Globalization;
using System.Text;

namespace DiffComparer.Core;

/// <summary>Exact intrablock differences on extended graphemes; offsets remain UTF-16 for editors.</summary>
public static class TextDiffer
{
    public static IReadOnlyList<ExactEdit> Compute(string left, string right, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var identifiers = new Dictionary<string, int>(StringComparer.Ordinal);
        var a = Tokenize(left, identifiers, ct);
        var b = Tokenize(right, identifiers, ct);
        return SequenceDiffer.Compute(a.Ids, b.Ids, DiffStrategy.Minimal, ct).Select(edit =>
            new ExactEdit(new(a.Offsets[edit.LeftStart], a.Offsets[edit.LeftEnd] - a.Offsets[edit.LeftStart]),
                new(b.Offsets[edit.RightStart], b.Offsets[edit.RightEnd] - b.Offsets[edit.RightStart]))).ToArray();
    }

    private static (int[] Ids, int[] Offsets) Tokenize(string text, Dictionary<string, int> identifiers, CancellationToken ct)
    {
        int[] starts = StringInfo.ParseCombiningCharacters(text);
        int[] offsets = new int[starts.Length + 1], ids = new int[starts.Length];
        starts.CopyTo(offsets, 0);
        offsets[^1] = text.Length;
        for (int i = 0; i < starts.Length; i++)
        {
            if ((i & 1023) == 0) ct.ThrowIfCancellationRequested();
            int length = offsets[i + 1] - offsets[i];
            if (length == 1 && text[offsets[i]] < 128) { ids[i] = text[offsets[i]] + 1; continue; }
            string token = text.Substring(offsets[i], length);
            if (!identifiers.TryGetValue(token, out int id)) identifiers[token] = id = identifiers.Count + 256;
            ids[i] = id;
        }
        return (ids, offsets);
    }

    public static List<WordSegment> Segments(string text, int documentOffset, IReadOnlyList<TextRange> changes)
    {
        var result = new List<WordSegment>();
        int position = 0;
        int low = 0, high = changes.Count;
        while (low < high)
        {
            int middle = (low + high) / 2;
            if (changes[middle].End <= documentOffset) low = middle + 1; else high = middle;
        }
        for (int i = low; i < changes.Count && changes[i].Offset < documentOffset + text.Length; i++)
        {
            var change = changes[i];
            int start = Math.Clamp(change.Offset - documentOffset, 0, text.Length);
            int end = Math.Clamp(change.End - documentOffset, 0, text.Length);
            if (end <= position || end <= start) continue;
            start = Math.Max(position, start);
            if (start > position) result.Add(new(text[position..start], false));
            result.Add(new(text[start..end], true));
            position = end;
        }
        if (position < text.Length) result.Add(new(text[position..], false));
        return result;
    }

    internal static string Normalize(string text)
    {
        if (text.Length == 0) return text;
        var builder = new StringBuilder(text.Length);
        bool gap = false;
        foreach (char c in text)
        {
            if (c is ' ' or '\t') { gap = builder.Length > 0; continue; }
            if (gap) builder.Append(' ');
            builder.Append(c); gap = false;
        }
        return builder.ToString();
    }
}
