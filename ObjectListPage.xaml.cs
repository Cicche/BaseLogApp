using BaseLogApp.Core.Models;
using BaseLogApp.Core.ViewModels;
using System.Collections.ObjectModel;
using Microsoft.Extensions.DependencyInjection;
namespace BaseLogApp.Views;
public partial class ObjectListPage : ContentPage
{
    private readonly JumpsViewModel _vm;
    private readonly IServiceProvider _services;
    private readonly ObservableCollection<ObjectCatalogItem> _items = new();
    private readonly List<ObjectCatalogItem> _allItems = new();
    public ObjectListPage(JumpsViewModel vm, IServiceProvider services)
    {
        InitializeComponent();
        _vm = vm;
        _services = services;
        ObjectsView.ItemsSource = _items;
        ToolbarItems.Add(new ToolbarItem
        {
            Text = "+",
            Command = new Command(async () => await Navigation.PushModalAsync(new NavigationPage(_services.GetRequiredService<AddObjectPage>())))
        });
    }
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _vm.LoadAsync();
        var jumpCounts = _vm.Items
            .Where(x => !string.IsNullOrWhiteSpace(x.Oggetto))
            .GroupBy(x => x.Oggetto!.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        var rows = await _vm.GetObjectsCatalogAsync();
        _allItems.Clear();
        foreach (var row in rows)
        {
            row.JumpCount = jumpCounts.TryGetValue(row.Name.Trim(), out var count) ? count : 0;
            _allItems.Add(row);
        }
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
                || (!string.IsNullOrWhiteSpace(it.Region) && it.Region.Contains(q, StringComparison.OrdinalIgnoreCase))
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
        {
            var page = ActivatorUtilities.CreateInstance<AddObjectPage>(_services, item);
            await Navigation.PushModalAsync(new NavigationPage(page));
        }
        ObjectsView.SelectedItem = null;
    }
}
