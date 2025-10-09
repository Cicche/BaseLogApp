using SQLite;

namespace BaseLogApp.Models;

[Table("ZJUMPTYPE")]
public class JumpType
{
    [PrimaryKey, Column("Z_PK")]
    public int Id { get; set; }

    [Column("ZNAME")]
    public string? Name { get; set; }
}
