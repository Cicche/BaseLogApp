using BaseLogApp.Core.ViewModels;

namespace BaseLogApp.Views;

public partial class DbToolsPage : ContentPage
{
    private static readonly FilePickerFileType JsonFileType = new(new Dictionary<DevicePlatform, IEnumerable<string>>
    {
        { DevicePlatform.WinUI, new[] { ".json" } },
        { DevicePlatform.iOS, new[] { "public.json" } },
        { DevicePlatform.Android, new[] { "application/json" } },
        { DevicePlatform.macOS, new[] { "public.json" } }
    });

    private readonly JumpsViewModel _vm;

    public DbToolsPage(JumpsViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
    }

    private async void OnExportJsonClicked(object sender, EventArgs e)
    {
        var path = Path.Combine(FileSystem.AppDataDirectory, "jumps_export.json");
        var ok = await _vm.ExportLightweightJsonAsync(path);
        await DisplayAlert("Export JSON", ok ? $"File salvato: {path}" : "Export fallito", "OK");
    }

    private async void OnImportJsonClicked(object sender, EventArgs e)
    {
        var picked = await FilePicker.Default.PickAsync(new PickOptions
        {
            PickerTitle = "Seleziona file JSON",
            FileTypes = JsonFileType
        });

        if (picked is null) return;

        var ok = await _vm.ImportLightweightJsonAsync(picked.FullPath);
        await DisplayAlert("Import JSON", ok ? "Import completato" : "Import fallito", "OK");
        if (ok) await _vm.LoadAsync();
    }

    private async void OnExportFullClicked(object sender, EventArgs e)
    {
        var path = Path.Combine(FileSystem.AppDataDirectory, "full_export.sqlite");
        var ok = await _vm.ExportFullDbAsync(path);
        await DisplayAlert("Export DB", ok ? $"File salvato: {path}" : "Export fallito", "OK");
    }

    private async void OnImportFullClicked(object sender, EventArgs e)
    {
        var picked = await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = "Seleziona file .sqlite" });
        if (picked is null) return;

        var ok = await _vm.ImportFullDbAsync(picked.FullPath);
        await DisplayAlert("Import DB", ok ? "Import completato" : "Import fallito", "OK");
        if (ok) await _vm.LoadAsync();
    }

    private async void OnCloseClicked(object sender, EventArgs e)
        => await Navigation.PopModalAsync();
}
