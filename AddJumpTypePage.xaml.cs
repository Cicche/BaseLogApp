using BaseLogApp.Core.Models;
using BaseLogApp.Core.ViewModels;

namespace BaseLogApp.Views;

public partial class AddJumpTypePage : ContentPage
{
    private readonly JumpsViewModel _vm;
    private readonly CatalogItem? _editing;

    public AddJumpTypePage(JumpsViewModel vm, CatalogItem? editing = null)
    {
        InitializeComponent();
        _vm = vm;
        _editing = editing;

        if (_editing is not null)
        {
            Title = "Modifica Tipo salto";
            NameEntry.Text = _editing.Name;
            NotesEntry.Text = _editing.Notes;
            SaveButton.Text = "Salva modifiche";
            SeedDefaultsButton.IsVisible = false;
        }
    }

    private async void OnSaveClicked(object sender, EventArgs e)
    {
        var ok = _editing is null
            ? await _vm.AddJumpTypeAsync(NameEntry.Text ?? string.Empty, NotesEntry.Text)
            : await _vm.UpdateJumpTypeAsync(_editing.Id, NameEntry.Text ?? string.Empty, NotesEntry.Text);

        await DisplayAlert("Tipo salto", ok ? "Tipo salto salvato" : "Errore salvataggio", "OK");
        if (ok)
            await Navigation.PopModalAsync();
    }

    private async void OnSeedDefaultsClicked(object sender, EventArgs e)
    {
        await _vm.AddJumpTypeAsync("Low", "default");
        await _vm.AddJumpTypeAsync("Terminal", "default");
        await _vm.AddJumpTypeAsync("Subterminal", "default");
        await DisplayAlert("Tipo salto", "Default inseriti", "OK");
    }

    private async void OnCloseClicked(object sender, EventArgs e)
        => await Navigation.PopModalAsync();
}
