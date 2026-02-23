using BaseLogApp.Core.ViewModels;

namespace BaseLogApp.Views;

public partial class SettingsPage : ContentPage
{
    private readonly JumpsViewModel _vm;

    public SettingsPage(JumpsViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
    }

    private async void OnOpenAddObjectClicked(object sender, EventArgs e)
        => await Navigation.PushModalAsync(new NavigationPage(new AddObjectPage(_vm)));

    private async void OnOpenAddRigClicked(object sender, EventArgs e)
        => await Navigation.PushModalAsync(new NavigationPage(new AddRigPage(_vm)));

    private async void OnOpenDbToolsClicked(object sender, EventArgs e)
        => await Navigation.PushModalAsync(new NavigationPage(new DbToolsPage(_vm)));
}
