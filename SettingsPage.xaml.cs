using BaseLogApp.Core.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace BaseLogApp.Views;

public partial class SettingsPage : ContentPage
{
    private readonly JumpsViewModel _vm;
    private readonly IServiceProvider _services;

    public SettingsPage(JumpsViewModel vm, IServiceProvider services)
    {
        InitializeComponent();
        _vm = vm;
        _services = services;
    }

    private async void OnOpenObjectListClicked(object sender, EventArgs e)
        => await Navigation.PushAsync(_services.GetRequiredService<ObjectListPage>());

    private async void OnOpenRigListClicked(object sender, EventArgs e)
        => await Navigation.PushAsync(_services.GetRequiredService<RigListPage>());

    private async void OnOpenJumpTypeListClicked(object sender, EventArgs e)
        => await Navigation.PushAsync(_services.GetRequiredService<JumpTypeListPage>());

    private async void OnOpenConfigurationClicked(object sender, EventArgs e)
        => await Navigation.PushAsync(_services.GetRequiredService<ConfigurationPage>());

    private async void OnOpenDbToolsClicked(object sender, EventArgs e)
        => await Navigation.PushModalAsync(new NavigationPage(_services.GetRequiredService<DbToolsPage>()));

    private async void OnOpenDiagnosticClicked(object sender, EventArgs e)
        => await Navigation.PushAsync(_services.GetRequiredService<DiagnosticPage>());
}

