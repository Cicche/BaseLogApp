using SQLite;

namespace BaseLogApp.Models;

public class Jump
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    public string? ExitName { get; set; }
    public string? ObjectName { get; set; }

    // Conservata in UTC (TEXT o INTEGER epoch). Visualizzazione con converter.
    public DateTime? JumpDateUtc { get; set; }

    public string? LocationName { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }

    public string? PhotoPath { get; set; }
    public string? Notes { get; set; }
    public string? GearSetup { get; set; }

    // Se lo schema reale ha int?, cambiare questi in int?
    public string? ExitHeight { get; set; }
    public string? DelaySeconds { get; set; }

    // altri flag/colonne preesistenti…
}
