using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using DiffComparer.UI.Interfaces;
using DiffComparer.UI.Models;

namespace DiffComparer.UI.Services;

public class FilePickerService : IFilePickerService, IReportExportService, ITextFileSaveService
{
    private readonly Window _window;
    public FilePickerService(Window window) => _window = window;

    public async Task<string?> OpenTextFileAsync() => (await OpenFileAsync())?.Text;

    public async Task<TextFileContent?> OpenFileAsync(string encodingName = "Automático")
    {
        var files = await _window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Abrir archivo de texto", AllowMultiple = false
        });
        if (files.Count == 0) return null;
        var file = files[0];
        await using var stream = await file.OpenReadAsync();
        return await Task.Run(async () =>
        {
            var loaded = await TextFileReader.ReadWithMetadataAsync(stream, encodingName);
            var (text, encoding) = (loaded.Text, loaded.Encoding);
            return new TextFileContent(text, file.Name, file.Path.IsFile ? file.Path.LocalPath : file.Path.ToString(),
                encoding, TextFileContent.DetectLineEndings(text), loaded.HasBom);
        });
    }

    public async Task<TextFileContent?> SaveFileAsAsync(TextFileContent content)
    {
        // Encode before selecting/truncating the target: an unrepresentable character cannot destroy a file.
        var bytes = await Task.Run(() => TextFileEncoding.Encode(content));
        var file = await _window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Guardar texto como", SuggestedFileName = content.Name,
            FileTypeChoices = new[] { new FilePickerFileType("Archivo de texto") { Patterns = new[] { "*" } } }
        });
        if (file is null) return null;
        await using var stream = await file.OpenWriteAsync();
        if (stream.CanSeek) stream.SetLength(0);
        await stream.WriteAsync(bytes);
        return content with { Name = file.Name, Path = file.Path.IsFile ? file.Path.LocalPath : file.Path.ToString() };
    }

    public async Task<bool> SaveTextAsync(string suggestedName, string content, bool html)
    {
        var file = await _window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = html ? "Exportar informe HTML" : "Exportar parche unified diff",
            SuggestedFileName = suggestedName,
            DefaultExtension = html ? "html" : "patch",
            FileTypeChoices = new[] { new FilePickerFileType(html ? "Informe HTML" : "Parche")
                { Patterns = new[] { html ? "*.html" : "*.patch" } } }
        });
        if (file is null) return false;
        await using var stream = await file.OpenWriteAsync();
        if (stream.CanSeek) stream.SetLength(0);
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        await writer.WriteAsync(content);
        return true;
    }
}

public static class TextFileEncoding
{
    public static byte[] Encode(TextFileContent content)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Encoding encoding = content.EncodingName.ToLowerInvariant() switch
        {
            "utf-8" => new UTF8Encoding(content.HasBom, true),
            "utf-16" => new UnicodeEncoding(false, content.HasBom, true),
            "utf-16be" => new UnicodeEncoding(true, content.HasBom, true),
            "utf-32" => new UTF32Encoding(false, content.HasBom, true),
            "utf-32be" => new UTF32Encoding(true, content.HasBom, true),
            _ => Encoding.GetEncoding(content.EncodingName, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback)
        };
        byte[] body = encoding.GetBytes(content.Text);
        byte[] preamble = content.HasBom ? encoding.GetPreamble() : Array.Empty<byte>();
        var result = new byte[preamble.Length + body.Length];
        preamble.CopyTo(result, 0); body.CopyTo(result, preamble.Length);
        return result;
    }
}

public static class TextFileReader
{
    public static async Task<(string Text, string Encoding)> ReadAsync(Stream stream, string encodingName)
    {
        var result = await ReadWithMetadataAsync(stream, encodingName).ConfigureAwait(false);
        return (result.Text, result.Encoding);
    }
    public static async Task<(string Text, string Encoding, bool HasBom)> ReadWithMetadataAsync(Stream stream, string encodingName)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Encoding encoding = encodingName switch
        {
            "Windows-1252" => Encoding.GetEncoding(1252, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback),
            "UTF-16 LE" => new UnicodeEncoding(false, true, true),
            "UTF-16 BE" => new UnicodeEncoding(true, true, true),
            _ => new UTF8Encoding(false, true)
        };
        byte[] prefix = new byte[4];
        int count = 0;
        while (count < prefix.Length)
        {
            int read = await stream.ReadAsync(prefix.AsMemory(count)).ConfigureAwait(false);
            if (read == 0) break;
            count += read;
        }
        int bom = 0;
        if (count >= 4 && prefix[0] == 0xff && prefix[1] == 0xfe && prefix[2] == 0 && prefix[3] == 0)
        { encoding = new UTF32Encoding(false, false, true); bom = 4; }
        else if (count >= 4 && prefix[0] == 0 && prefix[1] == 0 && prefix[2] == 0xfe && prefix[3] == 0xff)
        { encoding = new UTF32Encoding(true, false, true); bom = 4; }
        else if (count >= 3 && prefix[0] == 0xef && prefix[1] == 0xbb && prefix[2] == 0xbf)
        { encoding = new UTF8Encoding(false, true); bom = 3; }
        else if (count >= 2 && prefix[0] == 0xff && prefix[1] == 0xfe)
        { encoding = new UnicodeEncoding(false, false, true); bom = 2; }
        else if (count >= 2 && prefix[0] == 0xfe && prefix[1] == 0xff)
        { encoding = new UnicodeEncoding(true, false, true); bom = 2; }
        using var prefixed = new PrefixStream(stream, prefix.AsMemory(bom, count - bom));
        using var reader = new StreamReader(prefixed, encoding, false, 65536, leaveOpen: true);
        try
        {
            string text = await reader.ReadToEndAsync().ConfigureAwait(false);
            if (text.Contains('\0')) throw new InvalidDataException("El archivo contiene datos binarios; selecciona su codificación si es texto.");
            return (text, reader.CurrentEncoding.WebName, bom > 0);
        }
        catch (DecoderFallbackException ex)
        {
            throw new InvalidDataException("No se pudo decodificar el texto. Selecciona UTF-8, UTF-16 o Windows-1252 y vuelve a abrir el archivo.", ex);
        }
    }

    // Replay the sniffed bytes without seeking or buffering the whole file.
    private sealed class PrefixStream : Stream
    {
        private readonly Stream _stream;
        private ReadOnlyMemory<byte> _prefix;
        public PrefixStream(Stream stream, ReadOnlyMemory<byte> prefix) { _stream = stream; _prefix = prefix; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_prefix.Length == 0) return _stream.Read(buffer, offset, count);
            int length = Math.Min(count, _prefix.Length);
            _prefix.Span[..length].CopyTo(buffer.AsSpan(offset, length));
            _prefix = _prefix[length..];
            return length;
        }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, System.Threading.CancellationToken ct = default)
        {
            if (_prefix.Length == 0) return _stream.ReadAsync(buffer, ct);
            int length = Math.Min(buffer.Length, _prefix.Length);
            _prefix.Span[..length].CopyTo(buffer.Span);
            _prefix = _prefix[length..];
            return ValueTask.FromResult(length);
        }
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, System.Threading.CancellationToken ct) =>
            ReadAsync(buffer.AsMemory(offset, count), ct).AsTask();
    }
}
