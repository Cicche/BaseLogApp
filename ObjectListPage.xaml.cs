using BaseLogApp.Core.Models;
using BaseLogApp.Core.ViewModels;
using System.Collections.ObjectModel;

namespace BaseLogApp.Views;

public partial class ObjectListPage : ContentPage
{
    private readonly JumpsViewModel _vm;
    private readonly ObservableCollection<ObjectCatalogItem> _items = new();
    private readonly List<ObjectCatalogItem> _allItems = new();

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
        _allItems.Clear();
        _allItems.AddRange(rows);
        ApplyFilter(ObjectSearch.Text);
    }


    private void OnSearchTextChanged(object? sender, TextChangedEventArgs e)
        => ApplyFilter(e.NewTextValue);

    private void ApplyFilter(string? query)
    {
        var q = (query ?? string.Empty).Trim();
        IEnumerable<ObjectCatalogItem> filtered = _allItems;

        if (!string.IsNullOrWhiteSpace(q))
        {
            filtered = _allItems.Where(it =>
                   (!string.IsNullOrWhiteSpace(it.Name) && it.Name.Contains(q, StringComparison.OrdinalIgnoreCase))
                || (!string.IsNullOrWhiteSpace(it.ObjectType) && it.ObjectType.Contains(q, StringComparison.OrdinalIgnoreCase))
                || (!string.IsNullOrWhiteSpace(it.Description) && it.Description.Contains(q, StringComparison.OrdinalIgnoreCase))
                || (!string.IsNullOrWhiteSpace(it.Notes) && it.Notes.Contains(q, StringComparison.OrdinalIgnoreCase))
                || (!string.IsNullOrWhiteSpace(it.Position) && it.Position.Contains(q, StringComparison.OrdinalIgnoreCase))
                || (!string.IsNullOrWhiteSpace(it.HeightMeters) && it.HeightMeters.Contains(q, StringComparison.OrdinalIgnoreCase)));
        }

        _items.Clear();
        foreach (var item in filtered.OrderBy(x => x.Name))
            _items.Add(item);
    }

    private async void OnObjectSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is ObjectCatalogItem item)
            await Navigation.PushModalAsync(new NavigationPage(new AddObjectPage(_vm, item)));

        ObjectsView.SelectedItem = null;
    }
}
