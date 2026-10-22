using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using DiffComparer.Core;
using DiffComparer.UI.Models;

namespace DiffComparer.UI.Services;

public static class ComparisonExporter
{
    public static string Html(ComparisonSnapshot snapshot, string leftName, string rightName,
        IReadOnlySet<int> reviewed, CancellationToken ct = default)
    {
        static string E(string value) => WebUtility.HtmlEncode(value);
        var html = new StringBuilder("<!doctype html><html lang=\"es\"><meta charset=\"utf-8\"><title>Informe de comparación</title><style>body{font:14px system-ui;margin:24px;color:#222}table{border-collapse:collapse;width:100%}th,td{border:1px solid #ccc;padding:4px 8px;vertical-align:top}pre{margin:0;white-space:pre-wrap;overflow-wrap:anywhere}td.num{color:#666;width:4em}.Added{background:#e4f7e7}.Deleted{background:#ffe8e8}.Modified{background:#fff5d2}.moved{background:#e1efff}mark{background:#ffc266}th{background:#eee}.reviewed{opacity:.7}</style><h1>Informe de comparación</h1>");
        html.Append($"<p>{snapshot.Added} agregadas · {snapshot.Deleted} eliminadas · {snapshot.Modified} modificadas · {snapshot.MovedBlocks} bloques movidos · {reviewed.Count}/{snapshot.Blocks.Length} revisados</p>");
        string endings = snapshot.Canonical?.Options.CompareLineEndings == true
            ? "Se comparan los terminadores CRLF/LF/CR." : "Se ignora el estilo CRLF/LF/CR; se detecta la presencia del salto final.";
        html.Append($"<p>Regla: {(snapshot.IgnoreWhitespace ? "ignorar diferencias de espacios" : "comparación exacta de contenido")}. {endings}</p>");
        if (snapshot.CoarseAlignment) html.Append("<p>Se simplificó la alineación de bloques grandes para limitar el uso de memoria.</p>");
        html.Append($"<table><thead><tr><th>Línea</th><th>{E(leftName)}</th><th>Línea</th><th>{E(rightName)}</th><th>Estado / revisión</th></tr></thead><tbody>");
        for (int i = 0; i < snapshot.Lines.Length; i++)
        {
            ct.ThrowIfCancellationRequested();
            var line = snapshot.Lines[i];
            bool done = reviewed.Contains(snapshot.BlockForLine[i]);
            string Render(string text, List<WordSegment> segments) => segments.Count == 0 || line.Status != LineStatus.Modified ? E(text)
                : string.Concat(segments.Select(s => s.IsChanged ? $"<mark>{E(s.Text)}</mark>" : E(s.Text)));
            string status = line.Status switch { LineStatus.Added => "Agregada", LineStatus.Deleted => "Eliminada", LineStatus.Modified => "Modificada", _ => "Igual" };
            if (line.MoveId.HasValue) status += $" · Movimiento {line.MoveId} ↔ línea {line.MovedPartnerLine + 1}";
            if (line.HasTerminatorChange) status += " · ¶ Cambio de terminador o salto final";
            if (done) status += " · Revisado";
            html.Append($"<tr class=\"{(line.MoveId.HasValue ? "moved" : line.Status.ToString())}{(done ? " reviewed" : "")}\"><td class=\"num\">{line.LeftLineDisplay}</td><td><pre>{Render(line.LeftText, line.LeftSegments)}</pre></td><td class=\"num\">{line.RightLineDisplay}</td><td><pre>{Render(line.RightText, line.RightSegments)}</pre></td><td>{E(status)}</td></tr>");
        }
        return html.Append("</tbody></table></html>").ToString();
    }

    // Export all source lines in one valid unified hunk, independent of UI filters and ignore rules.
    public static string Patch(string leftText, string rightText, string leftName, string rightName, CancellationToken ct = default)
    {
        if (leftText == rightText) return "";
        static (string[] Lines, bool Newline) Split(string value)
        {
            if (value.Length == 0) return (Array.Empty<string>(), false);
            value = value.Replace("\r\n", "\n").Replace('\r', '\n');
            bool end = value.EndsWith('\n');
            return ((end ? value[..^1] : value).Split('\n'), end);
        }
        var left = Split(leftText);
        var right = Split(rightText);
        static string Name(string value) => System.IO.Path.GetFileName(value).Replace('\n', '_').Replace('\r', '_').Replace('\t', '_');
        var lines = new DiffEngine(ct) { IgnoreWhitespace = false, IncludeCharacterDetails = false }.Compare(left.Lines, right.Lines);
        if (left.Lines.SequenceEqual(right.Lines) && left.Newline == right.Newline) return "";
        var patch = new StringBuilder($"--- a/{Name(leftName)}\n+++ b/{Name(rightName)}\n@@ -{(left.Lines.Length == 0 ? 0 : 1)},{left.Lines.Length} +{(right.Lines.Length == 0 ? 0 : 1)},{right.Lines.Length} @@\n");
        void Emit(char prefix, string text, bool noNewline)
        {
            patch.Append(prefix).Append(text).Append('\n');
            if (noNewline) patch.Append("\\ No newline at end of file\n");
        }
        foreach (var line in lines)
        {
            ct.ThrowIfCancellationRequested();
            bool leftEnd = line.LeftLine == left.Lines.Length - 1 && !left.Newline;
            bool rightEnd = line.RightLine == right.Lines.Length - 1 && !right.Newline;
            if (line.Status == LineStatus.Equal && leftEnd == rightEnd) Emit(' ', line.LeftText, leftEnd);
            else
            {
                if (line.LeftLine.HasValue) Emit('-', line.LeftText, leftEnd);
                if (line.RightLine.HasValue) Emit('+', line.RightText, rightEnd);
            }
        }
        return patch.ToString();
    }
}
