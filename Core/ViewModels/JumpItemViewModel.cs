using CommunityToolkit.Mvvm.ComponentModel;
using BaseLog.Models;
using Microsoft.Maui.Devices.Sensors;

namespace BaseLog.ViewModels;

public partial class JumpItemViewModel : ObservableObject
{
    private readonly Jump _model;

    public JumpItemViewModel(Jump model)
    {
        _model = model;
        MapLocation = (_model.Latitude.HasValue && _model.Longitude.HasValue)
            ? new Location(_model.Latitude!.Value, _model.Longitude!.Value)
            : null;
    }

    [ObservableProperty] private bool isExpanded;
    [ObservableProperty] private bool showInlineMap;

    public int Id => _model.Id;
    public string ExitName => _model.ExitName ?? string.Empty;
    public string ObjectName => _model.ObjectName ?? string.Empty;
    public DateTime? JumpDateUtc => _model.JumpDateUtc;
    public string LocationName => _model.LocationName ?? string.Empty;

    public string? PhotoPath => _model.PhotoPath;
    public bool HasPhoto => !string.IsNullOrWhiteSpace(_model.PhotoPath);

    public string? Notes => _model.Notes;
    public string? GearSetup => _model.GearSetup;

    // Se nello schema reali sono int?, tipizzare int? nel model e nel binding usare StringFormat
    public string? ExitHeight => _model.ExitHeight;
    public string? DelaySeconds => _model.DelaySeconds;

    public double? Latitude => _model.Latitude;
    public double? Longitude => _model.Longitude;
    public bool HasCoordinates => _model.Latitude.HasValue && _model.Longitude.HasValue;
    public Location? MapLocation { get; }

    public void RefreshFrom(Jump updated)
    {
        _model.ExitName = updated.ExitName;
        _model.ObjectName = updated.ObjectName;
        _model.JumpDateUtc = updated.JumpDateUtc;
        _model.LocationName = updated.LocationName;
        _model.PhotoPath = updated.PhotoPath;
        _model.Notes = updated.Notes;
        _model.GearSetup = updated.GearSetup;
        _model.ExitHeight = updated.ExitHeight;
        _model.DelaySeconds = updated.DelaySeconds;

        OnPropertyChanged(nameof(ExitName));
        OnPropertyChanged(nameof(ObjectName));
        OnPropertyChanged(nameof(JumpDateUtc));
        OnPropertyChanged(nameof(LocationName));
        OnPropertyChanged(nameof(PhotoPath));
        OnPropertyChanged(nameof(HasPhoto));
        OnPropertyChanged(nameof(Notes));
        OnPropertyChanged(nameof(GearSetup));
        OnPropertyChanged(nameof(ExitHeight));
        OnPropertyChanged(nameof(DelaySeconds));
    }
}
