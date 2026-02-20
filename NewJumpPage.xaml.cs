using BaseLogApp.Core.Models;
using System.Collections.ObjectModel;

namespace BaseLogApp.Views;

public partial class NewJumpPage : ContentPage
{
    public event EventHandler<JumpListItem>? JumpSaved;

    private readonly List<string> _allObjects;
    private readonly ObservableCollection<string> _filteredObjects = new();

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
            Note = NotesEditor.Text
        };

        JumpSaved?.Invoke(this, item);
        await Navigation.PopModalAsync();
    }
}
