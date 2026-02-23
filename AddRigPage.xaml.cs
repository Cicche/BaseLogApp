using BaseLogApp.Core.ViewModels;

namespace BaseLogApp.Views;

public partial class AddRigPage : ContentPage
{
    private readonly JumpsViewModel _vm;

    public AddRigPage(JumpsViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
    }

    private async void OnSaveClicked(object sender, EventArgs e)
    {
        var ok = await _vm.AddRigAsync(RigNameEntry.Text ?? string.Empty, RigDescriptionEntry.Text);
        await DisplayAlert("Rig", ok ? "Rig salvato" : "Errore salvataggio rig", "OK");
    }

    private async void OnCloseClicked(object sender, EventArgs e)
        => await Navigation.PopModalAsync();
}
