using System.IO;
using Microsoft.Maui.Controls;

namespace BaseLogApp.Core.Models;

public class CatalogItem
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Notes { get; set; }
}

public sealed class ObjectCatalogItem : CatalogItem
{
    public string? ObjectType { get; set; }
    public string? Description { get; set; }
    public string? HeightMeters { get; set; }
    public string? HeightUnit { get; set; }
    public string? Region { get; set; }
    public string? Position { get; set; }
    public string? Latitude { get; set; }
    public string? Longitude { get; set; }
    public byte[]? PhotoBlob { get; set; }
    public int JumpCount { get; set; }

    public bool HasPhoto => PhotoBlob is { Length: > 0 };

    public ImageSource? PhotoSource
        => HasPhoto ? ImageSource.FromStream(() => new MemoryStream(PhotoBlob!)) : null;

    public string TypeInitial
    {
        get
        {
            var raw = (ObjectType ?? string.Empty).Trim();
            if (raw.Length == 0)
                return "O";

            var first = char.ToUpperInvariant(raw[0]);
            return first is 'B' or 'A' or 'S' or 'E' ? first.ToString() : "O";
        }
    }
}
