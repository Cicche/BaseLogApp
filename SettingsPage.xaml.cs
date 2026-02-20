namespace BaseLogApp.Views;

public partial class SettingsPage : ContentPage
{
    public SettingsPage()
    {
        InitializeComponent();
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
        => await DisplayAlert("Object", "Salvataggio object da collegare al DB da completare nel prossimo step.", "OK");

    private async void OnAddRigClicked(object sender, EventArgs e)
        => await DisplayAlert("Rig", "Salvataggio rig da collegare al DB da completare nel prossimo step.", "OK");

    private async void OnImportDbClicked(object sender, EventArgs e)
        => await DisplayAlert("Import", "Import DB: placeholder pronto, collegamento file-picker nel prossimo step.", "OK");

    private async void OnExportDbClicked(object sender, EventArgs e)
        => await DisplayAlert("Export", "Export DB: placeholder pronto, collegamento sharing nel prossimo step.", "OK");
}
