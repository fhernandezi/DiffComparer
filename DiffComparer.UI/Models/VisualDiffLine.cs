namespace DiffComparer.UI.Models;

using DiffComparer.Core;

public class VisualDiffLine
{
    public int VisualIndex { get; set; }

    public int? LeftRealLine { get; set; }
    public int? RightRealLine { get; set; }

    public string LeftText { get; set; } = "";
    public string RightText { get; set; } = "";

    public LineStatus Status { get; set; }

    public bool IsLeftGhost => LeftRealLine is null;
    public bool IsRightGhost => RightRealLine is null;
}