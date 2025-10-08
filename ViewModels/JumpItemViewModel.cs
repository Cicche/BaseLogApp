using BaseLogApp.Data;
using BaseLogApp.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Maui.Controls;
using System.Globalization;
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

    public string? Notes => Model.Notes;

    // Dati dell'oggetto/exit (ZOBJECT) caricati dopo
    public ExitObject? Exit { get; private set; }

    public string ExitName => Exit?.Name ?? string.Empty;
    public string LocationName => Exit?.Region ?? string.Empty;
    public double? Latitude => Exit?.Latitude;
    public double? Longitude => Exit?.Longitude;
    public bool HasCoordinates => Latitude.HasValue && Longitude.HasValue;

    public ImageSource? Thumbnail { get; private set; }

    public DateTime? JumpDateUtc
    {
        get
        {
            if (Model.JumpDateRaw is null) return null;

            var t = Model.JumpDateRaw.Value;

            // Heuristica: se e "Apple epoch" (secondi dal 2001-01-01)
            var appleEpoch = new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc);

            // Se t e nell'ordine dei secondi (es. 760000000) usa direttamente
            if (t > 100000 && t < 5000000000)
                return appleEpoch.AddSeconds(t);

            // Se sono millisecondi
            if (t >= 5000000000)
                return appleEpoch.AddMilliseconds(t);

            // Fallback: prova Unix epoch secondi
            if (t > 0)
                return DateTimeOffset.FromUnixTimeSeconds(t).UtcDateTime;

            return null;
        }
    }

    public async Task HydrateAsync(IJumpsRepository repo)
    {
        Exit = null;
        if (Model.ObjectId is int oid && oid > 0)
            Exit = await repo.GetObjectAsync(oid);

        byte[]? bytes = null;
        if (Exit is not null)
            bytes = await repo.GetObjectThumbnailAsync(Exit.Id);

        Thumbnail = (bytes is { Length: > 0 })
            ? ImageSource.FromStream(() => new MemoryStream(bytes))
            : null;

        OnPropertyChanged(nameof(Exit));
        OnPropertyChanged(nameof(ExitName));
        OnPropertyChanged(nameof(LocationName));
        OnPropertyChanged(nameof(Latitude));
        OnPropertyChanged(nameof(Longitude));
        OnPropertyChanged(nameof(HasCoordinates));
        OnPropertyChanged(nameof(Thumbnail));
    }

    public bool Matches(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return true;

        var comparison = StringComparison.CurrentCultureIgnoreCase;

        if (JumpNumber is int number &&
            number.ToString(CultureInfo.CurrentCulture).Contains(query, comparison))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(ExitName) && ExitName.Contains(query, comparison))
            return true;

        if (!string.IsNullOrWhiteSpace(LocationName) && LocationName.Contains(query, comparison))
            return true;

        if (!string.IsNullOrWhiteSpace(Notes) && Notes.Contains(query, comparison))
            return true;

        if (JumpDateUtc is DateTime date)
        {
            var culture = CultureInfo.CurrentCulture;
            var dateText = date.ToLocalTime().ToString("d", culture);
            if (dateText.Contains(query, comparison))
                return true;
        }

        return false;
    }
}
