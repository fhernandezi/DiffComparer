using System;
using System.IO;
using System.Text.Json;

namespace DiffComparer.UI.Editing;

public sealed class EditorTypographyStore(string path)
{
    private sealed record Preferences(string FontName, double FontSize, double Zoom, double LineSpacing, bool WordWrap);
    public EditorTypography Load()
    {
        try
        {
            var value = JsonSerializer.Deserialize<Preferences>(File.ReadAllText(path));
            if (value is not null) return new EditorTypography
            { FontName = value.FontName, FontSize = value.FontSize, Zoom = value.Zoom, LineSpacing = value.LineSpacing, WordWrap = value.WordWrap };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException) { }
        return new();
    }
    public bool Save(EditorTypography value)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            string temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(new Preferences(value.FontName, value.FontSize,
                value.Zoom, value.LineSpacing, value.WordWrap)));
            File.Move(temporary, path, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { return false; }
    }
}
