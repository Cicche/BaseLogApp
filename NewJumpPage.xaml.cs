using BaseLogApp.Core.Models;

namespace BaseLogApp.Views;

public partial class NewJumpPage : ContentPage
{
    public event EventHandler<JumpListItem>? JumpSaved;

    public NewJumpPage()
    {
        InitializeComponent();
        DatePicker.Date = DateTime.Today;
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
