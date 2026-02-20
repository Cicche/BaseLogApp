using BaseLogApp.Core.Models;
using SQLite;
using System.Diagnostics;
using System.Globalization;

namespace BaseLogApp.Core.Data
{
    public interface IJumpsReader
    {
        Task<IReadOnlyList<JumpListItem>> GetJumpsAsync();
        Task<IReadOnlyList<string>> GetObjectNamesAsync();
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

                var photoExpr = await ResolveObjectPhotoExpressionAsync(db);
                var sql = $@"
                                SELECT
                                l.Z_PK AS Id,
                                l.ZJUMPNUMBER AS NumeroSalto,
                                CAST(l.ZDATE AS TEXT) AS ZDATE_TEXT,
                                o.ZNAME AS Oggetto,
                                jt.ZNAME AS TipoSalto,
                                l.ZNOTES AS Note,
                                {photoExpr} AS ObjectPhotoPath
                                FROM ZLOGENTRY l
                                LEFT JOIN ZOBJECT o ON l.ZOBJECT = o.Z_PK
                                LEFT JOIN ZJUMPTYPE jt ON l.ZJUMPTYPE = jt.Z_PK
                                ORDER BY l.ZJUMPNUMBER DESC;";

                var rows = await db.QueryAsync<RawRow>(sql);

                return rows.Select(r => new JumpListItem
                {
                    Id = r.Id,
                    NumeroSalto = r.NumeroSalto,
                    Data = AppleSecondsToDisplayFromText(r.ZDATE_TEXT),
                    Oggetto = r.Oggetto,
                    TipoSalto = r.TipoSalto,
                    Note = r.Note,
                    ObjectPhotoPath = NormalizePhotoPath(r.ObjectPhotoPath)
                }).ToList();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error reading jumps database at '{dbPath}': {ex.Message}");
                return Array.Empty<JumpListItem>();
            }
        }

        public async Task<IReadOnlyList<string>> GetObjectNamesAsync()
        {
            var dbPath = ResolveDbPath();
            if (!File.Exists(dbPath))
                return Array.Empty<string>();

            try
            {
                var conn = new SQLiteConnectionString(dbPath, false);
                var db = new SQLiteAsyncConnection(conn);
                var rows = await db.QueryAsync<ObjectNameRow>("SELECT ZNAME AS Name FROM ZOBJECT WHERE ZNAME IS NOT NULL AND TRIM(ZNAME) <> '' ORDER BY ZNAME;");
                return rows.Select(x => x.Name!.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error reading objects from database at '{dbPath}': {ex.Message}");
                return Array.Empty<string>();
            }
        }

        private static async Task<string> ResolveObjectPhotoExpressionAsync(SQLiteAsyncConnection db)
        {
            try
            {
                var columns = await db.QueryAsync<PragmaColumn>("PRAGMA table_info('ZOBJECT');");
                var set = columns.Select(c => c.Name ?? string.Empty).ToHashSet(StringComparer.OrdinalIgnoreCase);

                var photoCandidates = new[]
                {
                    "ZIMAGEPATH",
                    "ZIMAGE",
                    "ZPHOTO",
                    "ZPHOTOPATH",
                    "ZTHUMBNAIL",
                    "ZIMAGEURL"
                };

                var found = photoCandidates.FirstOrDefault(set.Contains);
                return found is null ? "NULL" : $"o.{found}";
            }
            catch
            {
                return "NULL";
            }
        }

        private static string? NormalizePhotoPath(string? dbValue)
        {
            if (string.IsNullOrWhiteSpace(dbValue))
                return null;

            var trimmed = dbValue.Trim();
            if (trimmed.StartsWith("/", StringComparison.Ordinal) ||
                trimmed.Contains(":\\", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                return trimmed;
            }

            return null;
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
            public string? ObjectPhotoPath { get; set; }
        }

        private sealed class ObjectNameRow
        {
            public string? Name { get; set; }
        }

        private sealed class PragmaColumn
        {
            public string? Name { get; set; }
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
