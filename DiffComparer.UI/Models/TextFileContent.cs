namespace DiffComparer.UI.Models;

public sealed record TextFileContent(string Text, string Name, string Path, string EncodingName, string LineEndings, bool HasBom = false)
{
    public static string DetectLineEndings(string text)
    {
        int crlf = 0, lf = 0, cr = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\r')
            {
                if (i + 1 < text.Length && text[i + 1] == '\n') { crlf++; i++; }
                else cr++;
            }
            else if (text[i] == '\n') lf++;
        }
        int kinds = (crlf > 0 ? 1 : 0) + (lf > 0 ? 1 : 0) + (cr > 0 ? 1 : 0);
        return kinds > 1 ? "Mixtos" : crlf > 0 ? "CRLF" : lf > 0 ? "LF" : cr > 0 ? "CR" : "Sin saltos";
    }
}
