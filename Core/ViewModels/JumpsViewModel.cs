using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using BaseLogApp.Data;
using BaseLogApp.Models;
using Microsoft.Maui.Dispatching;

namespace BaseLogApp.ViewModels;

public partial class JumpsPageViewModel : ObservableObject
{
    private readonly IJumpsRepository _repo;
    public ObservableCollection<JumpItemViewModel> Jumps { get; } = new();

    public JumpsPageViewModel(IJumpsRepository repo)
    {
        _repo = repo;
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        var items = await _repo.GetAllAsync().ConfigureAwait(false);
        MainThread.BeginInvokeOnMainThread(() =>
        {
            Jumps.Clear();
            foreach (var j in items)
                Jumps.Add(new JumpItemViewModel(j));
        });

        // Hydration asincrona di Exit e Thumbnail
        _ = Task.Run(async () =>
        {
            var tasks = Jumps
                .Where(vm => vm.Model.ObjectId.HasValue)
                .Select(vm => vm.HydrateAsync(_repo));
            await Task.WhenAll(tasks);
        });
    }

    [RelayCommand]
    public void ToggleExpand(JumpItemViewModel? item)
    {
        if (item is null) return;
        item.IsExpanded = !item.IsExpanded;
    }
}
