using SQLite;

namespace BaseLogApp.Models;

// Tabella oggetti/exit
[Table("ZOBJECT")]
public class ExitObject
{
    [PrimaryKey, Column("Z_PK")]
    public int Id { get; set; }

    [Column("ZNAME")]
    public string? Name { get; set; }

    [Column("ZREGION")]
    public string? Region { get; set; }

    [Column("ZLATITUDE")]
    public double? Latitude { get; set; }

    [Column("ZLONGITUDE")]
    public double? Longitude { get; set; }

    [Column("ZHEIGHT")]
    public int? Height { get; set; }
}
