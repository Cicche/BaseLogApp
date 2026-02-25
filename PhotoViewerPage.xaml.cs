using BaseLogApp.Core.Models;

namespace BaseLogApp.Views;

public partial class PhotoViewerPage : ContentPage
{
    private readonly JumpListItem _item;

    public PhotoViewerPage(JumpListItem item)
    {
        InitializeComponent();
        _item = item;
        PreviewImage.Source = item.ObjectPhotoSource;
    }

    private async void OnSaveClicked(object sender, EventArgs e)
    {
        try
        {
            var bytes = _item.JumpPhotoBlob ?? _item.ObjectPhotoBlob;
            if (bytes is not { Length: > 0 })
            {
                await DisplayAlert("Foto", "Nessun blob da salvare (forse immagine da path esterno).", "OK");
                return;
            }

            var target = Path.Combine(FileSystem.CacheDirectory, $"jump_{_item.NumeroSalto}_{DateTime.Now:yyyyMMddHHmmss}.jpg");
            await File.WriteAllBytesAsync(target, bytes);
            await DisplayAlert("Salvata", $"File scritto in: {target}", "OK");
        }
        catch (Exception ex)
        {
            await DisplayAlert("Errore", ex.Message, "OK");
        }
    }

    private async void OnCopyClicked(object sender, EventArgs e)
    {
        var path = _item.ObjectPhotoPath ?? "(nessun path disponibile)";
        await Clipboard.Default.SetTextAsync(path);
        await DisplayAlert("Copiato", "Percorso copiato negli appunti.", "OK");
    }

    private async void OnCloseClicked(object sender, EventArgs e)
    {
        await Navigation.PopModalAsync();
    }
}
