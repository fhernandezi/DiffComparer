using DiffComparer.Core;
using DiffComparer.UI.Models;
using System.Collections.Generic;
using System.Text;

namespace DiffComparer.UI.Services;

public static class VisualDiffBuilder
{
    public static VisualDiffDocument Build(IReadOnlyList<LineMap> lines)
    {
        var visualLines = new List<VisualDiffLine>();

        var leftBuilder = new StringBuilder();
        var rightBuilder = new StringBuilder();

        for (int i = 0; i < lines.Count; i++)
        {
            var line = lines[i];

            visualLines.Add(new VisualDiffLine
            {
                VisualIndex = i,

                LeftRealLine = line.LeftLine,
                RightRealLine = line.RightLine,

                LeftText = line.LeftText,
                RightText = line.RightText,

                Status = line.Status
            });

            leftBuilder.Append(line.LeftLine.HasValue ? line.LeftText : "");
            rightBuilder.Append(line.RightLine.HasValue ? line.RightText : "");

            if (i < lines.Count - 1)
            {
                leftBuilder.AppendLine();
                rightBuilder.AppendLine();
            }
        }

        return new VisualDiffDocument
        {
            LeftText = leftBuilder.ToString(),
            RightText = rightBuilder.ToString(),
            Lines = visualLines
        };
    }
}