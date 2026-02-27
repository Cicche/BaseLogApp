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

    private async void OnOpenObjectListClicked(object sender, EventArgs e)
        => await Navigation.PushAsync(new ObjectListPage(_vm));

    private async void OnOpenRigListClicked(object sender, EventArgs e)
        => await Navigation.PushAsync(new RigListPage(_vm));

    private async void OnOpenJumpTypeListClicked(object sender, EventArgs e)
        => await Navigation.PushAsync(new JumpTypeListPage(_vm));

    private async void OnOpenDbToolsClicked(object sender, EventArgs e)
        => await Navigation.PushModalAsync(new NavigationPage(new DbToolsPage(_vm)));

    private async void OnOpenDiagnosticClicked(object sender, EventArgs e)
        => await Navigation.PushAsync(new DiagnosticPage(_vm));
}
