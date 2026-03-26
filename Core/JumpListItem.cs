using SQLite;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace BaseLogApp.Core.Models
{
    public class JumpListItem : INotifyPropertyChanged
    {
        [PrimaryKey]
        public int Id { get; set; }
        public int NumeroSalto { get; set; }
        public string Data { get; set; } = "";
        public string? Oggetto { get; set; }
        public string? TipoSalto { get; set; }
        public string? Note { get; set; }
        public string? ObjectPhotoPath { get; set; }
        public byte[]? ObjectPhotoBlob { get; set; }
        public byte[]? JumpPhotoBlob { get; set; }
        public byte[]? NewPhotoBytes { get; set; }
        public bool RemoveJumpPhoto { get; set; }
        public string? Latitude { get; set; }
        public string? Longitude { get; set; }
        public int? DelaySeconds { get; set; }
        public int? HeadingDegrees { get; set; }
        public List<string> RigNames { get; set; } = new();

        private bool _isExpanded;
        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                if (_isExpanded == value) return;
                _isExpanded = value;
                OnPropertyChanged();
            }
        }

        public bool IsEdit { get; set; }
        public int OriginalNumeroSalto { get; set; }

        public string TimeDisplay
        {
            get
            {
                if (DateTime.TryParseExact(Data, new[] { "dd/MM/yyyy HH:mm", "dd/MM/yyyy" }, null, System.Globalization.DateTimeStyles.None, out var dt))
                    return dt.ToString("HH:mm");
                return "";
            }
        }

        public string DateOnlyDisplay
        {
            get
            {
                if (DateTime.TryParseExact(Data, new[] { "dd/MM/yyyy HH:mm", "dd/MM/yyyy" }, null, System.Globalization.DateTimeStyles.None, out var dt))
                    return dt.ToString("dd/MM/yyyy");
                return Data;
            }
        }


        public string DelayDisplay => DelaySeconds?.ToString() ?? "-";
        public string HeadingDisplay
            => HeadingDegrees.HasValue
                ? (HeadingDegrees.Value > 0
                    ? $"+{HeadingDegrees.Value} deg"
                    : $"{HeadingDegrees.Value} deg")
                : "-";
        public string RigDisplay => RigNames.Count == 0 ? "-" : string.Join(", ", RigNames);

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private ImageSource? _cachedPhotoSource;
        public ImageSource ObjectPhotoSource => _cachedPhotoSource ??= CreatePhotoSource();

        private ImageSource CreatePhotoSource()
        {
            if (JumpPhotoBlob is { Length: > 0 })
                return ImageSource.FromStream(() => new MemoryStream(JumpPhotoBlob));

            if (ObjectPhotoBlob is { Length: > 0 })
                return ImageSource.FromStream(() => new MemoryStream(ObjectPhotoBlob));

            if (!string.IsNullOrWhiteSpace(ObjectPhotoPath))
                return ImageSource.FromFile(ObjectPhotoPath);

            return ImageSource.FromFile("dotnet_bot.png");
        }
    }
}
