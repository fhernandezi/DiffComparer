using Avalonia.Headless.XUnit;
using DiffComparer.Tests.Fakes;
using DiffComparer.UI.ViewModels;
using Xunit;

namespace DiffComparer.Tests;

public sealed class MainWindowViewModelFileToolbarTests
{
    [AvaloniaFact]
    public void InitialState_ShouldStartInEditMode()
    {
        var filePicker = new FakeFilePickerService();
        var vm = new MainWindowViewModel(filePicker);

        Assert.True(vm.IsEditMode);
        Assert.False(vm.IsComparing);
    }

    [AvaloniaFact]
    public async Task OpenLeftFileCommand_ShouldSetLeftText()
    {
        var filePicker = new FakeFilePickerService
        {
            NextFileContent = "left file content"
        };

        var vm = new MainWindowViewModel(filePicker);

        await vm.OpenLeftFileCommand.ExecuteAsync(null);

        Assert.Equal("left file content", vm.LeftText);
    }

    [AvaloniaFact]
    public async Task OpenRightFileCommand_ShouldSetRightText()
    {
        var filePicker = new FakeFilePickerService
        {
            NextFileContent = "right file content"
        };

        var vm = new MainWindowViewModel(filePicker);

        await vm.OpenRightFileCommand.ExecuteAsync(null);

        Assert.Equal("right file content", vm.RightText);
    }

    [AvaloniaFact]
    public async Task OpenLeftFileCommand_WhenPickerReturnsNull_ShouldNotOverwriteLeftText()
    {
        var filePicker = new FakeFilePickerService
        {
            NextFileContent = null
        };

        var vm = new MainWindowViewModel(filePicker)
        {
            LeftText = "previous left text"
        };

        await vm.OpenLeftFileCommand.ExecuteAsync(null);

        Assert.Equal("previous left text", vm.LeftText);
    }

    [AvaloniaFact]
    public async Task CompareCommand_ShouldLeaveEditMode()
    {
        var filePicker = new FakeFilePickerService();
        var vm = new MainWindowViewModel(filePicker)
        {
            LeftText = "A",
            RightText = "B"
        };

        await vm.CompareCommand.ExecuteAsync(null);

        Assert.False(vm.IsEditMode);
    }

    [AvaloniaFact]
    public void EditCommand_ShouldReturnToEditMode()
    {
        var filePicker = new FakeFilePickerService();
        var vm = new MainWindowViewModel(filePicker);

        vm.IsEditMode = false;

        vm.EditCommand.Execute(null);

        Assert.True(vm.IsEditMode);
    }

    [AvaloniaFact]
    public void CompareCancelCommand_ShouldExist()
    {
        var filePicker = new FakeFilePickerService();
        var vm = new MainWindowViewModel(filePicker);

        Assert.NotNull(vm.CompareCancelCommand);
    }
}