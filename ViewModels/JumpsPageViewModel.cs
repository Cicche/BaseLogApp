using BaseLogApp.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.Dispatching;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace BaseLogApp.ViewModels;

public partial class JumpsPageViewModel : ObservableObject
{
    private readonly IJumpsRepository _repo;
    private readonly List<JumpItemViewModel> _allJumps = new();
    private JumpItemViewModel? _expandedItem;

    public ObservableCollection<JumpItemViewModel> Jumps { get; } = new();

    [ObservableProperty]
    private string? searchText;

    [ObservableProperty]
    private string pageTitle = "Salti";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CountSummary))]
    private int filteredCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CountSummary))]
    private int totalCount;

    public string CountSummary => $"{FilteredCount}/{TotalCount}";

    public JumpsPageViewModel(IJumpsRepository repo)
    {
        _repo = repo;
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        // 1) Carica i record dal repository
        var items = await _repo.GetAllAsync().ConfigureAwait(false);
        var viewModels = items.Select(j => new JumpItemViewModel(j)).ToList();

        // 2) Popola la Collection sul thread UI
        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            _allJumps.Clear();
            _allJumps.AddRange(viewModels);
            TotalCount = _allJumps.Count;
            ApplyFilter();
        });

        // 3) Hydrate in background (Exit, Thumbnail) per tutti gli item
        var snapshot = _allJumps.ToList(); // cattura una foto stabile
        _ = Task.Run(async () =>
        {
            var tasks = snapshot.Select(vm => vm.HydrateAsync(_repo));
            await Task.WhenAll(tasks);
        });
    }

    [RelayCommand]
    public void ToggleExpand(JumpItemViewModel? item)
    {
        if (item is null) return;

        void Update()
        {
            var shouldExpand = !item.IsExpanded;

            if (shouldExpand)
            {
                if (_expandedItem is not null && _expandedItem != item)
                    _expandedItem.IsExpanded = false;

                item.IsExpanded = true;
                _expandedItem = item;
            }
            else
            {
                item.IsExpanded = false;
                if (_expandedItem == item)
                    _expandedItem = null;
            }
        }

        if (MainThread.IsMainThread)
        {
            Update();
        }
        else
        {
            MainThread.BeginInvokeOnMainThread(Update);
        }
    }

    partial void OnSearchTextChanged(string? value) => ApplyFilter();

    private void ApplyFilter()
    {
        var filteredItems = FilterItems(SearchText);

        void Update()
        {
            Jumps.Clear();
            foreach (var vm in filteredItems)
                Jumps.Add(vm);

            FilteredCount = Jumps.Count;
            PageTitle = $"Salti ({CountSummary})";

            if (_expandedItem is not null && !filteredItems.Contains(_expandedItem))
                _expandedItem = null;
        }

        if (MainThread.IsMainThread)
        {
            Update();
        }
        else
        {
            MainThread.BeginInvokeOnMainThread(Update);
        }
    }

    private List<JumpItemViewModel> FilterItems(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return _allJumps.ToList();

        var criteria = query.Trim();
        return _allJumps
            .Where(vm => vm.Matches(criteria))
            .ToList();
    }
}
