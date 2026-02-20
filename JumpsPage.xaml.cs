using BaseLogApp.Core.Models;
using BaseLogApp.Core.ViewModels;

namespace BaseLogApp.Views;

public partial class JumpsPage : ContentPage
{
    private readonly JumpsViewModel _vm;

    public JumpsPage(JumpsViewModel vm)
    {
        InitializeComponent();

        _vm = vm;
        BindingContext = _vm;

        ToolbarItems.Add(new ToolbarItem
        {
            Text = "+",
            Priority = 0,
            Order = ToolbarItemOrder.Primary,
            Command = new Command(async () => await OpenNewJumpPage())
        });
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _vm.LoadAsync();
    }

    private async Task OpenNewJumpPage()
    {
        var knownObjects = await _vm.GetObjectNamesAsync();
        var page = new NewJumpPage(_vm.NextJumpNumber, knownObjects);
        page.JumpSaved += OnJumpSaved;
        await Navigation.PushModalAsync(new NavigationPage(page));
    }

    private void OnJumpSaved(object? sender, JumpListItem e)
    {
        _vm.AddJump(e);

        if (sender is NewJumpPage page)
            page.JumpSaved -= OnJumpSaved;
    }
}
