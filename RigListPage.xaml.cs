using BaseLogApp.Core.Models;
using BaseLogApp.Core.ViewModels;
using System.Collections.ObjectModel;

namespace BaseLogApp.Views;

public partial class RigListPage : ContentPage
{
    private readonly JumpsViewModel _vm;
    private readonly ObservableCollection<CatalogItem> _items = new();

    public RigListPage(JumpsViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        RigsView.ItemsSource = _items;

        ToolbarItems.Add(new ToolbarItem
        {
            Text = "+",
            Command = new Command(async () => await Navigation.PushModalAsync(new NavigationPage(new AddRigPage(_vm))))
        });
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        var rows = await _vm.GetRigsCatalogAsync();
        _items.Clear();
        foreach (var r in rows)
            _items.Add(r);
    }

    private async void OnRigSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is CatalogItem item)
            await Navigation.PushModalAsync(new NavigationPage(new AddRigPage(_vm, item)));

        RigsView.SelectedItem = null;
    }
}
