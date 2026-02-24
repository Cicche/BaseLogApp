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
    public string? Position { get; set; }
    public string? Latitude { get; set; }
    public string? Longitude { get; set; }
}
