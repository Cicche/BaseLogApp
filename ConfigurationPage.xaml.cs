using BaseLogApp.Core.Diagnostics;
using BaseLogApp.Core.ViewModels;
using System.Collections.ObjectModel;

namespace BaseLogApp.Views;

public partial class ConfigurationPage : ContentPage
{
    private readonly JumpsViewModel _vm;

    private readonly ObservableCollection<string> _deploymentTypes = new();
    private readonly ObservableCollection<string> _sliderTypes = new();
    private readonly ObservableCollection<string> _pilotChutes = new();
    private readonly ObservableCollection<string> _brakeSettings = new();

    private string GetLogPath() => _vm.GetCurrentDbPath() + ".log";

    public ConfigurationPage(JumpsViewModel vm)
    {
        InitializeComponent();
        _vm = vm;

        DeploymentList.ItemsSource = _deploymentTypes;
        SliderList.ItemsSource = _sliderTypes;
        PilotChuteList.ItemsSource = _pilotChutes;
        BrakeSettingList.ItemsSource = _brakeSettings;

        ToolbarItems.Add(new ToolbarItem
        {
            Text = "+",
            Command = new Command(async () => await OnAddClickedAsync())
        });
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        await ReloadListAsync(_deploymentTypes, _vm.GetDeploymentTypeNamesAsync);
        await ReloadListAsync(_sliderTypes, _vm.GetSliderTypeNamesAsync);
        await ReloadListAsync(_pilotChutes, _vm.GetPilotChuteTypeNamesAsync);
        await ReloadListAsync(_brakeSettings, _vm.GetBrakeSettingNamesAsync);
    }

    private static async Task ReloadListAsync(ObservableCollection<string> target, Func<Task<IReadOnlyList<string>>> loader)
    {
        var rows = await loader();
        target.Clear();
        foreach (var item in rows)
            target.Add(item);
    }

    private async Task OnAddClickedAsync()
    {
        try
        {
            var category = await DisplayActionSheet(
                "Aggiungi voce",
                "Annulla",
                null,
                "Deployment Type",
                "Slider",
                "Pilot Chute",
                "Brake Setting");

            if (string.IsNullOrWhiteSpace(category) || category == "Annulla")
                return;

            var name = await DisplayPromptAsync("Nuova voce", $"Inserisci nome per {category}:", "Salva", "Annulla");
            if (string.IsNullOrWhiteSpace(name))
                return;

            var ok = category switch
            {
                "Deployment Type" => await _vm.AddDeploymentTypeAsync(name.Trim(), null),
                "Slider" => await _vm.AddSliderTypeAsync(name.Trim(), null),
                "Pilot Chute" => await _vm.AddPilotChuteTypeAsync(name.Trim(), null),
                "Brake Setting" => await _vm.AddBrakeSettingAsync(name.Trim(), null),
                _ => false
            };

            if (!ok)
            {
                await DisplayAlert("Configuration", "Impossibile salvare la voce.", "OK");
                return;
            }

            await ReloadAsync();
        }
        catch (Exception ex)
        {
            AppLog.Error(GetLogPath(), LogCategories.RuntimeError, nameof(ConfigurationPage), nameof(OnAddClickedAsync), "Add configuration entry failed.", ex: ex);
            await DisplayAlert("Configuration", $"Errore: {ex.Message}", "OK");
        }
    }
}
