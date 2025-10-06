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
        // 1) Carica i record dal repository
        var items = await _repo.GetAllAsync().ConfigureAwait(false);

        // 2) Popola la Collection sul thread UI
        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            Jumps.Clear();
            foreach (var j in items)
                Jumps.Add(new JumpItemViewModel(j));
        });

        // 3) Hydrate in background (Exit, Thumbnail) per tutti gli item
        //    HydrateAsync internamente gestisce ObjectId null
        var snapshot = Jumps.ToList(); // cattura una foto stabile
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
        item.IsExpanded = !item.IsExpanded;
    }
   
    

}
