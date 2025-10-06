using BaseLogApp.Data;
using BaseLogApp.ViewModels;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.Dispatching;
using System.Collections.ObjectModel;
using System.Xml.Linq;

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
        Jumps.Clear();
        var items = await _repo.GetAllAsync().ConfigureAwait(false);
        MainThread.BeginInvokeOnMainThread(() =>
        {
            foreach (var j in items)
                Jumps.Add(new JumpItemViewModel(j));
        });
    }

    [RelayCommand]
    public void ToggleExpand(JumpItemViewModel? item)
    {
        if (item is null) return;
        item.IsExpanded = !item.IsExpanded;
        // Per aprirne una sola alla volta, decommenta:
        // if (item.IsExpanded)
        //     foreach (var other in Jumps.Where(x => !ReferenceEquals(x, item)))
        //         other.IsExpanded = false;
    }

    [RelayCommand]
    public async Task Edit(JumpItemViewModel? item)
    {
        if (item is null) return;
        await Shell.Current.GoToAsync($"editjump?id={item.Id}");
    }

    [RelayCommand]
    public async Task ShowMap(JumpItemViewModel? item)
    {
        if (item is null || !item.HasCoordinates) return;
        await Shell.Current.GoToAsync($"map?lat={item.Latitude}&lng={item.Longitude}");
    }

    [RelayCommand]
    public async Task Delete(JumpItemViewModel? item)
    {
        if (item is null) return;
        await _repo.DeleteAsync(item.Id);
        Jumps.Remove(item);
    }
}
