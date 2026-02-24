using BaseLogApp.Core.Models;
using BaseLogApp.Core.ViewModels;
using System.Globalization;

namespace BaseLogApp.Views;

public partial class AddObjectPage : ContentPage
{
    private readonly JumpsViewModel _vm;
    private byte[]? _objectPhotoBytes;
    private readonly ObjectCatalogItem? _editing;

    public AddObjectPage(JumpsViewModel vm, ObjectCatalogItem? editing = null)
    {
        InitializeComponent();
        _vm = vm;
        _editing = editing;

        if (_editing is not null)
        {
            Title = "Modifica Object";
            ObjectNameEntry.Text = _editing.Name;
            ObjectTypeEntry.Text = _editing.ObjectType;
            ObjectDescriptionEntry.Text = _editing.Description ?? _editing.Notes;
            ObjectPositionEntry.Text = _editing.Position;
            ObjectHeightEntry.Text = _editing.HeightMeters;
            SaveButton.Text = "Salva modifiche";
        }
    }

    private async void OnAutoPositionClicked(object sender, EventArgs e)
    {
        var location = await Geolocation.GetLastKnownLocationAsync() ?? await Geolocation.GetLocationAsync();
        if (location is null)
        {
            await DisplayAlert("GPS", "Posizione non disponibile", "OK");
            return;
        }

        ObjectPositionEntry.Text = $"{location.Latitude.ToString(CultureInfo.InvariantCulture)}, {location.Longitude.ToString(CultureInfo.InvariantCulture)}";
    }

    private async void OnOpenMapClicked(object sender, EventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(ObjectPositionEntry.Text))
            await Launcher.OpenAsync($"https://www.google.com/maps/search/?api=1&query={Uri.EscapeDataString(ObjectPositionEntry.Text)}");
        else
            await Launcher.OpenAsync("https://www.google.com/maps");
    }

    private async void OnPickPhotoClicked(object sender, EventArgs e)
    {
        var file = await MediaPicker.Default.PickPhotoAsync();
        if (file is null) return;

        await using var stream = await file.OpenReadAsync();
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms);
        _objectPhotoBytes = ms.ToArray();
        PhotoNameLabel.Text = Path.GetFileName(file.FileName);
    }

    private async void OnSaveClicked(object sender, EventArgs e)
    {
        var ok = _editing is null
            ? await _vm.AddObjectAsync(ObjectNameEntry.Text ?? string.Empty, ObjectTypeEntry.Text, ObjectDescriptionEntry.Text, ObjectPositionEntry.Text, ObjectHeightEntry.Text, _objectPhotoBytes)
            : await _vm.UpdateObjectAsync(_editing.Id, ObjectNameEntry.Text ?? string.Empty, ObjectTypeEntry.Text, ObjectDescriptionEntry.Text, ObjectPositionEntry.Text, ObjectHeightEntry.Text, _objectPhotoBytes);

        await DisplayAlert("Object", ok ? "Object salvato" : "Errore salvataggio object", "OK");
        if (ok)
            await Navigation.PopModalAsync();
    }

    private async void OnCloseClicked(object sender, EventArgs e)
        => await Navigation.PopModalAsync();
}
