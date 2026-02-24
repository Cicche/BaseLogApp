using BaseLogApp.Core.Models;
using BaseLogApp.Core.ViewModels;
using System.Collections.ObjectModel;

namespace BaseLogApp.Views;

public partial class ObjectListPage : ContentPage
{
    private readonly JumpsViewModel _vm;
    private readonly ObservableCollection<ObjectCatalogItem> _items = new();

    public ObjectListPage(JumpsViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        ObjectsView.ItemsSource = _items;

        ToolbarItems.Add(new ToolbarItem
        {
            Text = "+",
            Command = new Command(async () => await Navigation.PushModalAsync(new NavigationPage(new AddObjectPage(_vm))))
        });
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        var rows = await _vm.GetObjectsCatalogAsync();
        _items.Clear();
        foreach (var r in rows)
            _items.Add(r);
    }

    private async void OnObjectSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is ObjectCatalogItem item)
            await Navigation.PushModalAsync(new NavigationPage(new AddObjectPage(_vm, item)));

        ObjectsView.SelectedItem = null;
    }
}
