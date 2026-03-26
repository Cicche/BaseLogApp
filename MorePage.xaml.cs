using BaseLogApp.Core.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace BaseLogApp.Views;

public partial class MorePage : ContentPage
{
    private readonly JumpsViewModel _vm;
    private readonly IServiceProvider _services;

    public MorePage(JumpsViewModel vm, IServiceProvider services)
    {
        InitializeComponent();
        _vm = vm;
        _services = services;

        ToolbarItems.Add(new ToolbarItem
        {
            Text = "+",
            Command = new Command(async () => await OnAddMenuClicked())
        });
    }

    private async void OnGearTapped(object sender, EventArgs e)
        => await Navigation.PushAsync(_services.GetRequiredService<RigListPage>());

    private async void OnConfigurationTapped(object sender, EventArgs e)
        => await Navigation.PushAsync(_services.GetRequiredService<ConfigurationPage>());

    private async void OnJumpTypeTapped(object sender, EventArgs e)
        => await Navigation.PushAsync(_services.GetRequiredService<JumpTypeListPage>());

    private async void OnDiagnosticTapped(object sender, EventArgs e)
        => await Navigation.PushAsync(_services.GetRequiredService<DiagnosticPage>());

    private async void OnDbToolsTapped(object sender, EventArgs e)
        => await Navigation.PushModalAsync(new NavigationPage(_services.GetRequiredService<DbToolsPage>()));

    private async Task OnAddMenuClicked()
    {
        var action = await DisplayActionSheet(
            "Aggiungi",
            "Annulla",
            null,
            "Rig",
            "Tipo salto",
            "Deployment Type",
            "Slider",
            "Pilot Chute",
            "Brake Setting");

        if (string.IsNullOrWhiteSpace(action) || action == "Annulla")
            return;

        switch (action)
        {
            case "Rig":
                await Navigation.PushModalAsync(new NavigationPage(_services.GetRequiredService<AddRigPage>()));
                break;
            case "Tipo salto":
                await Navigation.PushModalAsync(new NavigationPage(_services.GetRequiredService<AddJumpTypePage>()));
                break;
            case "Deployment Type":
                await AddConfigurationValueAsync("Deployment Type", (name, notes) => _vm.AddDeploymentTypeAsync(name, notes));
                break;
            case "Slider":
                await AddConfigurationValueAsync("Slider", (name, notes) => _vm.AddSliderTypeAsync(name, notes));
                break;
            case "Pilot Chute":
                await AddConfigurationValueAsync("Pilot Chute", (name, notes) => _vm.AddPilotChuteTypeAsync(name, notes));
                break;
            case "Brake Setting":
                await AddConfigurationValueAsync("Brake Setting", (name, notes) => _vm.AddBrakeSettingAsync(name, notes));
                break;
        }
    }

    private async Task AddConfigurationValueAsync(string category, Func<string, string?, Task<bool>> saver)
    {
        var name = await DisplayPromptAsync("Nuova voce", $"Nome {category}:", "Salva", "Annulla");
        if (string.IsNullOrWhiteSpace(name))
            return;

        var ok = await saver(name.Trim(), null);
        if (!ok)
            await DisplayAlert("Aggiungi", $"Impossibile salvare {category}.", "OK");
    }
}
