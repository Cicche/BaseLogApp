using BaseLogApp.Core.Models;
using SQLite;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace BaseLogApp.Core.Data
{
    public enum DbProfile
    {
        Legacy,
        Modern
    }

    public interface IJumpsReader
    {
        void SetDbProfile(DbProfile profile);
        string GetCurrentDbPath();
        Task<IReadOnlyList<JumpListItem>> GetJumpsAsync();
        Task<IReadOnlyList<string>> GetObjectNamesAsync();
        Task<bool> AddJumpAsync(JumpListItem jump);
        Task<bool> AddObjectAsync(string name, string? description, string? position, string? heightMeters, byte[]? photoBytes);
        Task<bool> AddRigAsync(string name, string? description);
        Task<bool> AddJumpTypeAsync(string name, string? notes);
        Task<bool> ExportLightweightJsonAsync(string filePath);
        Task<bool> ImportLightweightJsonAsync(string filePath);
        Task<bool> ExportFullDbAsync(string destinationPath);
        Task<bool> ImportFullDbAsync(string sourcePath);
    }

    public class JumpsReader : IJumpsReader
    {
        private const string DefaultDbName = "BASELogbook.sqlite";
        private readonly string _dbPathWindows = @"C:\Temp\BASELogbook.sqlite";
        private DbProfile _profile = DbProfile.Modern;

        public void SetDbProfile(DbProfile profile) => _profile = profile;
        public string GetCurrentDbPath() => ResolveDbPath();

        public async Task<IReadOnlyList<JumpListItem>> GetJumpsAsync()
        {
            var dbPath = ResolveDbPath();
            if (!File.Exists(dbPath))
                return Array.Empty<JumpListItem>();

            try
            {
                var db = new SQLiteAsyncConnection(new SQLiteConnectionString(dbPath, false));

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
                            PhotoPath AS ObjectPhotoPath,
                            NULL AS ObjectPhotoBlob,
                            NULL AS JumpPhotoBlob,
                            CAST(Latitude AS TEXT) AS Latitude,
                            CAST(Longitude AS TEXT) AS Longitude
                        FROM Jump
                        ORDER BY Id DESC;";

                    var jumpRows = await db.QueryAsync<JumpRow>(jumpSql);
                    if (jumpRows.Count > 0)
                    {
                        return jumpRows.Select(ToJumpItemModern).ToList();
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
                        {photoExpr} AS ObjectPhotoPath,
                        (SELECT oi.ZIMAGE FROM ZOBJECTIMAGE oi WHERE oi.ZOBJECT = o.Z_PK LIMIT 1) AS ObjectPhotoBlob,
                        (SELECT li.ZIMAGE FROM ZLOGENTRYIMAGE li WHERE li.ZLOGENTRY = l.Z_PK LIMIT 1) AS JumpPhotoBlob,
                        NULL AS Latitude,
                        NULL AS Longitude
                    FROM ZLOGENTRY l
                    LEFT JOIN ZOBJECT o ON l.ZOBJECT = o.Z_PK
                    LEFT JOIN ZJUMPTYPE jt ON l.ZJUMPTYPE = jt.Z_PK
                    ORDER BY l.ZJUMPNUMBER DESC;";

                var rows = await db.QueryAsync<JumpRow>(sql);
                return rows.Select(ToJumpItemLegacy).ToList();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error reading jumps database at '{dbPath}': {ex.Message}");
                return Array.Empty<JumpListItem>();
            }
        }

        private static JumpListItem ToJumpItemModern(JumpRow r) => new()
        {
            Id = r.Id,
            NumeroSalto = r.NumeroSalto,
            Data = UnixSecondsToDisplay(r.DateText),
            Oggetto = r.Oggetto,
            TipoSalto = r.TipoSalto,
            Note = r.Note,
            ObjectPhotoPath = NormalizePhotoPath(r.ObjectPhotoPath),
            Latitude = r.Latitude,
            Longitude = r.Longitude
        };

        private static JumpListItem ToJumpItemLegacy(JumpRow r) => new()
        {
            Id = r.Id,
            NumeroSalto = r.NumeroSalto,
            Data = AppleSecondsToDisplayFromText(r.DateText),
            Oggetto = r.Oggetto,
            TipoSalto = r.TipoSalto,
            Note = r.Note,
            ObjectPhotoPath = NormalizePhotoPath(r.ObjectPhotoPath),
            ObjectPhotoBlob = r.ObjectPhotoBlob,
            JumpPhotoBlob = r.JumpPhotoBlob
        };

        public async Task<IReadOnlyList<string>> GetObjectNamesAsync()
        {
            var dbPath = ResolveDbPath();
            if (!File.Exists(dbPath)) return Array.Empty<string>();

            try
            {
                var db = new SQLiteAsyncConnection(new SQLiteConnectionString(dbPath, false));
                var names = new List<string>();

                if (await HasTableAsync(db, "ZOBJECT"))
                    names.AddRange((await db.QueryAsync<ObjectNameRow>("SELECT ZNAME AS Name FROM ZOBJECT WHERE ZNAME IS NOT NULL AND TRIM(ZNAME) <> '';"))
                        .Select(x => x.Name!));

                if (await HasTableAsync(db, "Jump"))
                    names.AddRange((await db.QueryAsync<ObjectNameRow>("SELECT ObjectName AS Name FROM Jump WHERE ObjectName IS NOT NULL AND TRIM(ObjectName) <> '';"))
                        .Select(x => x.Name!));

                return names.Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error reading objects from database: {ex.Message}");
                return Array.Empty<string>();
            }
        }

        public async Task<bool> AddJumpAsync(JumpListItem jump)
        {
            var dbPath = ResolveDbPath();
            if (!File.Exists(dbPath)) return false;
            try
            {
                var db = new SQLiteAsyncConnection(new SQLiteConnectionString(dbPath, false));
                if (await HasTableAsync(db, "Jump"))
                {
                    await db.ExecuteAsync(@"
                        INSERT INTO Jump (ExitName, ObjectName, JumpDateUtc, Latitude, Longitude, PhotoPath, Notes)
                        VALUES (?, ?, ?, ?, ?, ?, ?);",
                        jump.TipoSalto,
                        jump.Oggetto,
                        ToUnixSeconds(jump.Data),
                        ToNullableDouble(jump.Latitude),
                        ToNullableDouble(jump.Longitude),
                        jump.ObjectPhotoPath,
                        jump.Note);
                    return true;
                }

                if (await HasTableAsync(db, "ZLOGENTRY"))
                {
                    var nextPk = await GetNextPrimaryKeyAsync(db, "ZLOGENTRY", "Z_PK");
                    await db.ExecuteAsync(@"
                        INSERT INTO ZLOGENTRY (Z_PK, Z_ENT, Z_OPT, ZJUMPNUMBER, ZDATE, ZNOTES)
                        VALUES (?, 1, 1, ?, ?, ?);",
                        nextPk,
                        jump.NumeroSalto,
                        ToAppleSeconds(jump.Data),
                        jump.Note);

                    if (jump.NewPhotoBytes is { Length: > 0 } && await HasTableAsync(db, "ZLOGENTRYIMAGE"))
                    {
                        var nextImgPk = await GetNextPrimaryKeyAsync(db, "ZLOGENTRYIMAGE", "Z_PK");
                        await db.ExecuteAsync(@"
                            INSERT INTO ZLOGENTRYIMAGE (Z_PK, Z_ENT, Z_OPT, ZLOGENTRY, ZIMAGE)
                            VALUES (?, 1, 1, ?, ?);",
                            nextImgPk,
                            nextPk,
                            jump.NewPhotoBytes);
                    }

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

        public async Task<bool> AddObjectAsync(string name, string? description, string? position, string? heightMeters, byte[]? photoBytes)
        {
            var dbPath = ResolveDbPath();
            if (!File.Exists(dbPath) || string.IsNullOrWhiteSpace(name)) return false;
            try
            {
                var db = new SQLiteAsyncConnection(new SQLiteConnectionString(dbPath, false));
                if (!await HasTableAsync(db, "ZOBJECT")) return false;

                var nextPk = await GetNextPrimaryKeyAsync(db, "ZOBJECT", "Z_PK");
                var notes = string.Join(" | ", new[] { description, position is null ? null : $"GPS:{position}", heightMeters is null ? null : $"H:{heightMeters}m" }
                    .Where(x => !string.IsNullOrWhiteSpace(x)));

                await db.ExecuteAsync("INSERT INTO ZOBJECT (Z_PK, Z_ENT, Z_OPT, ZNAME, ZNOTES) VALUES (?,1,1,?,?);", nextPk, name.Trim(), notes);

                if (photoBytes is { Length: > 0 } && await HasTableAsync(db, "ZOBJECTIMAGE"))
                {
                    var nextImgPk = await GetNextPrimaryKeyAsync(db, "ZOBJECTIMAGE", "Z_PK");
                    await db.ExecuteAsync("INSERT INTO ZOBJECTIMAGE (Z_PK, Z_ENT, Z_OPT, ZOBJECT, ZIMAGE) VALUES (?,1,1,?,?);", nextImgPk, nextPk, photoBytes);
                }

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
            if (!File.Exists(dbPath) || string.IsNullOrWhiteSpace(name)) return false;
            try
            {
                var db = new SQLiteAsyncConnection(new SQLiteConnectionString(dbPath, false));
                if (!await HasTableAsync(db, "ZRIG")) return false;

                var nextPk = await GetNextPrimaryKeyAsync(db, "ZRIG", "Z_PK");
                await db.ExecuteAsync("INSERT INTO ZRIG (Z_PK, Z_ENT, Z_OPT, ZNAME, ZNOTES) VALUES (?,1,1,?,?);", nextPk, name.Trim(), description?.Trim());
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error inserting rig: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> AddJumpTypeAsync(string name, string? notes)
        {
            var dbPath = ResolveDbPath();
            if (!File.Exists(dbPath) || string.IsNullOrWhiteSpace(name)) return false;
            try
            {
                var db = new SQLiteAsyncConnection(new SQLiteConnectionString(dbPath, false));
                if (!await HasTableAsync(db, "ZJUMPTYPE")) return false;

                var existing = await db.QueryAsync<ObjectNameRow>("SELECT ZNAME AS Name FROM ZJUMPTYPE WHERE LOWER(TRIM(ZNAME)) = LOWER(TRIM(?)) LIMIT 1;", name);
                if (existing.Count > 0) return true;

                var nextPk = await GetNextPrimaryKeyAsync(db, "ZJUMPTYPE", "Z_PK");
                await db.ExecuteAsync("INSERT INTO ZJUMPTYPE (Z_PK, Z_ENT, Z_OPT, ZNAME, ZNOTES) VALUES (?,1,1,?,?);", nextPk, name.Trim(), notes?.Trim());
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error inserting jump type: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> ExportLightweightJsonAsync(string filePath)
        {
            try
            {
                var data = await GetJumpsAsync();
                var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(filePath, json);
                return true;
            }
            catch { return false; }
        }

        public async Task<bool> ImportLightweightJsonAsync(string filePath)
        {
            try
            {
                var json = await File.ReadAllTextAsync(filePath);
                var jumps = JsonSerializer.Deserialize<List<JumpListItem>>(json) ?? new();
                var ok = true;
                foreach (var j in jumps)
                    ok &= await AddJumpAsync(j);
                return ok;
            }
            catch { return false; }
        }

        public Task<bool> ExportFullDbAsync(string destinationPath)
        {
            try
            {
                File.Copy(ResolveDbPath(), destinationPath, true);
                return Task.FromResult(true);
            }
            catch { return Task.FromResult(false); }
        }

        public Task<bool> ImportFullDbAsync(string sourcePath)
        {
            try
            {
                File.Copy(sourcePath, ResolveDbPath(), true);
                return Task.FromResult(true);
            }
            catch { return Task.FromResult(false); }
        }

        private string ResolveDbPath()
        {
#if WINDOWS
            var legacy = Environment.GetEnvironmentVariable("BASELOG_DB_PATH_LEGACY");
            var modern = Environment.GetEnvironmentVariable("BASELOG_DB_PATH_MODERN");
            var fallback = Environment.GetEnvironmentVariable("BASELOG_DB_PATH");

            if (_profile == DbProfile.Legacy && !string.IsNullOrWhiteSpace(legacy)) return legacy;
            if (_profile == DbProfile.Modern && !string.IsNullOrWhiteSpace(modern)) return modern;
            if (!string.IsNullOrWhiteSpace(fallback)) return fallback;
            return _dbPathWindows;
#else
            return Path.Combine(FileSystem.AppDataDirectory, DefaultDbName);
#endif
        }

        private static async Task<bool> HasTableAsync(SQLiteAsyncConnection db, string tableName)
            => (await db.QueryAsync<ScalarInt>("SELECT COUNT(*) AS Value FROM sqlite_master WHERE type='table' AND name=?;", tableName)).FirstOrDefault()?.Value > 0;

        private static async Task<int> GetNextPrimaryKeyAsync(SQLiteAsyncConnection db, string table, string pkColumn)
            => (await db.QueryAsync<ScalarInt>($"SELECT IFNULL(MAX({pkColumn}),0)+1 AS Value FROM {table};")).FirstOrDefault()?.Value ?? 1;

        private static async Task<string> ResolveObjectPhotoExpressionAsync(SQLiteAsyncConnection db)
        {
            try
            {
                var set = (await db.QueryAsync<PragmaColumn>("PRAGMA table_info('ZOBJECT');")).Select(c => c.Name ?? string.Empty).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var found = new[] { "ZIMAGEPATH", "ZIMAGE", "ZPHOTO", "ZPHOTOPATH", "ZTHUMBNAIL", "ZIMAGEURL" }.FirstOrDefault(set.Contains);
                return found is null ? "NULL" : $"o.{found}";
            }
            catch { return "NULL"; }
        }

        private static string? NormalizePhotoPath(string? dbValue)
        {
            if (string.IsNullOrWhiteSpace(dbValue)) return null;
            var trimmed = dbValue.Trim();
            return (trimmed.StartsWith("/") || trimmed.Contains(":\\") || trimmed.StartsWith("http", StringComparison.OrdinalIgnoreCase)) ? trimmed : null;
        }

        private static string AppleSecondsToDisplayFromText(string? text)
        {
            if (string.IsNullOrWhiteSpace(text) || !double.TryParse(text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)) return "";
            var dt = new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(seconds).ToLocalTime();
            return dt.ToString("dd/MM/yyyy");
        }

        private static string UnixSecondsToDisplay(string? text)
            => string.IsNullOrWhiteSpace(text) || !long.TryParse(text, out var s) ? "" : DateTimeOffset.FromUnixTimeSeconds(s).ToLocalTime().ToString("dd/MM/yyyy");

        private static long ToUnixSeconds(string? displayDate)
            => DateTime.TryParseExact(displayDate, "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
                ? new DateTimeOffset(parsed.Date).ToUnixTimeSeconds()
                : DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        private static double ToAppleSeconds(string? displayDate)
        {
            if (!DateTime.TryParseExact(displayDate, "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
                parsed = DateTime.UtcNow.Date;
            return (parsed.ToUniversalTime() - new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
        }

        private static double? ToNullableDouble(string? text)
            => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : null;

        private sealed class JumpRow
        {
            public int Id { get; set; }
            public int NumeroSalto { get; set; }
            public string? DateText { get; set; }
            public string? Oggetto { get; set; }
            public string? TipoSalto { get; set; }
            public string? Note { get; set; }
            public string? ObjectPhotoPath { get; set; }
            public byte[]? ObjectPhotoBlob { get; set; }
            public byte[]? JumpPhotoBlob { get; set; }
            public string? Latitude { get; set; }
            public string? Longitude { get; set; }
        }

        private sealed class ObjectNameRow { public string? Name { get; set; } }
        private sealed class PragmaColumn { public string? Name { get; set; } }
        private sealed class ScalarInt { public int Value { get; set; } }
    }
}
