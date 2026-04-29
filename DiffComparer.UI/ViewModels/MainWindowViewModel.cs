using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiffComparer.Core;
using DiffComparer.UI.Interfaces;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace DiffComparer.UI.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly DiffEngine _engine = new();
    private readonly IFilePickerService _filePicker;
    public MainWindowViewModel(IFilePickerService filePicker)
    {
        _filePicker = filePicker;
    }


    [ObservableProperty]
    private string _leftText = "";

    [ObservableProperty]
    private string _rightText = "";

    [ObservableProperty]
    private bool _isEditMode = true;

    [ObservableProperty]
    private bool _isComparing = false;

    [ObservableProperty]
    private string _statsText = "";

    [ObservableProperty]
    private int _currentChangeIndex = -1;

    public ObservableCollection<LineMap> DiffLines { get; } = new();

    [RelayCommand(IncludeCancelCommand = true)]
    private async Task CompareAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(LeftText) &&
            string.IsNullOrWhiteSpace(RightText))
            return;

        IsComparing = true;
        DiffLines.Clear();

        var left = LeftText.Split('\n');
        var right = RightText.Split('\n');

        try
        {
            var result = await Task.Run(() =>
                _engine.Compare(left, right), ct);

            foreach (var line in result)
                DiffLines.Add(line);

            var added = result.Count(l => l.Status == LineStatus.Added);
            var deleted = result.Count(l => l.Status == LineStatus.Deleted);
            var modified = result.Count(l => l.Status == LineStatus.Modified);

            StatsText = $"+{added}  -{deleted}  ~{modified}";
            IsEditMode = false;
        }
        catch (OperationCanceledException)
        {
            StatsText = "Cancelado";
        }
        finally
        {
            IsComparing = false;
        }
    }

    [RelayCommand]
    private void Edit()
    {
        IsEditMode = true;
        StatsText = "";
    }
    [RelayCommand]
    private async Task OpenLeftFileAsync()
    {
        var text = await _filePicker.OpenTextFileAsync();

        if (text != null)
            LeftText = text;
    }

    [RelayCommand]
    private async Task OpenRightFileAsync()
    {
        var text = await _filePicker.OpenTextFileAsync();

        if (text != null)
            RightText = text;
    }
    [RelayCommand]
    private void NextChange()
    {
        var changes = GetChangeBlocks();

        if (changes.Count == 0)
            return;

        var next = changes.FirstOrDefault(i => i > CurrentChangeIndex);

        if (!changes.Any(i => i > CurrentChangeIndex))
            next = changes.First();

        CurrentChangeIndex = next;
    }

    [RelayCommand]
    private void PreviousChange()
    {
        var changes = GetChangeBlocks();

        if (changes.Count == 0)
            return;

        var previous = changes
            .Where(i => i < CurrentChangeIndex)
            .DefaultIfEmpty(changes.Last())
            .Last();

        CurrentChangeIndex = previous;
    }

    internal List<int> GetChangeBlocks()
    {
        var blocks = new List<int>();
        var insideBlock = false;

        for (int i = 0; i < DiffLines.Count; i++)
        {
            var isChange = DiffLines[i].Status != LineStatus.Equal;

            if (isChange && !insideBlock)
            {
                blocks.Add(i); // inicio del bloque
                insideBlock = true;
            }
            else if (!isChange)
            {
                insideBlock = false;
            }
        }

        return blocks;
    }
}