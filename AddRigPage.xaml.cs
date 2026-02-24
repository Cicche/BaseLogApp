using BaseLogApp.Core.Models;
using BaseLogApp.Core.ViewModels;

namespace BaseLogApp.Views;

public partial class AddRigPage : ContentPage
{
    private readonly JumpsViewModel _vm;
    private readonly CatalogItem? _editing;

    public AddRigPage(JumpsViewModel vm, CatalogItem? editing = null)
    {
        InitializeComponent();
        _vm = vm;
        _editing = editing;

        if (_editing is not null)
        {
            Title = "Modifica Rig";
            RigNameEntry.Text = _editing.Name;
            RigDescriptionEntry.Text = _editing.Notes;
            SaveButton.Text = "Salva modifiche";
        }
    }

    private async void OnSaveClicked(object sender, EventArgs e)
    {
        var ok = _editing is null
            ? await _vm.AddRigAsync(RigNameEntry.Text ?? string.Empty, RigDescriptionEntry.Text)
            : await _vm.UpdateRigAsync(_editing.Id, RigNameEntry.Text ?? string.Empty, RigDescriptionEntry.Text);

        await DisplayAlert("Rig", ok ? "Rig salvato" : "Errore salvataggio rig", "OK");
        if (ok)
            await Navigation.PopModalAsync();
    }

    private async void OnCloseClicked(object sender, EventArgs e)
        => await Navigation.PopModalAsync();
}
