using BaseLogApp.Core.Models;
using BaseLogApp.Core.ViewModels;
using System.Collections.ObjectModel;

namespace BaseLogApp.Views;

public partial class JumpTypeListPage : ContentPage
{
    private readonly JumpsViewModel _vm;
    private readonly ObservableCollection<CatalogItem> _items = new();

    public JumpTypeListPage(JumpsViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        TypesView.ItemsSource = _items;

        ToolbarItems.Add(new ToolbarItem
        {
            Text = "+",
            Command = new Command(async () => await Navigation.PushModalAsync(new NavigationPage(new AddJumpTypePage(_vm))))
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
            await Navigation.PushModalAsync(new NavigationPage(new AddJumpTypePage(_vm, item)));

        TypesView.SelectedItem = null;
    }
}
