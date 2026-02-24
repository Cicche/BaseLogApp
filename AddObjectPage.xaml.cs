using BaseLogApp.Core.Models;
using BaseLogApp.Core.ViewModels;
using System.Collections.ObjectModel;
using System.Globalization;

namespace BaseLogApp.Views;

public partial class AddObjectPage : ContentPage
{
    private readonly JumpsViewModel _vm;
    private byte[]? _objectPhotoBytes;
    private ObjectCatalogItem? _editing;
    private readonly List<ObjectCatalogItem> _existing = new();
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
        }
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        var rows = await _vm.GetObjectsCatalogAsync();
        _existing.Clear();
        _existing.AddRange(rows.OrderBy(x => x.Name));
        ExistingObjectPicker.ItemsSource = _existing.Select(x => x.Name).ToList();
    }

    private void FillFromObject(ObjectCatalogItem item)
    {
        ObjectNameEntry.Text = item.Name;
        ObjectTypeEntry.Text = item.ObjectType;
        ObjectDescriptionEntry.Text = item.Description ?? item.Notes;
        ObjectPositionEntry.Text = item.Position;
        ObjectHeightEntry.Text = item.HeightMeters;

        if (item.PhotoBlob is { Length: > 0 })
        {
            ObjectPreviewImage.Source = ImageSource.FromStream(() => new MemoryStream(item.PhotoBlob));
            ObjectPreviewImage.IsVisible = true;
        }
        else
        {
            ObjectPreviewImage.Source = null;
            ObjectPreviewImage.IsVisible = false;
        }
    }

    private void OnExistingObjectChanged(object? sender, EventArgs e)
    {
        if (ExistingObjectPicker.SelectedIndex < 0 || ExistingObjectPicker.SelectedIndex >= _existing.Count)
            return;

        _editing = _existing[ExistingObjectPicker.SelectedIndex];
        Title = "Modifica Object";
        SaveButton.Text = "Salva modifiche";
        FillFromObject(_editing);
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
    }

    private async void OnSaveClicked(object sender, EventArgs e)
    {
        var target = _editing;
        var ok = target is null
            ? await _vm.AddObjectAsync(ObjectNameEntry.Text ?? string.Empty, ObjectTypeEntry.Text, ObjectDescriptionEntry.Text, ObjectPositionEntry.Text, ObjectHeightEntry.Text, _objectPhotoBytes)
            : await _vm.UpdateObjectAsync(target.Id, ObjectNameEntry.Text ?? string.Empty, ObjectTypeEntry.Text, ObjectDescriptionEntry.Text, ObjectPositionEntry.Text, ObjectHeightEntry.Text, _objectPhotoBytes);

        await DisplayAlert("Object", ok ? "Object salvato" : "Errore salvataggio object", "OK");
        if (ok) await Navigation.PopModalAsync();
    }

    private async void OnCloseClicked(object sender, EventArgs e)
        => await Navigation.PopModalAsync();
}
