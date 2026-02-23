using BaseLogApp.Core.ViewModels;
using System.Collections.ObjectModel;

namespace BaseLogApp.Views;

public partial class JumpTypeListPage : ContentPage
{
    private readonly JumpsViewModel _vm;
    private readonly ObservableCollection<string> _items = new();

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
        var rows = await _vm.GetJumpTypeNamesAsync();
        _items.Clear();
        foreach (var r in rows)
            _items.Add(r);
    }
}
