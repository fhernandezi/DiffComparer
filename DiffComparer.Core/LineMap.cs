using System;
using System.Collections.Generic;
using System.Text;

namespace DiffComparer.Core;

public record LineMap(
    int? LeftLine,
    int? RightLine,
    string LeftText,
    string RightText,
    LineStatus Status,
    int Similarity
)
{
    // Muestra el número o vacío si es línea fantasma
    public string LeftLineDisplay => LeftLine.HasValue
        ? (LeftLine.Value + 1).ToString() : "";

    public string RightLineDisplay => RightLine.HasValue
        ? (RightLine.Value + 1).ToString() : "";

    public List<WordSegment> LeftSegments { get; init; } = new();
    public List<WordSegment> RightSegments { get; init; } = new();
}