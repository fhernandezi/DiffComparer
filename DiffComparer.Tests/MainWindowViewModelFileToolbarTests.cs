using DiffComparer.Tests.Fakes;
using DiffComparer.UI.ViewModels;
using Xunit;

namespace DiffComparer.Tests;

public sealed class MainWindowViewModelFileToolbarTests
{
    [Fact]
    public void InitialState_ShouldStartInEditMode()
    {
        var filePicker = new FakeFilePickerService();
        var vm = new MainWindowViewModel(filePicker);

        Assert.True(vm.IsEditMode);
        Assert.False(vm.IsComparing);
    }

    [Fact]
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

    [Fact]
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

    [Fact]
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

    [Fact]
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

    [Fact]
    public void EditCommand_ShouldReturnToEditMode()
    {
        var filePicker = new FakeFilePickerService();
        var vm = new MainWindowViewModel(filePicker);

        vm.IsEditMode = false;

        vm.EditCommand.Execute(null);

        Assert.True(vm.IsEditMode);
    }

    [Fact]
    public void CompareCancelCommand_ShouldExist()
    {
        var filePicker = new FakeFilePickerService();
        var vm = new MainWindowViewModel(filePicker);

        Assert.NotNull(vm.CompareCancelCommand);
    }
}