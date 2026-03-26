using BaseLogApp.Core.Models;
using BaseLogApp.Core.ViewModels;
using System.Collections.ObjectModel;
using Microsoft.Extensions.DependencyInjection;
namespace BaseLogApp.Views;
public partial class JumpTypeListPage : ContentPage
{
    private readonly JumpsViewModel _vm;
    private readonly IServiceProvider _services;
    private readonly ObservableCollection<CatalogItem> _items = new();
    public JumpTypeListPage(JumpsViewModel vm, IServiceProvider services)
    {
        InitializeComponent();
        _vm = vm;
        _services = services;
        TypesView.ItemsSource = _items;
        ToolbarItems.Add(new ToolbarItem
        {
            Text = "+",
            Command = new Command(async () => await Navigation.PushModalAsync(new NavigationPage(_services.GetRequiredService<AddJumpTypePage>())))
        });
    }
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        var rows = await _vm.GetJumpTypesCatalogAsync();
        _items.Clear();
        foreach (var r in rows)
            _items.Add(r);
    }
    private async void OnTypeSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is CatalogItem item)
        {
            var page = ActivatorUtilities.CreateInstance<AddJumpTypePage>(_services, item);
            await Navigation.PushModalAsync(new NavigationPage(page));
        }
        TypesView.SelectedItem = null;
    }
}
