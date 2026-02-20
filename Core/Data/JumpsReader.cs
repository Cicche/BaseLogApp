using BaseLogApp.Core.Models;
using SQLite;
using System.Diagnostics;
using System.Globalization;

namespace BaseLogApp.Core.Data
{
    public interface IJumpsReader
    {
        Task<IReadOnlyList<JumpListItem>> GetJumpsAsync();
    }

    public class JumpsReader : IJumpsReader
    {
        private const string DefaultDbName = "BASELogbook.sqlite";
        private readonly string _dbPathWindows = @"C:\Temp\BASELogbook.sqlite";

        public async Task<IReadOnlyList<JumpListItem>> GetJumpsAsync()
        {
            var dbPath = ResolveDbPath();
            if (!File.Exists(dbPath))
                return Array.Empty<JumpListItem>();

            try
            {
                var conn = new SQLiteConnectionString(dbPath, false);
                var db = new SQLiteAsyncConnection(conn);

                const string sql = @"
                                SELECT
                                l.Z_PK AS Id,
                                l.ZJUMPNUMBER AS NumeroSalto,
                                CAST(l.ZDATE AS TEXT) AS ZDATE_TEXT,
                                o.ZNAME AS Oggetto,
                                jt.ZNAME AS TipoSalto,
                                l.ZNOTES AS Note
                                FROM ZLOGENTRY l
                                LEFT JOIN ZOBJECT o ON l.ZOBJECT = o.Z_PK
                                LEFT JOIN ZJUMPTYPE jt ON l.ZJUMPTYPE = jt.Z_PK
                                ORDER BY l.ZDATE DESC;";
                var rows = await db.QueryAsync<RawRow>(sql);

                Debug.WriteLine($"DB Exists={File.Exists(dbPath)}");
                Debug.WriteLine($"Rows={rows.Count}");

                return rows.Select(r => new JumpListItem
                {
                    Id = r.Id,
                    NumeroSalto = r.NumeroSalto,
                    Data = AppleSecondsToDisplayFromText(r.ZDATE_TEXT),
                    Oggetto = r.Oggetto,
                    TipoSalto = r.TipoSalto,
                    Note = r.Note
                }).ToList();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error reading jumps database at '{dbPath}': {ex.Message}");
                return Array.Empty<JumpListItem>();
            }
        }

        private string ResolveDbPath()
        {
#if WINDOWS
            var configuredPath = Environment.GetEnvironmentVariable("BASELOG_DB_PATH");
            if (!string.IsNullOrWhiteSpace(configuredPath))
                return configuredPath;

            return _dbPathWindows;
#else
            return Path.Combine(FileSystem.AppDataDirectory, DefaultDbName);
#endif
        }

        private sealed class RawRow
        {
            public int Id { get; set; }
            public int NumeroSalto { get; set; }
            public string? ZDATE_TEXT { get; set; }
            public string? Oggetto { get; set; }
            public string? TipoSalto { get; set; }
            public string? Note { get; set; }
        }

        private static string AppleSecondsToDisplayFromText(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";
            if (!double.TryParse(text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
                return "";

            var appleEpoch = new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var dt = appleEpoch.AddSeconds(seconds).ToLocalTime();
            return dt.ToString("dd/MM/yyyy");
        }
    }
}
