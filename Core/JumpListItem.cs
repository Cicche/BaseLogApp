using SQLite;

namespace BaseLogApp.Core.Models
{
    public class JumpListItem
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
        public string? Latitude { get; set; }
        public string? Longitude { get; set; }

        public ImageSource ObjectPhotoSource
        {
            get
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
}
