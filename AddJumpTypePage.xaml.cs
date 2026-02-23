using BaseLogApp.Core.ViewModels;

namespace BaseLogApp.Views;

public partial class AddJumpTypePage : ContentPage
{
    private readonly JumpsViewModel _vm;

    public AddJumpTypePage(JumpsViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
    }

    private async void OnSaveClicked(object sender, EventArgs e)
    {
        var ok = await _vm.AddJumpTypeAsync(NameEntry.Text ?? string.Empty, NotesEntry.Text);
        await DisplayAlert("Tipo salto", ok ? "Tipo salto salvato" : "Errore salvataggio", "OK");
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
