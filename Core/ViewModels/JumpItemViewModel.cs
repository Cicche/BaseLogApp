using CommunityToolkit.Mvvm.ComponentModel;
using BaseLogApp.Models;
using BaseLogApp.Data;
using Microsoft.Maui.Controls; // per ImageSource
using System.IO;

namespace BaseLogApp.ViewModels;

public partial class JumpItemViewModel : ObservableObject
{
    public JumpItemViewModel(Jump model)
    {
        Model = model ?? throw new ArgumentNullException(nameof(model));
    }

    public Jump Model { get; }

    [ObservableProperty]
    private bool isExpanded;

    // Dati base dal log (ZLOGENTRY)
    public int Id => Model.Id;
    public int? JumpNumber => Model.JumpNumber;
    public DateTime? JumpDateUtc => Model.JumpDateUtc;
    public string? Notes => Model.Notes;

    // Dati dell’oggetto/exit (ZOBJECT) caricati dopo
    public ExitObject? Exit { get; private set; }

    public string ExitName => Exit?.Name ?? string.Empty;
    public string LocationName => Exit?.Region ?? string.Empty;
    public double? Latitude => Exit?.Latitude;
    public double? Longitude => Exit?.Longitude;
    public bool HasCoordinates => Latitude.HasValue && Longitude.HasValue;

    public ImageSource? Thumbnail { get; private set; }

    public async Task HydrateAsync(IJumpsRepository repo)
    {
        if (repo is null) return;

        if (Model.ObjectId is int oid)
            Exit = await repo.GetObjectAsync(oid);

        byte[]? bytes = null;
        if (Model.ObjectId is int objectId)
            bytes = await repo.GetObjectThumbnailAsync(objectId);

        if (bytes is not null && bytes.Length > 0)
            Thumbnail = ImageSource.FromStream(() => new MemoryStream(bytes));
        else
            Thumbnail = null;

        OnPropertyChanged(nameof(Exit));
        OnPropertyChanged(nameof(ExitName));
        OnPropertyChanged(nameof(LocationName));
        OnPropertyChanged(nameof(Latitude));
        OnPropertyChanged(nameof(Longitude));
        OnPropertyChanged(nameof(HasCoordinates));
        OnPropertyChanged(nameof(Thumbnail));
    }
}
