using BaseLogApp.Core.Diagnostics;
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
    private List<string> _allRigs = [];
    private string? _selectedRig;
    private readonly ObservableCollection<string> _filteredObjects = new();
    private readonly ObservableCollection<string> _filteredJumpTypes = new();
    private string? _selectedPhotoPath;
    private bool _removeJumpPhoto;
    private bool _hasPersistedJumpPhoto;
    private readonly JumpListItem? _editing;
    private readonly JumpListItem? _template;
    private readonly JumpsViewModel _vm;
    private int _openingDirectionSign;
    private bool _isCompactDateTimeLayout;

    private string GetLogPath() => _vm.GetCurrentDbPath() + ".log";

    public NewJumpPage(
        JumpsViewModel vm,
        int suggestedJumpNumber,
        IReadOnlyList<string> knownObjects,
        IReadOnlyList<string> knownJumpTypes,
        JumpListItem? editing = null,
        JumpListItem? template = null)
    {
        InitializeComponent();

        _vm = vm;
        _editing = editing;
        _template = template;
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
            ApplyOpeningFromSignedValue(0);
            Title = "Nuovo salto";
            ApplyTemplateDefaults();
            UpdatePhotoPreview();
            UpdateRigLabel();
        }
        else
        {
            Title = "Modifica salto";
            NumberEntry.Text = _editing.NumeroSalto.ToString();
            ObjectEntry.Text = _editing.Oggetto;
            TypeEntry.Text = _editing.TipoSalto;
            NotesEditor.Text = _editing.Note;
            DelayEntry.Text = _editing.DelaySeconds?.ToString(CultureInfo.InvariantCulture);
            _selectedRig = _editing.RigNames.FirstOrDefault();
            ApplyOpeningFromSignedValue(_editing.HeadingDegrees);

            if (DateTime.TryParseExact(_editing.Data, new[] { "dd/MM/yyyy HH:mm", "dd/MM/yyyy" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            {
                DatePicker.Date = parsed.Date;
                TimePicker.Time = parsed.TimeOfDay;
            }

            if (_editing.JumpPhotoBlob is { Length: > 0 })
            {
                _hasPersistedJumpPhoto = true;
                JumpPhotoPreview.Source = ImageSource.FromStream(() => new MemoryStream(_editing.JumpPhotoBlob));
                PhotoPathLabel.Text = "Foto salto salvata";
            }
            else
            {
                SetObjectFallbackPreview();
            }

            UpdatePhotoPreview();
            UpdateRigLabel();
            DeleteButton.IsVisible = true;
        }
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_allRigs.Count == 0)
            _allRigs = (await _vm.GetRigNamesAsync()).ToList();
        UpdateRigLabel();
        UpdateDateTimeLayout(Width);
    }

    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);
        UpdateDateTimeLayout(width);
    }

    private void OnObjectTextChanged(object? sender, TextChangedEventArgs e) => FilterSuggestions(e.NewTextValue, _allObjects, _filteredObjects, ObjectSuggestionsView);
    private void OnJumpTypeTextChanged(object? sender, TextChangedEventArgs e) => FilterSuggestions(e.NewTextValue, _allJumpTypes, _filteredJumpTypes, JumpTypeSuggestionsView);
    private void OnOpeningTextChanged(object? sender, TextChangedEventArgs e) => UpdateOpeningPreview();

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
            _removeJumpPhoto = false;
            PhotoPathLabel.Text = Path.GetFileName(_selectedPhotoPath);
            JumpPhotoPreview.Source = ImageSource.FromFile(_selectedPhotoPath);
            UpdatePhotoPreview();
        }
        catch (Exception ex)
        {
            AppLog.Error(GetLogPath(), LogCategories.RuntimeError, nameof(NewJumpPage), nameof(OnPickPhotoClicked), "Pick photo failed.", ex: ex);
            await DisplayAlert("Foto", $"Impossibile selezionare foto: {ex.Message}", "OK");
        }
    }

    private async Task<byte[]?> LoadSelectedPhotoBytesAsync()
        => string.IsNullOrWhiteSpace(_selectedPhotoPath) || !File.Exists(_selectedPhotoPath) ? null : await File.ReadAllBytesAsync(_selectedPhotoPath);

    private async void OnPickRigClicked(object sender, EventArgs e)
    {
        if (_allRigs.Count == 0)
            _allRigs = (await _vm.GetRigNamesAsync()).ToList();

        if (_allRigs.Count == 0)
        {
            await DisplayAlert("Rig", "Nessun rig disponibile. Creane uno nella tab Gear.", "OK");
            return;
        }

        var options = _allRigs.OrderBy(x => x).ToList();
        options.Insert(0, "Nessuno");
        var selected = await DisplayActionSheet("Seleziona rig", "Annulla", null, options.ToArray());
        if (string.IsNullOrWhiteSpace(selected) || selected == "Annulla")
            return;

        _selectedRig = selected == "Nessuno" ? null : selected;
        UpdateRigLabel();
    }

    private void OnRemovePhotoClicked(object sender, EventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(_selectedPhotoPath))
        {
            _selectedPhotoPath = null;
            if (_hasPersistedJumpPhoto && _editing?.JumpPhotoBlob is { Length: > 0 })
            {
                JumpPhotoPreview.Source = ImageSource.FromStream(() => new MemoryStream(_editing.JumpPhotoBlob));
                PhotoPathLabel.Text = "Foto salto salvata";
            }
            else
            {
                SetObjectFallbackPreview();
            }
            _removeJumpPhoto = false;
            UpdatePhotoPreview();
            return;
        }

        if (_hasPersistedJumpPhoto)
        {
            _hasPersistedJumpPhoto = false;
            _removeJumpPhoto = true;
            SetObjectFallbackPreview();
            PhotoPathLabel.Text = "Foto salto rimossa (verra eliminata al salvataggio)";
        }

        UpdatePhotoPreview();
    }

    private async void OnCancelClicked(object sender, EventArgs e) => await Navigation.PopModalAsync();

    private async void OnSaveClicked(object sender, EventArgs e)
    {
        try
        {
            var jumpNumber = ParseNullableInt(NumberEntry.Text) ?? -1;
            var composedDate = DatePicker.Date.Date.Add(TimePicker.Time);
            var coords = await _vm.GetObjectCoordinatesAsync(ObjectEntry.Text);
            var headingValue = ParseSignedOpening();
            if (!headingValue.IsValid)
            {
                await DisplayAlert("Apertura", headingValue.ErrorMessage!, "OK");
                return;
            }

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
                NewPhotoBytes = _removeJumpPhoto ? null : await LoadSelectedPhotoBytesAsync(),
                RemoveJumpPhoto = _removeJumpPhoto,
                Latitude = coords.Latitude?.ToString(CultureInfo.InvariantCulture),
                Longitude = coords.Longitude?.ToString(CultureInfo.InvariantCulture),
                DelaySeconds = ParseNullableInt(DelayEntry.Text),
                HeadingDegrees = headingValue.Value,
                RigNames = string.IsNullOrWhiteSpace(_selectedRig) ? [] : [_selectedRig]
            };

            if (SaveRequested is null)
            {
                await DisplayAlert("Errore", "Salvataggio non disponibile.", "OK");
                return;
            }

            var saved = await SaveRequested(item);
            if (saved)
                await Navigation.PopModalAsync();
        }
        catch (Exception ex)
        {
            AppLog.Error(GetLogPath(), LogCategories.RuntimeError, nameof(NewJumpPage), nameof(OnSaveClicked), "Save jump failed.", ex: ex);
            await DisplayAlert("Errore", $"Salvataggio non riuscito: {ex.Message}", "OK");
        }
    }

    private void OnLeftDirectionClicked(object sender, EventArgs e)
    {
        _openingDirectionSign = -1;
        UpdateOpeningDirectionButtons();
        UpdateOpeningPreview();
    }

    private void OnRightDirectionClicked(object sender, EventArgs e)
    {
        _openingDirectionSign = 1;
        UpdateOpeningDirectionButtons();
        UpdateOpeningPreview();
    }

    private static int? ParseNullableInt(string? text)
        => int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? Math.Max(0, value) : null;

    private void UpdatePhotoPreview()
    {
        var hasPreview = JumpPhotoPreview.Source is not null;
        JumpPhotoPreview.IsVisible = hasPreview;
        RemovePhotoButton.IsVisible = !string.IsNullOrWhiteSpace(_selectedPhotoPath) || _hasPersistedJumpPhoto;
    }

    private void SetObjectFallbackPreview()
    {
        if (_editing?.ObjectPhotoBlob is { Length: > 0 })
        {
            JumpPhotoPreview.Source = ImageSource.FromStream(() => new MemoryStream(_editing.ObjectPhotoBlob));
            PhotoPathLabel.Text = "Nessuna foto salto (anteprima object)";
            return;
        }

        if (!string.IsNullOrWhiteSpace(_editing?.ObjectPhotoPath) && File.Exists(_editing.ObjectPhotoPath))
        {
            JumpPhotoPreview.Source = ImageSource.FromFile(_editing.ObjectPhotoPath);
            PhotoPathLabel.Text = "Nessuna foto salto (anteprima object)";
            return;
        }

        JumpPhotoPreview.Source = null;
        PhotoPathLabel.Text = "Nessuna foto salto";
    }

    private void UpdateRigLabel()
        => RigValueLabel.Text = string.IsNullOrWhiteSpace(_selectedRig) ? "Rig: -" : $"Rig: {_selectedRig}";

    private void UpdateDateTimeLayout(double width)
    {
        if (width <= 0)
            return;

        var compact = width < 500;
        if (compact == _isCompactDateTimeLayout && DateTimeGrid.RowDefinitions.Count > 0)
            return;

        _isCompactDateTimeLayout = compact;
        DateTimeGrid.RowDefinitions.Clear();
        DateTimeGrid.ColumnDefinitions.Clear();

        if (compact)
        {
            DateTimeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });
            DateTimeGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            DateTimeGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            Grid.SetRow(DatePicker, 0);
            Grid.SetColumn(DatePicker, 0);
            Grid.SetRow(TimePicker, 1);
            Grid.SetColumn(TimePicker, 0);
            TimePicker.HorizontalOptions = LayoutOptions.Start;
            TimePicker.WidthRequest = 160;
            return;
        }

        DateTimeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });
        DateTimeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        DateTimeGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        Grid.SetRow(DatePicker, 0);
        Grid.SetColumn(DatePicker, 0);
        Grid.SetRow(TimePicker, 0);
        Grid.SetColumn(TimePicker, 1);
        TimePicker.HorizontalOptions = LayoutOptions.End;
        TimePicker.WidthRequest = 160;
    }

    private void ApplyTemplateDefaults()
    {
        if (_template is null)
            return;

        ObjectEntry.Text = _template.Oggetto;
        TypeEntry.Text = _template.TipoSalto;
        NotesEditor.Text = _template.Note;
        DelayEntry.Text = _template.DelaySeconds?.ToString(CultureInfo.InvariantCulture);
        _selectedRig = _template.RigNames.FirstOrDefault();
        ApplyOpeningFromSignedValue(_template.HeadingDegrees);
    }

    private void ApplyOpeningFromSignedValue(int? signed)
    {
        var value = signed ?? 0;
        var magnitude = Math.Abs(value);
        OpeningEntry.Text = magnitude == 0 ? string.Empty : magnitude.ToString(CultureInfo.InvariantCulture);
        _openingDirectionSign = value switch
        {
            < 0 => -1,
            > 0 => 1,
            _ => 0
        };
        UpdateOpeningDirectionButtons();
        UpdateOpeningPreview();
    }

    private (bool IsValid, int? Value, string? ErrorMessage) ParseSignedOpening()
    {
        var raw = (OpeningEntry.Text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(raw))
            return (true, null, null);

        if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var magnitude))
            return (false, null, "Inserisci un valore numerico valido.");

        if (magnitude < 0 || magnitude > 9999)
            return (false, null, "Il valore apertura deve essere tra 0 e 9999.");

        if (magnitude == 0)
            return (true, 0, null);

        if (_openingDirectionSign == 0)
            return (false, null, "Seleziona direzione SX o DX.");

        var signed = _openingDirectionSign < 0 ? -magnitude : magnitude;
        return (true, signed, null);
    }

    private void UpdateOpeningDirectionButtons()
    {
        LeftDirectionButton.BackgroundColor = _openingDirectionSign < 0 ? Color.FromArgb("#2D8CFF") : Color.FromArgb("#27314E");
        RightDirectionButton.BackgroundColor = _openingDirectionSign > 0 ? Color.FromArgb("#2D8CFF") : Color.FromArgb("#27314E");
    }

    private void UpdateOpeningPreview()
    {
        var parsed = ParseSignedOpening();
        if (!parsed.IsValid || !parsed.Value.HasValue)
        {
            OpeningPreviewLabel.Text = "Apertura: -";
            return;
        }

        var value = parsed.Value.Value;
        var signed = value > 0 ? $"+{value}" : value.ToString(CultureInfo.InvariantCulture);
        OpeningPreviewLabel.Text = $"Apertura: {signed}°";
    }

    private async void OnDeleteClicked(object sender, EventArgs e)
    {
        if (_editing is null)
            return;

        var confirm = await DisplayAlert("Elimina salto", $"Eliminare il salto #{_editing.NumeroSalto}?", "Si", "No");
        if (!confirm)
            return;

        var ok = await _vm.DeleteJumpAsync(_editing);
        if (!ok)
        {
            await DisplayAlert("Salto", "Impossibile eliminare il salto.", "OK");
            return;
        }

        await DisplayAlert("Salto", "Salto eliminato.", "OK");
        await Navigation.PopModalAsync();
    }
}

