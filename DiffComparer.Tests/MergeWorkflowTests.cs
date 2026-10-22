using Avalonia.Headless.XUnit;
using DiffComparer.Core;
using DiffComparer.Tests.Fakes;
using DiffComparer.UI.Interfaces;
using DiffComparer.UI.Models;
using DiffComparer.UI.Services;
using DiffComparer.UI.ViewModels;
using System.Text;

namespace DiffComparer.Tests;

public class MergeWorkflowTests
{
    [AvaloniaFact]
    public async Task CopyRecomputeUndoRedo_KeepCanonicalDocumentsAndRejectOldArrows()
    {
        using var vm = new MainWindowViewModel(new FakeFilePickerService())
        { LeftText = "head\nold\nmiddle\ntail", RightText = "head\nnew\nmiddle\ntail\nextra" };
        await vm.CompareCommand.ExecuteAsync(null);
        var original = vm.CanonicalResult!;
        var oldRequest = new CopyRequest(original.Id, 0, CopyDirection.LeftToRight);
        await vm.ApplyChangeCommand.ExecuteAsync(oldRequest);
        Assert.Equal("head\nold\nmiddle\ntail\nextra", vm.RightText);
        Assert.True(vm.IsResultCurrent);
        Assert.Equal(1, vm.ChangeBlockCount);
        Assert.False(vm.ApplyChangeCommand.CanExecute(oldRequest));
        await vm.ApplyChangeCommand.ExecuteAsync(oldRequest);
        Assert.Equal(1, vm.ChangeBlockCount);
        await vm.UndoMergeCommand.ExecuteAsync(null);
        Assert.Equal(original.Right.Text, vm.RightText);
        Assert.Equal(2, vm.ChangeBlockCount);
        await vm.RedoMergeCommand.ExecuteAsync(null);
        Assert.Equal(1, vm.ChangeBlockCount);
        await vm.CopyToRightCommand.ExecuteAsync(null);
        Assert.Equal(vm.LeftText, vm.RightText);
        Assert.Equal(0, vm.ChangeBlockCount);
        vm.EditCommand.Execute(null);
        Assert.Same(vm.Session.Right, vm.RightDocument);
        vm.RightDocument.UndoStack.Undo();
        Assert.EndsWith("extra", vm.RightText);
    }

    [AvaloniaFact]
    public async Task PartiallyFilteredHunk_CannotBeCopied_AndManualChangesInvalidateResult()
    {
        using var vm = new MainWindowViewModel(new FakeFilePickerService())
        { LeftText = "head\nvalue old\nremove this\ntail", RightText = "head\nvalue new\ntail" };
        await vm.CompareCommand.ExecuteAsync(null);
        Assert.True(vm.CopyToRightCommand.CanExecute(null));
        vm.ShowDeleted = false;
        await vm.PendingViewUpdate;
        Assert.False(vm.CopyToRightCommand.CanExecute(null));
        vm.ShowDeleted = true;
        await vm.PendingViewUpdate;
        Assert.True(vm.CopyToRightCommand.CanExecute(null));
        vm.RightDocument.Insert(0, "manual\n");
        Assert.False(vm.IsResultCurrent);
        Assert.False(vm.CopyToRightCommand.CanExecute(null));
        await vm.CompareCommand.ExecuteAsync(null);
        Assert.True(vm.IsResultCurrent);
    }

    [AvaloniaFact]
    public void Session_UndoesCopiesAcrossBothSidesInOrder()
    {
        var session = new ComparisonSession();
        session.Load(DocumentSide.Left, "a\nLEFT\nmiddle\nleft tail");
        session.Load(DocumentSide.Right, "a\nRIGHT\nmiddle\nright tail");
        ComparisonResult Analyze()
        {
            var capture = session.Capture();
            return new DiffEngine().Analyze(capture.Left.Materialize(), capture.Right.Materialize());
        }
        var first = Analyze();
        session.Apply(first, new(first.Id, 0, CopyDirection.LeftToRight), false);
        var second = Analyze();
        session.Apply(second, new(second.Id, 0, CopyDirection.RightToLeft), false);
        Assert.Equal(session.Left.Text, session.Right.Text);
        session.Undo();
        Assert.EndsWith("left tail", session.Left.Text);
        session.Undo();
        Assert.Contains("RIGHT", session.Right.Text);
        session.Redo(); session.Redo();
        Assert.Equal(session.Left.Text, session.Right.Text);
        Assert.Throws<InvalidOperationException>(() => session.Apply(first, new(first.Id, 0, CopyDirection.LeftToRight), false));
    }

    [Theory]
    [InlineData("utf-8", false)] [InlineData("utf-8", true)]
    [InlineData("utf-16", true)] [InlineData("utf-16BE", true)]
    [InlineData("utf-32", true)] [InlineData("utf-32BE", true)]
    public async Task SavedBytes_RoundTripEncodingBomAndNewlines(string encoding, bool bom)
    {
        var content = new TextFileContent("á 👩‍💻\r\nfin", "test", "", encoding, "CRLF", bom);
        using var stream = new MemoryStream(TextFileEncoding.Encode(content));
        var read = await TextFileReader.ReadWithMetadataAsync(stream, "Automático");
        Assert.Equal(content.Text, read.Text);
        Assert.Equal(bom, read.HasBom);
        Assert.Equal(encoding.ToLowerInvariant(), read.Encoding.ToLowerInvariant());
    }

    [Fact]
    public void Save_RejectsUnrepresentableTextBeforeWriting()
    {
        Assert.Throws<EncoderFallbackException>(() => TextFileEncoding.Encode(
            new("😀", "test", "", "windows-1252", "Sin saltos")));
    }

    private sealed class SavePicker : IFilePickerService, ITextFileSaveService
    {
        public bool Cancel, Fail;
        public TextFileContent? Saved;
        public Task<string?> OpenTextFileAsync() => Task.FromResult<string?>(null);
        public Task<TextFileContent?> SaveFileAsAsync(TextFileContent content)
        {
            if (Fail) throw new IOException("destino no disponible");
            Saved = content;
            return Task.FromResult(Cancel ? null : content with { Name = "saved.txt", Path = "saved.txt" });
        }
    }

    [AvaloniaFact]
    public async Task SaveAs_UsesRealTextInsteadOfProjection_AndOnlyMarksSuccessfulSaveClean()
    {
        var picker = new SavePicker { Cancel = true };
        using var vm = new MainWindowViewModel(picker) { LeftText = "a\nb", RightText = "a\nx\nb" };
        await vm.CompareCommand.ExecuteAsync(null);
        await vm.SaveLeftAsCommand.ExecuteAsync(null);
        Assert.Contains("editado", vm.LeftHeader);
        picker.Cancel = false; picker.Fail = true;
        await vm.SaveLeftAsCommand.ExecuteAsync(null);
        Assert.Contains("No se pudo guardar", vm.StatsText);
        Assert.Contains("editado", vm.LeftHeader);
        picker.Fail = false;
        await vm.SaveLeftAsCommand.ExecuteAsync(null);
        Assert.Equal("a\nb", picker.Saved!.Text);
        Assert.Equal("saved.txt", vm.LeftName);
        Assert.DoesNotContain("editado", vm.LeftHeader);
        Assert.True(vm.IsResultCurrent);
    }
}
