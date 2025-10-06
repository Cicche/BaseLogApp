using SQLite;

namespace BaseLogApp.Models;

// Tabella delle righe di log
[Table("ZLOGENTRY")]
public class Jump
{
    [PrimaryKey, Column("Z_PK")]
    public int Id { get; set; }

    [Column("ZJUMPNUMBER")]
    public int? JumpNumber { get; set; }

    [Column("ZDATE")]
    public DateTime? JumpDateUtc { get; set; }

    [Column("ZNOTES")]
    public string? Notes { get; set; }

    [Column("ZOBJECT")]
    public int? ObjectId { get; set; }

    [Column("ZDELAY")]
    public int? DelaySeconds { get; set; }

    // campi aggiuntivi se/quando servono:
    [Column("ZJUMPTYPE")] public int? JumpTypeId { get; set; }
    [Column("ZDEPLOYMENTTYPE")] public int? DeploymentTypeId { get; set; }
    [Column("ZSLIDERTYPE")] public int? SliderTypeId { get; set; }
}
