using BaseLogApp.Core.Models;
using System.Collections.ObjectModel;
using System.Globalization;

namespace BaseLogApp.Views;

public partial class NewJumpPage : ContentPage
{
    public event EventHandler<JumpListItem>? JumpSaved;

    private readonly List<string> _allObjects;
    private readonly ObservableCollection<string> _filteredObjects = new();
    private string? _selectedPhotoPath;

    public NewJumpPage(int suggestedJumpNumber, IReadOnlyList<string> knownObjects)
    {
        InitializeComponent();

        _allObjects = knownObjects
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x)
            .ToList();

        ObjectSuggestionsView.ItemsSource = _filteredObjects;
        DatePicker.Date = DateTime.Today;
        NumberEntry.Text = suggestedJumpNumber.ToString();
    }

    private void OnObjectTextChanged(object? sender, TextChangedEventArgs e)
    {
        var query = (e.NewTextValue ?? string.Empty).Trim();
        _filteredObjects.Clear();

        if (query.Length < 1)
        {
            ObjectSuggestionsView.IsVisible = false;
            return;
        }

        foreach (var item in _allObjects.Where(x => x.Contains(query, StringComparison.OrdinalIgnoreCase)).Take(8))
            _filteredObjects.Add(item);

        ObjectSuggestionsView.IsVisible = _filteredObjects.Count > 0;
    }

    private void OnObjectSuggestionSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is string selected)
            ObjectEntry.Text = selected;

        ObjectSuggestionsView.SelectedItem = null;
        ObjectSuggestionsView.IsVisible = false;
    }

    private async void OnAutoGpsClicked(object sender, EventArgs e)
    {
        var location = await Geolocation.GetLastKnownLocationAsync() ?? await Geolocation.GetLocationAsync();
        if (location is null)
        {
            await DisplayAlert("GPS", "Posizione non disponibile", "OK");
            return;
        }

        LatitudeEntry.Text = location.Latitude.ToString(CultureInfo.InvariantCulture);
        LongitudeEntry.Text = location.Longitude.ToString(CultureInfo.InvariantCulture);
    }

    private async void OnOpenMapClicked(object sender, EventArgs e)
    {
        if (double.TryParse(LatitudeEntry.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var lat) &&
            double.TryParse(LongitudeEntry.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var lng))
        {
            await Launcher.OpenAsync($"https://www.google.com/maps/search/?api=1&query={lat.ToString(CultureInfo.InvariantCulture)},{lng.ToString(CultureInfo.InvariantCulture)}");
        }
        else
        {
            await Launcher.OpenAsync("https://www.google.com/maps");
        }
    }

    private async void OnPickPhotoClicked(object sender, EventArgs e)
    {
        try
        {
            var file = await MediaPicker.Default.PickPhotoAsync();
            if (file is null)
                return;

            _selectedPhotoPath = file.FullPath;
            PhotoPathLabel.Text = Path.GetFileName(_selectedPhotoPath);
        }
        catch (Exception ex)
        {
            await DisplayAlert("Foto", $"Impossibile selezionare foto: {ex.Message}", "OK");
        }
    }

    private async void OnCancelClicked(object sender, EventArgs e)
    {
        await Navigation.PopModalAsync();
    }

    private async void OnSaveClicked(object sender, EventArgs e)
    {
        if (!int.TryParse(NumberEntry.Text, out var jumpNumber))
        {
            await DisplayAlert("Dato non valido", "Inserisci un numero salto valido.", "OK");
            return;
        }

        var item = new JumpListItem
        {
            Id = jumpNumber,
            NumeroSalto = jumpNumber,
            Data = DatePicker.Date.ToString("dd/MM/yyyy"),
            Oggetto = ObjectEntry.Text,
            TipoSalto = TypeEntry.Text,
            Note = NotesEditor.Text,
            ObjectPhotoPath = _selectedPhotoPath,
            Latitude = LatitudeEntry.Text,
            Longitude = LongitudeEntry.Text
        };

        JumpSaved?.Invoke(this, item);
        await Navigation.PopModalAsync();
    }
}
