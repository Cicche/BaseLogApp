using SQLite;
using System.Reflection;

namespace BaseLogApp.Models;

// Tabella delle righe di log
[Table("ZLOGENTRY")]
public class Jump
{
    [PrimaryKey, Column("Z_PK")] public int Id { get; set; }
    [Column("ZJUMPNUMBER")] public int? JumpNumber { get; set; }

    // Sostituisci questo:
   // [Column("ZDATE")] public DateTime? JumpDateUtc { get; set; }

    // Con uno di questi a seconda del DB (usa long? finché non verifichiamo):
    [Column("ZDATE")] public long? JumpDateRaw { get; set; } // epoch?
    // in alternativa: public string? JumpDateRaw { get; set; }

    [Column("ZNOTES")] public string? Notes { get; set; }
    [Column("ZOBJECT")] public int? ObjectId { get; set; }
    [Column("ZDELAY")] public int? DelaySeconds { get; set; }

    [Column("ZJUMPTYPE")] public int? JumpTypeId { get; set; }
    [Column("ZDEPLOYMENTTYPE")] public int? DeploymentTypeId { get; set; }
    [Column("ZSLIDERTYPE")] public int? SliderTypeId { get; set; }
}
