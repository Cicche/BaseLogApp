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
        Task<bool> AddJumpAsync(JumpListItem jump);
        Task<bool> AddObjectAsync(string name, string? description, string? position, string? heightMeters);
        Task<bool> AddRigAsync(string name, string? description);
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

                if (await HasTableAsync(db, "Jump"))
                {
                    const string jumpSql = @"
                        SELECT
                            Id AS Id,
                            Id AS NumeroSalto,
                            CAST(JumpDateUtc AS TEXT) AS DateText,
                            ObjectName AS Oggetto,
                            ExitName AS TipoSalto,
                            Notes AS Note,
                            PhotoPath AS ObjectPhotoPath
                        FROM Jump
                        ORDER BY Id DESC;";

                    var jumpRows = await db.QueryAsync<JumpRow>(jumpSql);
                    if (jumpRows.Count > 0)
                    {
                        return jumpRows.Select(r => new JumpListItem
                        {
                            Id = r.Id,
                            NumeroSalto = r.NumeroSalto,
                            Data = UnixSecondsToDisplay(r.DateText),
                            Oggetto = r.Oggetto,
                            TipoSalto = r.TipoSalto,
                            Note = r.Note,
                            ObjectPhotoPath = NormalizePhotoPath(r.ObjectPhotoPath)
                        }).ToList();
                    }
                }

                var photoExpr = await ResolveObjectPhotoExpressionAsync(db);
                var sql = $@"
                    SELECT
                        l.Z_PK AS Id,
                        l.ZJUMPNUMBER AS NumeroSalto,
                        CAST(l.ZDATE AS TEXT) AS DateText,
                        o.ZNAME AS Oggetto,
                        jt.ZNAME AS TipoSalto,
                        l.ZNOTES AS Note,
                        {photoExpr} AS ObjectPhotoPath
                    FROM ZLOGENTRY l
                    LEFT JOIN ZOBJECT o ON l.ZOBJECT = o.Z_PK
                    LEFT JOIN ZJUMPTYPE jt ON l.ZJUMPTYPE = jt.Z_PK
                    ORDER BY l.ZJUMPNUMBER DESC;";
                var rows = await db.QueryAsync<JumpRow>(sql);

                return rows.Select(r => new JumpListItem
                {
                    Id = r.Id,
                    NumeroSalto = r.NumeroSalto,
                    Data = AppleSecondsToDisplayFromText(r.DateText),
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
                var names = new List<string>();

                if (await HasTableAsync(db, "ZOBJECT"))
                {
                    var rows = await db.QueryAsync<ObjectNameRow>("SELECT ZNAME AS Name FROM ZOBJECT WHERE ZNAME IS NOT NULL AND TRIM(ZNAME) <> '';");
                    names.AddRange(rows.Select(x => x.Name!));
                }

                if (await HasTableAsync(db, "Jump"))
                {
                    var rows = await db.QueryAsync<ObjectNameRow>("SELECT ObjectName AS Name FROM Jump WHERE ObjectName IS NOT NULL AND TRIM(ObjectName) <> '';");
                    names.AddRange(rows.Select(x => x.Name!));
                }

                return names.Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error reading objects from database at '{dbPath}': {ex.Message}");
                return Array.Empty<string>();
            }
        }

        public async Task<bool> AddJumpAsync(JumpListItem jump)
        {
            var dbPath = ResolveDbPath();
            if (!File.Exists(dbPath))
                return false;

            try
            {
                var db = new SQLiteAsyncConnection(new SQLiteConnectionString(dbPath, false));

                if (await HasTableAsync(db, "Jump"))
                {
                    var unix = ToUnixSeconds(jump.Data);
                    await db.ExecuteAsync(@"
                        INSERT INTO Jump (ExitName, ObjectName, JumpDateUtc, Notes, PhotoPath)
                        VALUES (?, ?, ?, ?, ?);",
                        jump.TipoSalto,
                        jump.Oggetto,
                        unix,
                        jump.Note,
                        jump.ObjectPhotoPath);
                    return true;
                }

                if (await HasTableAsync(db, "ZLOGENTRY"))
                {
                    var nextPk = await GetNextPrimaryKeyAsync(db, "ZLOGENTRY", "Z_PK");
                    var zDate = ToAppleSeconds(jump.Data);
                    await db.ExecuteAsync(@"
                        INSERT INTO ZLOGENTRY (Z_PK, Z_ENT, Z_OPT, ZJUMPNUMBER, ZDATE, ZNOTES)
                        VALUES (?, 1, 1, ?, ?, ?);",
                        nextPk,
                        jump.NumeroSalto,
                        zDate,
                        jump.Note);
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error inserting jump: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> AddObjectAsync(string name, string? description, string? position, string? heightMeters)
        {
            var dbPath = ResolveDbPath();
            if (!File.Exists(dbPath) || string.IsNullOrWhiteSpace(name))
                return false;

            try
            {
                var db = new SQLiteAsyncConnection(new SQLiteConnectionString(dbPath, false));
                if (!await HasTableAsync(db, "ZOBJECT"))
                    return false;

                var nextPk = await GetNextPrimaryKeyAsync(db, "ZOBJECT", "Z_PK");
                var notes = string.Join(" | ", new[]
                {
                    description?.Trim(),
                    string.IsNullOrWhiteSpace(position) ? null : $"GPS: {position}",
                    string.IsNullOrWhiteSpace(heightMeters) ? null : $"H: {heightMeters}m"
                }.Where(x => !string.IsNullOrWhiteSpace(x)));

                await db.ExecuteAsync(@"
                    INSERT INTO ZOBJECT (Z_PK, Z_ENT, Z_OPT, ZNAME, ZNOTES)
                    VALUES (?, 1, 1, ?, ?);",
                    nextPk,
                    name.Trim(),
                    string.IsNullOrWhiteSpace(notes) ? null : notes);

                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error inserting object: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> AddRigAsync(string name, string? description)
        {
            var dbPath = ResolveDbPath();
            if (!File.Exists(dbPath) || string.IsNullOrWhiteSpace(name))
                return false;

            try
            {
                var db = new SQLiteAsyncConnection(new SQLiteConnectionString(dbPath, false));
                if (!await HasTableAsync(db, "ZRIG"))
                    return false;

                var nextPk = await GetNextPrimaryKeyAsync(db, "ZRIG", "Z_PK");
                await db.ExecuteAsync(@"
                    INSERT INTO ZRIG (Z_PK, Z_ENT, Z_OPT, ZNAME, ZNOTES)
                    VALUES (?, 1, 1, ?, ?);",
                    nextPk,
                    name.Trim(),
                    description?.Trim());

                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error inserting rig: {ex.Message}");
                return false;
            }
        }

        private static async Task<bool> HasTableAsync(SQLiteAsyncConnection db, string tableName)
        {
            var rows = await db.QueryAsync<ScalarInt>("SELECT COUNT(*) AS Value FROM sqlite_master WHERE type='table' AND name=?;", tableName);
            return rows.FirstOrDefault()?.Value > 0;
        }

        private static async Task<int> GetNextPrimaryKeyAsync(SQLiteAsyncConnection db, string table, string pkColumn)
        {
            var rows = await db.QueryAsync<ScalarInt>($"SELECT IFNULL(MAX({pkColumn}),0)+1 AS Value FROM {table};");
            return rows.FirstOrDefault()?.Value ?? 1;
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

        private sealed class JumpRow
        {
            public int Id { get; set; }
            public int NumeroSalto { get; set; }
            public string? DateText { get; set; }
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

        private sealed class ScalarInt
        {
            public int Value { get; set; }
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

        private static string UnixSecondsToDisplay(string? text)
        {
            if (string.IsNullOrWhiteSpace(text) || !long.TryParse(text, out var seconds))
                return "";

            return DateTimeOffset.FromUnixTimeSeconds(seconds).ToLocalTime().ToString("dd/MM/yyyy");
        }

        private static long ToUnixSeconds(string? displayDate)
        {
            if (DateTime.TryParseExact(displayDate, "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
                return new DateTimeOffset(parsed.Date).ToUnixTimeSeconds();

            return DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        }

        private static double ToAppleSeconds(string? displayDate)
        {
            if (!DateTime.TryParseExact(displayDate, "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
                parsed = DateTime.UtcNow.Date;

            var appleEpoch = new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            return (parsed.ToUniversalTime() - appleEpoch).TotalSeconds;
        }
    }
}
