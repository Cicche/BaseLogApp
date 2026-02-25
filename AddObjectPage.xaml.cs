using BaseLogApp.Core.Models;
using BaseLogApp.Core.ViewModels;
using System.Collections.ObjectModel;
using System.Globalization;

namespace BaseLogApp.Views;

public partial class AddObjectPage : ContentPage
{
    private readonly JumpsViewModel _vm;
    private byte[]? _objectPhotoBytes;
    private readonly ObjectCatalogItem? _editing;
    private readonly string[] _fixedTypes = ["Building", "Antennas", "Span", "Earth", "Others"];
    private readonly ObservableCollection<string> _typeSuggestions = new();

    public AddObjectPage(JumpsViewModel vm, ObjectCatalogItem? editing = null)
    {
        InitializeComponent();
        _vm = vm;
        _editing = editing;
        ObjectTypeSuggestionsView.ItemsSource = _typeSuggestions;

        if (_editing is not null)
        {
            Title = "Modifica Object";
            FillFromObject(_editing);
            SaveButton.Text = "Salva modifiche";
            DeleteButton.IsVisible = true;
        }
    }

    private void FillFromObject(ObjectCatalogItem item)
    {
        ObjectNameEntry.Text = item.Name;
        ObjectTypeEntry.Text = item.ObjectType;
        ObjectDescriptionEntry.Text = item.Description ?? item.Notes;
        ObjectPositionEntry.Text = item.Position;
        ObjectHeightEntry.Text = item.HeightMeters;
        NoPhotoNameLabel.Text = item.Name;

        if (item.PhotoBlob is { Length: > 0 })
        {
            ObjectPreviewImage.Source = ImageSource.FromStream(() => new MemoryStream(item.PhotoBlob));
            ObjectPreviewImage.IsVisible = true;
            NoPhotoBorder.IsVisible = false;
        }
        else
        {
            ObjectPreviewImage.Source = null;
            ObjectPreviewImage.IsVisible = false;
            NoPhotoBorder.IsVisible = true;
        }
    }

    private void OnObjectNameChanged(object? sender, TextChangedEventArgs e)
    {
        if (!ObjectPreviewImage.IsVisible)
            NoPhotoNameLabel.Text = string.IsNullOrWhiteSpace(e.NewTextValue) ? "Nome object" : e.NewTextValue.Trim();
    }

    private void OnObjectTypeTextChanged(object? sender, TextChangedEventArgs e)
    {
        var q = (e.NewTextValue ?? string.Empty).Trim();
        _typeSuggestions.Clear();
        if (q.Length == 0)
        {
            ObjectTypeSuggestionsView.IsVisible = false;
            return;
        }

        foreach (var t in _fixedTypes.Where(x => x.Contains(q, StringComparison.OrdinalIgnoreCase)))
            _typeSuggestions.Add(t);
        ObjectTypeSuggestionsView.IsVisible = _typeSuggestions.Count > 0;
    }

    private void OnObjectTypeSuggestionSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is string t)
            ObjectTypeEntry.Text = t;
        ObjectTypeSuggestionsView.SelectedItem = null;
        ObjectTypeSuggestionsView.IsVisible = false;
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
        ObjectPreviewImage.Source = ImageSource.FromStream(() => new MemoryStream(_objectPhotoBytes));
        ObjectPreviewImage.IsVisible = true;
        NoPhotoBorder.IsVisible = false;
    }

    private async void OnSaveClicked(object sender, EventArgs e)
    {
        var ok = _editing is null
            ? await _vm.AddObjectAsync(ObjectNameEntry.Text ?? string.Empty, ObjectTypeEntry.Text, ObjectDescriptionEntry.Text, ObjectPositionEntry.Text, ObjectHeightEntry.Text, _objectPhotoBytes)
            : await _vm.UpdateObjectAsync(_editing.Id, ObjectNameEntry.Text ?? string.Empty, ObjectTypeEntry.Text, ObjectDescriptionEntry.Text, ObjectPositionEntry.Text, ObjectHeightEntry.Text, _objectPhotoBytes);

        await DisplayAlert("Object", ok ? "Object salvato" : "Errore salvataggio object", "OK");
        if (ok) await Navigation.PopModalAsync();
    }

    private async void OnDeleteClicked(object sender, EventArgs e)
    {
        if (_editing is null)
            return;

        var check = await _vm.CanDeleteObjectAsync(_editing.Id);
        if (!check.CanDelete)
        {
            await DisplayAlert("Object", check.Reason ?? "Impossibile eliminare object associato a salti.", "OK");
            return;
        }

        var confirm = await DisplayAlert("Object", $"Eliminare '{_editing.Name}'?", "Sì", "No");
        if (!confirm)
            return;

        var ok = await _vm.DeleteObjectAsync(_editing.Id);
        await DisplayAlert("Object", ok ? "Object eliminato" : "Errore eliminazione object", "OK");
        if (ok)
            await Navigation.PopModalAsync();
    }

    private async void OnCloseClicked(object sender, EventArgs e)
        => await Navigation.PopModalAsync();
}
