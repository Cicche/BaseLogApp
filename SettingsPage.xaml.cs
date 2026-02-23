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

    private async void OnAutoPositionClicked(object sender, EventArgs e)
    {
        try
        {
            var location = await Geolocation.GetLastKnownLocationAsync();
            if (location is null)
                location = await Geolocation.GetLocationAsync(new GeolocationRequest(GeolocationAccuracy.Best, TimeSpan.FromSeconds(10)));

            if (location is not null)
                ObjectPositionEntry.Text = $"{location.Latitude:F6}, {location.Longitude:F6}";
            else
                await DisplayAlert("GPS", "Posizione non disponibile.", "OK");
        }
        catch (Exception ex)
        {
            await DisplayAlert("GPS", $"Impossibile leggere la posizione: {ex.Message}", "OK");
        }
    }

    private async void OnAddObjectClicked(object sender, EventArgs e)
    {
        var saved = await _vm.AddObjectAsync(
            ObjectNameEntry.Text ?? string.Empty,
            ObjectDescriptionEntry.Text,
            ObjectPositionEntry.Text,
            ObjectHeightEntry.Text);

        if (!saved)
        {
            await DisplayAlert("Object", "Impossibile salvare object nel DB.", "OK");
            return;
        }

        ObjectNameEntry.Text = string.Empty;
        ObjectDescriptionEntry.Text = string.Empty;
        ObjectHeightEntry.Text = string.Empty;
        ObjectPositionEntry.Text = string.Empty;
        await DisplayAlert("Object", "Object salvato nel DB.", "OK");
    }

    private async void OnAddRigClicked(object sender, EventArgs e)
    {
        var saved = await _vm.AddRigAsync(RigNameEntry.Text ?? string.Empty, RigDescriptionEntry.Text);
        if (!saved)
        {
            await DisplayAlert("Rig", "Impossibile salvare rig nel DB.", "OK");
            return;
        }

        RigNameEntry.Text = string.Empty;
        RigDescriptionEntry.Text = string.Empty;
        await DisplayAlert("Rig", "Rig salvato nel DB.", "OK");
    }

    private async void OnImportDbClicked(object sender, EventArgs e)
        => await DisplayAlert("Import", "Import DB verrà implementato nel prossimo step con file picker.", "OK");

    private async void OnExportDbClicked(object sender, EventArgs e)
        => await DisplayAlert("Export", "Export DB verrà implementato nel prossimo step con file sharing.", "OK");
}
