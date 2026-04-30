using System;
using System.Collections.Generic;
using System.Text;
namespace DiffComparer.UI.Models;

public class VisualDiffDocument
{
    public string LeftText { get; set; } = "";
    public string RightText { get; set; } = "";

    public List<VisualDiffLine> Lines { get; set; } = new();
}