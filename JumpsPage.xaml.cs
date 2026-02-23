using BaseLogApp.Core.Models;
using BaseLogApp.Core.ViewModels;

namespace BaseLogApp.Views;

public partial class JumpsPage : ContentPage
{
    private readonly JumpsViewModel _vm;
    private readonly ToolbarItem _dbSwitchItem;

    public JumpsPage(JumpsViewModel vm)
    {
        InitializeComponent();

        _vm = vm;
        BindingContext = _vm;

        _dbSwitchItem = new ToolbarItem
        {
            Text = _vm.CurrentProfileLabel,
            Priority = 0,
            Order = ToolbarItemOrder.Primary,
            Command = new Command(async () => await OnSwitchDbClicked())
        };

        ToolbarItems.Add(_dbSwitchItem);
        ToolbarItems.Add(new ToolbarItem
        {
            Text = "+",
            Priority = 1,
            Order = ToolbarItemOrder.Primary,
            Command = new Command(async () => await OpenNewJumpPage())
        });
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _vm.LoadAsync();
        _dbSwitchItem.Text = _vm.CurrentProfileLabel;
    }

    private async Task OnSwitchDbClicked()
    {
        await _vm.ToggleDbProfileAsync();
        _dbSwitchItem.Text = _vm.CurrentProfileLabel;
        await DisplayAlert("DB attivo", _vm.GetCurrentDbPath(), "OK");
    }

    private async Task OpenNewJumpPage()
    {
        var knownObjects = await _vm.GetObjectNamesAsync();
        var page = new NewJumpPage(_vm.NextJumpNumber, knownObjects);
        page.JumpSaved += OnJumpSaved;
        await Navigation.PushModalAsync(new NavigationPage(page));
    }

    private async void OnJumpSaved(object? sender, JumpListItem e)
    {
        var saved = await _vm.SaveJumpAsync(e);
        if (!saved)
            await DisplayAlert("DB", "Impossibile salvare il salto nel database.", "OK");

        if (sender is NewJumpPage page)
            page.JumpSaved -= OnJumpSaved;
    }
}
