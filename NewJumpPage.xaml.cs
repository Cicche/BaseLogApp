using BaseLogApp.Core.Models;
using BaseLogApp.Core.ViewModels;
using System.Collections.ObjectModel;
using System.Globalization;

namespace BaseLogApp.Views;

public partial class NewJumpPage : ContentPage
{
    public Func<JumpListItem, Task<bool>>? SaveRequested;

    private readonly List<string> _allObjects;
    private readonly List<string> _allJumpTypes;
    private readonly ObservableCollection<string> _filteredObjects = new();
    private readonly ObservableCollection<string> _filteredJumpTypes = new();
    private string? _selectedPhotoPath;
    private readonly JumpListItem? _editing;
    private readonly JumpsViewModel _vm;

    public NewJumpPage(JumpsViewModel vm, int suggestedJumpNumber, IReadOnlyList<string> knownObjects, IReadOnlyList<string> knownJumpTypes, JumpListItem? editing = null)
    {
        InitializeComponent();

        _vm = vm;
        _editing = editing;
        _allObjects = knownObjects.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();
        _allJumpTypes = knownJumpTypes.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();

        ObjectSuggestionsView.ItemsSource = _filteredObjects;
        JumpTypeSuggestionsView.ItemsSource = _filteredJumpTypes;

        if (_editing is null)
        {
            var now = DateTime.Now;
            DatePicker.Date = now.Date;
            TimePicker.Time = now.TimeOfDay;
            NumberEntry.Text = suggestedJumpNumber.ToString();
            Title = "Nuovo salto";
        }
        else
        {
            Title = "Modifica salto";
            NumberEntry.Text = _editing.NumeroSalto.ToString();
            ObjectEntry.Text = _editing.Oggetto;
            TypeEntry.Text = _editing.TipoSalto;
            NotesEditor.Text = _editing.Note;

            if (DateTime.TryParseExact(_editing.Data, new[] { "dd/MM/yyyy HH:mm", "dd/MM/yyyy" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            {
                DatePicker.Date = parsed.Date;
                TimePicker.Time = parsed.TimeOfDay;
            }
        }
    }

    private void OnObjectTextChanged(object? sender, TextChangedEventArgs e) => FilterSuggestions(e.NewTextValue, _allObjects, _filteredObjects, ObjectSuggestionsView);
    private void OnJumpTypeTextChanged(object? sender, TextChangedEventArgs e) => FilterSuggestions(e.NewTextValue, _allJumpTypes, _filteredJumpTypes, JumpTypeSuggestionsView);

    private static void FilterSuggestions(string? query, List<string> source, ObservableCollection<string> target, CollectionView list)
    {
        var text = (query ?? string.Empty).Trim();
        target.Clear();
        if (text.Length < 1) { list.IsVisible = false; return; }
        foreach (var item in source.Where(x => x.Contains(text, StringComparison.OrdinalIgnoreCase)).Take(8)) target.Add(item);
        list.IsVisible = target.Count > 0;
    }

    private void OnObjectSuggestionSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is string selected) ObjectEntry.Text = selected;
        ObjectSuggestionsView.SelectedItem = null; ObjectSuggestionsView.IsVisible = false;
    }

    private void OnJumpTypeSuggestionSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is string selected) TypeEntry.Text = selected;
        JumpTypeSuggestionsView.SelectedItem = null; JumpTypeSuggestionsView.IsVisible = false;
    }

    private async void OnOpenMapClicked(object sender, EventArgs e)
    {
        var (lat, lon) = await _vm.GetObjectCoordinatesAsync(ObjectEntry.Text);
        if (lat.HasValue && lon.HasValue)
            await Launcher.OpenAsync($"https://www.google.com/maps/search/?api=1&query={lat.Value.ToString(CultureInfo.InvariantCulture)},{lon.Value.ToString(CultureInfo.InvariantCulture)}");
        else
            await DisplayAlert("Mappa", "Coordinate non disponibili per questo object.", "OK");
    }

    private async void OnPickPhotoClicked(object sender, EventArgs e)
    {
        try
        {
            var file = await MediaPicker.Default.PickPhotoAsync();
            if (file is null) return;
            _selectedPhotoPath = file.FullPath;
            PhotoPathLabel.Text = Path.GetFileName(_selectedPhotoPath);
        }
        catch (Exception ex)
        {
            await DisplayAlert("Foto", $"Impossibile selezionare foto: {ex.Message}", "OK");
        }
    }

    private async Task<byte[]?> LoadSelectedPhotoBytesAsync()
        => string.IsNullOrWhiteSpace(_selectedPhotoPath) || !File.Exists(_selectedPhotoPath) ? null : await File.ReadAllBytesAsync(_selectedPhotoPath);

    private async void OnCancelClicked(object sender, EventArgs e) => await Navigation.PopModalAsync();

    private async void OnSaveClicked(object sender, EventArgs e)
    {
        if (!int.TryParse(NumberEntry.Text, out var jumpNumber))
        {
            await DisplayAlert("Dato non valido", "Inserisci un numero salto valido.", "OK");
            return;
        }

        var composedDate = DatePicker.Date.Date.Add(TimePicker.Time);
        var coords = await _vm.GetObjectCoordinatesAsync(ObjectEntry.Text);

        var item = new JumpListItem
        {
            Id = _editing?.Id ?? jumpNumber,
            NumeroSalto = jumpNumber,
            OriginalNumeroSalto = _editing?.NumeroSalto ?? jumpNumber,
            IsEdit = _editing is not null,
            Data = composedDate.ToString("dd/MM/yyyy HH:mm"),
            Oggetto = ObjectEntry.Text,
            TipoSalto = TypeEntry.Text,
            Note = NotesEditor.Text,
            ObjectPhotoPath = _selectedPhotoPath ?? _editing?.ObjectPhotoPath,
            ObjectPhotoBlob = _editing?.ObjectPhotoBlob,
            JumpPhotoBlob = _editing?.JumpPhotoBlob,
            NewPhotoBytes = await LoadSelectedPhotoBytesAsync(),
            Latitude = coords.Latitude?.ToString(CultureInfo.InvariantCulture),
            Longitude = coords.Longitude?.ToString(CultureInfo.InvariantCulture)
        };

        if (SaveRequested is null)
        {
            await DisplayAlert("Errore", "Salvataggio non disponibile.", "OK");
            return;
        }

        var saved = await SaveRequested(item);
        if (saved) await Navigation.PopModalAsync();
    }
}
