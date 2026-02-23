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
        public string? Latitude { get; set; }
        public string? Longitude { get; set; }

        public string ObjectPhotoSource => string.IsNullOrWhiteSpace(ObjectPhotoPath)
            ? "dotnet_bot.png"
            : ObjectPhotoPath;
    }
}
