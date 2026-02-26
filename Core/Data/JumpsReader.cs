using BaseLogApp.Core.Models;
using SQLite;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace BaseLogApp.Core.Data;

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
    Task<IReadOnlyList<string>> GetJumpTypeNamesAsync();
    Task<IReadOnlyList<string>> GetRigNamesAsync();
    Task<IReadOnlyList<ObjectCatalogItem>> GetObjectsCatalogAsync();
    Task<(double? Latitude, double? Longitude)> GetObjectCoordinatesAsync(string? objectName);
    Task<IReadOnlyList<CatalogItem>> GetRigsCatalogAsync();
    Task<IReadOnlyList<CatalogItem>> GetJumpTypesCatalogAsync();
    Task<bool> AddJumpAsync(JumpListItem jump);
    Task<bool> UpdateJumpAsync(JumpListItem jump);
    Task<bool> DeleteJumpAsync(JumpListItem jump);
    Task<bool> ShiftJumpNumbersUpFromAsync(int fromNumber, int? excludeId = null);
    Task<bool> SupportsJumpNumberShiftAsync();
    Task<bool> AddObjectAsync(string name, string? objectType, string? description, string? position, string? heightMeters, byte[]? photoBytes);
    Task<bool> AddRigAsync(string name, string? description);
    Task<bool> AddJumpTypeAsync(string name, string? notes);
    Task<bool> UpdateObjectAsync(int id, string name, string? objectType, string? description, string? position, string? heightMeters, byte[]? photoBytes);
    Task<bool> UpdateRigAsync(int id, string name, string? description);
    Task<bool> UpdateJumpTypeAsync(int id, string name, string? notes);
    Task<int> NormalizeJumpNumbersAsync();
    Task<(bool CanDelete, string? Reason)> CanDeleteObjectAsync(int id);
    Task<(bool CanDelete, string? Reason)> CanDeleteRigAsync(int id);
    Task<(bool CanDelete, string? Reason)> CanDeleteJumpTypeAsync(int id);
    Task<bool> DeleteObjectAsync(int id);
    Task<bool> DeleteRigAsync(int id);
    Task<bool> DeleteJumpTypeAsync(int id);
    Task<bool> ExportLightweightJsonAsync(string filePath);
    Task<bool> ImportLightweightJsonAsync(string filePath);
    Task<bool> ExportFullDbAsync(string destinationPath);
    Task<bool> ImportFullDbAsync(string sourcePath);
}

public sealed class JumpsReader : IJumpsReader
{
    private const string DefaultDbName = "BASELogbook.sqlite";
    private readonly string _legacyFallbackWindowsPath = @"C:\Temp\BASELogbook.sqlite";
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
            var db = Open(dbPath);
            if (await HasTableAsync(db, "Jump"))
                return await GetModernJumpsAsync(db);

            if (await HasTableAsync(db, "ZLOGENTRY"))
                return await GetLegacyJumpsAsync(db);

            return Array.Empty<JumpListItem>();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[JumpsReader] GetJumpsAsync failed: {ex.Message}");
            return Array.Empty<JumpListItem>();
        }
    }

    public async Task<IReadOnlyList<string>> GetObjectNamesAsync()
    {
        var db = await TryOpenDbAsync();
        if (db is null) return Array.Empty<string>();

        try
        {
            var names = new List<string>();

            if (await HasTableAsync(db, "Jump"))
            {
                var rows = await db.QueryAsync<NameRow>("SELECT ObjectName AS Name FROM Jump WHERE ObjectName IS NOT NULL AND TRIM(ObjectName) <> ''; ");
                names.AddRange(rows.Select(x => x.Name));
            }

            if (await HasTableAsync(db, "ZOBJECT"))
            {
                var rows = await db.QueryAsync<NameRow>("SELECT ZNAME AS Name FROM ZOBJECT WHERE ZNAME IS NOT NULL AND TRIM(ZNAME) <> ''; ");
                names.AddRange(rows.Select(x => x.Name));
            }

            return names
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x)
                .ToList();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    public async Task<IReadOnlyList<string>> GetJumpTypeNamesAsync()
    {
        var db = await TryOpenDbAsync();
        if (db is null) return Array.Empty<string>();

        try
        {
            if (await HasTableAsync(db, "JumpType"))
            {
                return (await db.QueryAsync<NameRow>("SELECT Name FROM JumpType WHERE Name IS NOT NULL AND TRIM(Name) <> '';"))
                    .Select(x => x.Name.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x => x)
                    .ToList();
            }

            if (await HasTableAsync(db, "ZJUMPTYPE"))
            {
                return (await db.QueryAsync<NameRow>("SELECT ZNAME AS Name FROM ZJUMPTYPE WHERE ZNAME IS NOT NULL AND TRIM(ZNAME) <> '';"))
                    .Select(x => x.Name.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x => x)
                    .ToList();
            }

            if (await HasTableAsync(db, "Jump"))
            {
                return (await db.QueryAsync<NameRow>("SELECT ExitName AS Name FROM Jump WHERE ExitName IS NOT NULL AND TRIM(ExitName) <> '';"))
                    .Select(x => x.Name.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x => x)
                    .ToList();
            }

            return Array.Empty<string>();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    public async Task<IReadOnlyList<string>> GetRigNamesAsync()
    {
        var db = await TryOpenDbAsync();
        if (db is null) return Array.Empty<string>();

        try
        {
            if (await HasTableAsync(db, "Rig"))
            {
                return (await db.QueryAsync<NameRow>("SELECT Name FROM Rig WHERE Name IS NOT NULL AND TRIM(Name) <> '';"))
                    .Select(x => x.Name.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x => x)
                    .ToList();
            }

            if (await HasTableAsync(db, "ZRIG"))
            {
                return (await db.QueryAsync<NameRow>("SELECT ZNAME AS Name FROM ZRIG WHERE ZNAME IS NOT NULL AND TRIM(ZNAME) <> '';"))
                    .Select(x => x.Name.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x => x)
                    .ToList();
            }

            return Array.Empty<string>();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    public async Task<IReadOnlyList<ObjectCatalogItem>> GetObjectsCatalogAsync()
    {
        var db = await TryOpenDbAsync();
        if (db is null) return Array.Empty<ObjectCatalogItem>();

        try
        {
            if (await HasTableAsync(db, "Object"))
            {
                var sql = @"
SELECT
    Id,
    Name,
    ObjectType,
    Description AS Notes,
    HeightMeters,
    HeightUnit,
    Position,
    CAST(Latitude AS TEXT) AS Latitude,
    CAST(Longitude AS TEXT) AS Longitude,
    PhotoBlob
FROM Object
ORDER BY Name COLLATE NOCASE;";
                var rows = await db.QueryAsync<ObjectCatalogRow>(sql);
                return rows.Select(ToObjectCatalogItem).ToList();
            }

            if (await HasTableAsync(db, "ZOBJECT"))
            {
                var objectCols = await GetTableColumnsAsync(db, "ZOBJECT");
                var typeExpr = ColumnExpr("o", objectCols, "ZOBJECTTYPE");
                var descExpr = ColumnExpr("o", objectCols, "ZDESCRIPTION", "ZDESC");
                var heightExpr = ColumnExpr("o", objectCols, "ZHEIGHT", "ZHEIGHTMETERS", "ZALTITUDE");
                var unitExpr = ColumnExpr("o", objectCols, "ZHEIGHTUNIT");
                var posExpr = ColumnExpr("o", objectCols, "ZPOSITION", "ZLOCATION");
                var latExpr = ColumnExpr("o", objectCols, "ZLATITUDE");
                var lonExpr = ColumnExpr("o", objectCols, "ZLONGITUDE");

                var sql = $@"
SELECT
    o.Z_PK AS Id,
    o.ZNAME AS Name,
    {typeExpr} AS ObjectType,
    {descExpr} AS Notes,
    CAST({heightExpr} AS TEXT) AS HeightMeters,
    {unitExpr} AS HeightUnit,
    {posExpr} AS Position,
    CAST({latExpr} AS TEXT) AS Latitude,
    CAST({lonExpr} AS TEXT) AS Longitude,
    (SELECT oi.ZIMAGE FROM ZOBJECTIMAGE oi WHERE oi.ZOBJECT = o.Z_PK LIMIT 1) AS PhotoBlob
FROM ZOBJECT o
ORDER BY o.ZNAME COLLATE NOCASE;";
                var rows = await db.QueryAsync<ObjectCatalogRow>(sql);
                return rows.Select(ToObjectCatalogItem).ToList();
            }

            return Array.Empty<ObjectCatalogItem>();
        }
        catch
        {
            return Array.Empty<ObjectCatalogItem>();
        }
    }

    public async Task<(double? Latitude, double? Longitude)> GetObjectCoordinatesAsync(string? objectName)
    {
        if (string.IsNullOrWhiteSpace(objectName))
            return (null, null);

        var db = await TryOpenDbAsync();
        if (db is null) return (null, null);

        try
        {
            if (await HasTableAsync(db, "Object"))
            {
                var row = await db.FindWithQueryAsync<CoordinateRow>(
                    "SELECT Latitude, Longitude FROM Object WHERE lower(trim(Name)) = lower(trim(?)) LIMIT 1;",
                    objectName.Trim());
                return (row?.Latitude, row?.Longitude);
            }

            if (await HasTableAsync(db, "ZOBJECT"))
            {
                var cols = await GetTableColumnsAsync(db, "ZOBJECT");
                var latExpr = ColumnExpr("o", cols, "ZLATITUDE");
                var lonExpr = ColumnExpr("o", cols, "ZLONGITUDE");
                var row = await db.FindWithQueryAsync<CoordinateRow>(
                    $"SELECT {latExpr} AS Latitude, {lonExpr} AS Longitude FROM ZOBJECT o WHERE lower(trim(o.ZNAME)) = lower(trim(?)) LIMIT 1;",
                    objectName.Trim());
                return (row?.Latitude, row?.Longitude);
            }

            return (null, null);
        }
        catch
        {
            return (null, null);
        }
    }

    public async Task<IReadOnlyList<CatalogItem>> GetRigsCatalogAsync()
    {
        var db = await TryOpenDbAsync();
        if (db is null) return Array.Empty<CatalogItem>();

        try
        {
            if (await HasTableAsync(db, "Rig"))
            {
                return (await db.QueryAsync<CatalogRow>("SELECT Id, Name, Description AS Notes FROM Rig ORDER BY Name COLLATE NOCASE;"))
                    .Select(x => new CatalogItem { Id = x.Id, Name = x.Name ?? string.Empty, Notes = x.Notes })
                    .ToList();
            }

            if (await HasTableAsync(db, "ZRIG"))
            {
                var cols = await GetTableColumnsAsync(db, "ZRIG");
                var notesExpr = ColumnExpr("r", cols, "ZDESCRIPTION", "ZNOTES");
                var rows = await db.QueryAsync<CatalogRow>($"SELECT r.Z_PK AS Id, r.ZNAME AS Name, {notesExpr} AS Notes FROM ZRIG r ORDER BY r.ZNAME COLLATE NOCASE;");
                return rows.Select(x => new CatalogItem { Id = x.Id, Name = x.Name ?? string.Empty, Notes = x.Notes }).ToList();
            }

            return Array.Empty<CatalogItem>();
        }
        catch
        {
            return Array.Empty<CatalogItem>();
        }
    }

    public async Task<IReadOnlyList<CatalogItem>> GetJumpTypesCatalogAsync()
    {
        var db = await TryOpenDbAsync();
        if (db is null) return Array.Empty<CatalogItem>();

        try
        {
            if (await HasTableAsync(db, "JumpType"))
            {
                return (await db.QueryAsync<CatalogRow>("SELECT Id, Name, Notes FROM JumpType ORDER BY Name COLLATE NOCASE;"))
                    .Select(x => new CatalogItem { Id = x.Id, Name = x.Name ?? string.Empty, Notes = x.Notes })
                    .ToList();
            }

            if (await HasTableAsync(db, "ZJUMPTYPE"))
            {
                var cols = await GetTableColumnsAsync(db, "ZJUMPTYPE");
                var notesExpr = ColumnExpr("jt", cols, "ZNOTES", "ZDESCRIPTION");
                var rows = await db.QueryAsync<CatalogRow>($"SELECT jt.Z_PK AS Id, jt.ZNAME AS Name, {notesExpr} AS Notes FROM ZJUMPTYPE jt ORDER BY jt.ZNAME COLLATE NOCASE;");
                return rows.Select(x => new CatalogItem { Id = x.Id, Name = x.Name ?? string.Empty, Notes = x.Notes }).ToList();
            }

            return Array.Empty<CatalogItem>();
        }
        catch
        {
            return Array.Empty<CatalogItem>();
        }
    }

    public async Task<bool> AddJumpAsync(JumpListItem jump)
    {
        var db = await TryOpenDbAsync();
        if (db is null) return false;

        try
        {
            if (await HasTableAsync(db, "Jump"))
            {
                var epoch = ParseDisplayDateToUnixSeconds(jump.Data) ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                await db.ExecuteAsync(
                    "INSERT INTO Jump (Id, JumpDateUtc, ObjectName, ExitName, Notes, PhotoPath, Latitude, Longitude) VALUES (?, ?, ?, ?, ?, ?, ?, ?);",
                    jump.NumeroSalto,
                    epoch,
                    jump.Oggetto,
                    jump.TipoSalto,
                    jump.Note,
                    jump.ObjectPhotoPath,
                    ToNullableDouble(jump.Latitude),
                    ToNullableDouble(jump.Longitude));
                return true;
            }

            if (await HasTableAsync(db, "ZLOGENTRY"))
            {
                var date = ParseDisplayDate(jump.Data) ?? DateTime.Now;
                var appleSeconds = ToAppleReferenceSeconds(date);
                var objectId = await FindLegacyIdByNameAsync(db, "ZOBJECT", jump.Oggetto);
                var typeId = await FindLegacyIdByNameAsync(db, "ZJUMPTYPE", jump.TipoSalto);

                await db.ExecuteAsync(
                    "INSERT INTO ZLOGENTRY (ZJUMPNUMBER, ZDATE, ZOBJECT, ZJUMPTYPE, ZNOTES) VALUES (?, ?, ?, ?, ?);",
                    jump.NumeroSalto,
                    appleSeconds,
                    objectId,
                    typeId,
                    jump.Note);
                return true;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> UpdateJumpAsync(JumpListItem jump)
    {
        var db = await TryOpenDbAsync();
        if (db is null) return false;

        try
        {
            if (await HasTableAsync(db, "Jump"))
            {
                var epoch = ParseDisplayDateToUnixSeconds(jump.Data) ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                var rows = await db.ExecuteAsync(
                    "UPDATE Jump SET Id = ?, JumpDateUtc = ?, ObjectName = ?, ExitName = ?, Notes = ?, PhotoPath = ?, Latitude = ?, Longitude = ? WHERE Id = ?;",
                    jump.NumeroSalto,
                    epoch,
                    jump.Oggetto,
                    jump.TipoSalto,
                    jump.Note,
                    jump.ObjectPhotoPath,
                    ToNullableDouble(jump.Latitude),
                    ToNullableDouble(jump.Longitude),
                    jump.Id);
                return rows > 0;
            }

            if (await HasTableAsync(db, "ZLOGENTRY"))
            {
                var date = ParseDisplayDate(jump.Data) ?? DateTime.Now;
                var appleSeconds = ToAppleReferenceSeconds(date);
                var objectId = await FindLegacyIdByNameAsync(db, "ZOBJECT", jump.Oggetto);
                var typeId = await FindLegacyIdByNameAsync(db, "ZJUMPTYPE", jump.TipoSalto);

                var rows = await db.ExecuteAsync(
                    "UPDATE ZLOGENTRY SET ZJUMPNUMBER = ?, ZDATE = ?, ZOBJECT = ?, ZJUMPTYPE = ?, ZNOTES = ? WHERE Z_PK = ?;",
                    jump.NumeroSalto,
                    appleSeconds,
                    objectId,
                    typeId,
                    jump.Note,
                    jump.Id);

                if (rows > 0 && jump.NewPhotoBytes is { Length: > 0 })
                {
                    if (await HasTableAsync(db, "ZLOGENTRYIMAGE"))
                    {
                        await db.ExecuteAsync("DELETE FROM ZLOGENTRYIMAGE WHERE ZLOGENTRY = ?;", jump.Id);
                        await db.ExecuteAsync("INSERT INTO ZLOGENTRYIMAGE (ZLOGENTRY, ZIMAGE) VALUES (?, ?);", jump.Id, jump.NewPhotoBytes);
                    }
                }

                return rows > 0;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> DeleteJumpAsync(JumpListItem jump)
    {
        var db = await TryOpenDbAsync();
        if (db is null) return false;

        try
        {
            if (await HasTableAsync(db, "Jump"))
                return await db.ExecuteAsync("DELETE FROM Jump WHERE Id = ?;", jump.Id) > 0;

            if (await HasTableAsync(db, "ZLOGENTRY"))
            {
                if (await HasTableAsync(db, "ZLOGENTRYIMAGE"))
                    await db.ExecuteAsync("DELETE FROM ZLOGENTRYIMAGE WHERE ZLOGENTRY = ?;", jump.Id);
                return await db.ExecuteAsync("DELETE FROM ZLOGENTRY WHERE Z_PK = ?;", jump.Id) > 0;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> ShiftJumpNumbersUpFromAsync(int fromNumber, int? excludeId = null)
    {
        var db = await TryOpenDbAsync();
        if (db is null) return false;

        try
        {
            if (await HasTableAsync(db, "Jump"))
            {
                var idRows = await db.QueryAsync<IdRow>("SELECT Id FROM Jump WHERE Id >= ? ORDER BY Id DESC;", fromNumber);
                foreach (var row in idRows)
                {
                    if (excludeId.HasValue && row.Id == excludeId.Value)
                        continue;

                    await db.ExecuteAsync("UPDATE Jump SET Id = ? WHERE Id = ?;", row.Id + 1, row.Id);
                }

                return true;
            }

            if (await HasTableAsync(db, "ZLOGENTRY"))
            {
                if (excludeId.HasValue)
                {
                    await db.ExecuteAsync(
                        "UPDATE ZLOGENTRY SET ZJUMPNUMBER = ZJUMPNUMBER + 1 WHERE ZJUMPNUMBER >= ? AND Z_PK <> ?;",
                        fromNumber,
                        excludeId.Value);
                }
                else
                {
                    await db.ExecuteAsync("UPDATE ZLOGENTRY SET ZJUMPNUMBER = ZJUMPNUMBER + 1 WHERE ZJUMPNUMBER >= ?;", fromNumber);
                }
                return true;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> SupportsJumpNumberShiftAsync()
    {
        var db = await TryOpenDbAsync();
        if (db is null) return false;
        return await HasTableAsync(db, "Jump") || await HasTableAsync(db, "ZLOGENTRY");
    }

    public async Task<bool> AddObjectAsync(string name, string? objectType, string? description, string? position, string? heightMeters, byte[]? photoBytes)
    {
        var db = await TryOpenDbAsync();
        if (db is null || string.IsNullOrWhiteSpace(name)) return false;

        try
        {
            if (await HasTableAsync(db, "Object"))
            {
                await db.ExecuteAsync(
                    "INSERT INTO Object (Name, ObjectType, Description, Position, HeightMeters, PhotoBlob) VALUES (?, ?, ?, ?, ?, ?);",
                    name.Trim(), objectType, description, position, heightMeters, photoBytes);
                return true;
            }

            if (await HasTableAsync(db, "ZOBJECT"))
            {
                await db.ExecuteAsync(
                    "INSERT INTO ZOBJECT (ZNAME, ZOBJECTTYPE, ZDESCRIPTION, ZPOSITION, ZHEIGHT) VALUES (?, ?, ?, ?, ?);",
                    name.Trim(), objectType, description, position, heightMeters);

                if (photoBytes is { Length: > 0 } && await HasTableAsync(db, "ZOBJECTIMAGE"))
                {
                    var id = await db.ExecuteScalarAsync<long>("SELECT Z_PK FROM ZOBJECT WHERE ZNAME = ? ORDER BY Z_PK DESC LIMIT 1;", name.Trim());
                    if (id > 0)
                        await db.ExecuteAsync("INSERT INTO ZOBJECTIMAGE (ZOBJECT, ZIMAGE) VALUES (?, ?);", id, photoBytes);
                }

                return true;
            }

            return false;
        }
        catch
        {
            return false;
        }

        private static void PopulateObjectFieldsFromNotes(ObjectCatalogItem row)
        {
            if (string.IsNullOrWhiteSpace(row.Description) && !string.IsNullOrWhiteSpace(row.Notes))
                row.Description = row.Notes;

            if (string.IsNullOrWhiteSpace(row.Position) && !string.IsNullOrWhiteSpace(row.Latitude) && !string.IsNullOrWhiteSpace(row.Longitude))
                row.Position = $"{row.Latitude}, {row.Longitude}";

            if (!string.IsNullOrWhiteSpace(row.HeightMeters))
            {
                var cleaned = row.HeightMeters.Trim();
                row.HeightMeters = cleaned.Contains(',') ? cleaned.Replace(',', '.') : cleaned;
            }

            if (string.IsNullOrWhiteSpace(row.HeightUnit))
                row.HeightUnit = "m";
        }

        private static void TryAddObjectField(HashSet<string> columns, List<string> insertColumns, List<object?> values, string c1, object? value)
        {
            if (!columns.Contains(c1)) return;
            insertColumns.Add(c1);
            values.Add(value);
        }

        private static void TryAddObjectField(HashSet<string> columns, List<string> insertColumns, List<object?> values, string c1, string c2, string c3, string c4, string? value)
        {
            var c = new[] { c1, c2, c3, c4 }.FirstOrDefault(columns.Contains);
            if (c is null) return;
            insertColumns.Add(c);
            values.Add(value);
        }

        private static void TryAddObjectField(HashSet<string> columns, List<string> insertColumns, List<object?> values, string c1, string c2, string c3, string? value)
        {
            var c = new[] { c1, c2, c3 }.FirstOrDefault(columns.Contains);
            if (c is null) return;
            insertColumns.Add(c);
            values.Add(value);
        }

        private static void TryAddObjectField(HashSet<string> columns, List<string> insertColumns, List<object?> values, string c1, string c2, string? value)
        {
            var c = new[] { c1, c2 }.FirstOrDefault(columns.Contains);
            if (c is null) return;
            insertColumns.Add(c);
            values.Add(value);
        }

        private static void TryAddFirstAvailableField(HashSet<string> columns, List<string> insertColumns, List<object?> values, object? value, params string[] candidates)
        {
            var c = candidates.FirstOrDefault(columns.Contains);
            if (c is null) return;
            insertColumns.Add(c);
            values.Add(value);
        }

        private static void TryAddFirstAvailableUpdate(HashSet<string> columns, List<string> updates, List<object?> values, object? value, params string[] candidates)
        {
            var c = candidates.FirstOrDefault(columns.Contains);
            if (c is null) return;
            updates.Add($"{c}=?");
            values.Add(value);
        }

        private static void TryAddObjectUpdate(HashSet<string> columns, List<string> updates, List<object?> values, string c1, object? value)
        {
            if (!columns.Contains(c1)) return;
            updates.Add($"{c1}=?");
            values.Add(value);
        }

        private static void TryAddObjectUpdate(HashSet<string> columns, List<string> updates, List<object?> values, string c1, string c2, string c3, string c4, string? value)
        {
            var c = new[] { c1, c2, c3, c4 }.FirstOrDefault(columns.Contains);
            if (c is null) return;
            updates.Add($"{c}=?");
            values.Add(value);
        }

        private static void TryAddObjectUpdate(HashSet<string> columns, List<string> updates, List<object?> values, string c1, string c2, string c3, string? value)
        {
            var c = new[] { c1, c2, c3 }.FirstOrDefault(columns.Contains);
            if (c is null) return;
            updates.Add($"{c}=?");
            values.Add(value);
        }

        private static void TryAddObjectUpdate(HashSet<string> columns, List<string> updates, List<object?> values, string c1, string c2, string? value)
        {
            var c = new[] { c1, c2 }.FirstOrDefault(columns.Contains);
            if (c is null) return;
            updates.Add($"{c}=?");
            values.Add(value);
        }

        private static async Task<string?> GetObjectNameByIdAsync(SQLiteAsyncConnection db, int id)
            => (await db.QueryAsync<ObjectNameRow>("SELECT ZNAME AS Name FROM ZOBJECT WHERE Z_PK=? LIMIT 1;", id)).FirstOrDefault()?.Name;

        private static async Task<string?> GetJumpTypeNameByIdAsync(SQLiteAsyncConnection db, int id)
            => (await db.QueryAsync<ObjectNameRow>("SELECT ZNAME AS Name FROM ZJUMPTYPE WHERE Z_PK=? LIMIT 1;", id)).FirstOrDefault()?.Name;

        private static async Task AppendDbLogAsync(string dbPath, string category, string title, IEnumerable<string> lines)
        {
            try
            {
                var logPath = dbPath + ".log";
                var header = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [CAT:{category}] {title}";
                var content = string.Join(Environment.NewLine, lines.Select(l => $" - {l}"));
                var block = $"{header}{Environment.NewLine}{content}{Environment.NewLine}";
                await File.AppendAllTextAsync(logPath, block + Environment.NewLine);
            }
            catch
            {
                // best-effort log writing
            }
        }

        private static async Task<int> NormalizeSequenceWithDateCheckAsync(
            string dbPath,
            IReadOnlyList<NormalizeRow> rows,
            Func<string?, DateTime?> parseDate,
            Func<int, int, Task> applyUpdate,
            string sourceLabel)
        {
            if (rows.Count == 0)
                return 0;

            var logs = new List<string>();

            // Human-friendly incongruence report: jump number order should follow jump date order.
            for (var i = 1; i < rows.Count; i++)
            {
                var prevDate = parseDate(rows[i - 1].DateText);
                var curDate = parseDate(rows[i].DateText);
                if (prevDate.HasValue && curDate.HasValue && curDate < prevDate)
                {
                    logs.Add($"INCONGRUENZA DATA/NUMERO: #{rows[i - 1].Number} ({prevDate:yyyy-MM-dd HH:mm}) precede #{rows[i].Number} ({curDate:yyyy-MM-dd HH:mm}) ma la data è invertita.");
                }
            }

            var expected = 1;
            var changes = 0;
            foreach (var row in rows)
            {
                if (row.Number > expected)
                {
                    await applyUpdate(row.Pk, expected);
                    logs.Add($"{sourceLabel} pk/id={row.Pk}: buco numerico chiuso {row.Number} -> {expected} (shift successivi)");
                    changes++;
                }
                else if (row.Number < expected)
                {
                    // Duplicate/out-of-order numbers: do not auto-fix aggressively, only report.
                    logs.Add($"{sourceLabel} pk/id={row.Pk}: numero {row.Number} inferiore all'atteso {expected} (verifica manuale consigliata)");
                }

                expected++;
            }

            if (logs.Count > 0)
                await AppendDbLogAsync(dbPath, "DATA_CONSISTENCY", "Controllo coerenza numeri salto", logs);

            return changes;
        }

        private static DateTime? AppleSecondsToDateTime(string? text)
        {
            if (string.IsNullOrWhiteSpace(text) || !double.TryParse(text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
                return null;
            return new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(seconds).ToLocalTime();
        }

        private static DateTime? UnixSecondsToDateTime(string? text)
        {
            if (string.IsNullOrWhiteSpace(text) || !long.TryParse(text, out var seconds))
                return null;
            return DateTimeOffset.FromUnixTimeSeconds(seconds).ToLocalTime().DateTime;
        }

        public async Task<IReadOnlyList<CatalogItem>> GetJumpTypesCatalogAsync()
        {
            var dbPath = ResolveDbPath();
            if (!File.Exists(dbPath)) return Array.Empty<CatalogItem>();

            try
            {
                var db = new SQLiteAsyncConnection(new SQLiteConnectionString(dbPath, false));
                if (!await HasTableAsync(db, "ZJUMPTYPE")) return Array.Empty<CatalogItem>();
                return (await db.QueryAsync<CatalogItem>("SELECT Z_PK AS Id, ZNAME AS Name, ZNOTES AS Notes FROM ZJUMPTYPE ORDER BY ZNAME;"))
                    .Where(x => !string.IsNullOrWhiteSpace(x.Name))
                    .ToList();
            }
            catch { return Array.Empty<CatalogItem>(); }
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
                    var logEntryColumns = await GetTableColumnsAsync(db, "ZLOGENTRY");
                    var insertColumns = new List<string> { "Z_PK", "Z_ENT", "Z_OPT", "ZJUMPNUMBER", "ZDATE", "ZNOTES" };
                    var insertValues = new List<object?> { nextPk, 1, 1, jump.NumeroSalto, ToAppleSeconds(jump.Data), jump.Note };
                    TryAddFirstAvailableField(logEntryColumns, insertColumns, insertValues, jump.DelaySeconds, "ZDELAY", "ZDELAYSECONDS", "ZDELAYINSECONDS");
                    TryAddFirstAvailableField(logEntryColumns, insertColumns, insertValues, jump.HeadingDegrees, "ZHEADING", "ZOPENINGHEADING", "ZTRACK");
                    TryAddObjectField(logEntryColumns, insertColumns, insertValues, "ZUNIQUEID", Guid.NewGuid().ToString("D"));

                    var placeholders = string.Join(",", Enumerable.Repeat("?", insertColumns.Count));
                    await db.ExecuteAsync($"INSERT INTO ZLOGENTRY ({string.Join(",", insertColumns)}) VALUES ({placeholders});", insertValues.ToArray());

                    if (jump.NewPhotoBytes is { Length: > 0 } && await HasTableAsync(db, "ZLOGENTRYIMAGE"))
                    {
                        var nextImgPk = await GetNextPrimaryKeyAsync(db, "ZLOGENTRYIMAGE", "Z_PK");
                        var logEntryImageColumns = await GetTableColumnsAsync(db, "ZLOGENTRYIMAGE");
                        if (logEntryImageColumns.Contains("ZUNIQUEID"))
                        {
                            await db.ExecuteAsync(@"
                                INSERT INTO ZLOGENTRYIMAGE (Z_PK, Z_ENT, Z_OPT, ZLOGENTRY, ZIMAGE, ZUNIQUEID)
                                VALUES (?, 1, 1, ?, ?, ?);",
                                nextImgPk,
                                nextPk,
                                jump.NewPhotoBytes,
                                Guid.NewGuid().ToString("D"));
                        }
                        else
                        {
                            await db.ExecuteAsync(@"
                                INSERT INTO ZLOGENTRYIMAGE (Z_PK, Z_ENT, Z_OPT, ZLOGENTRY, ZIMAGE)
                                VALUES (?, 1, 1, ?, ?);",
                                nextImgPk,
                                nextPk,
                                jump.NewPhotoBytes);
                        }
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

        public async Task<bool> UpdateJumpAsync(JumpListItem jump)
        {
            var dbPath = ResolveDbPath();
            if (!File.Exists(dbPath)) return false;
            try
            {
                var db = new SQLiteAsyncConnection(new SQLiteConnectionString(dbPath, false));
                if (await HasTableAsync(db, "Jump"))
                {
                    await db.ExecuteAsync(@"UPDATE Jump
                                            SET ExitName=?, ObjectName=?, JumpDateUtc=?, Latitude=?, Longitude=?, Notes=?
                                            WHERE Id=?;",
                        jump.TipoSalto,
                        jump.Oggetto,
                        ToUnixSeconds(jump.Data),
                        ToNullableDouble(jump.Latitude),
                        ToNullableDouble(jump.Longitude),
                        jump.Note,
                        jump.Id);
                    return true;
                }

                if (await HasTableAsync(db, "ZLOGENTRY"))
                {
                    var columns = await GetTableColumnsAsync(db, "ZLOGENTRY");
                    var updates = new List<string> { "ZJUMPNUMBER=?", "ZDATE=?", "ZNOTES=?" };
                    var values = new List<object?> { jump.NumeroSalto, ToAppleSeconds(jump.Data), jump.Note };
                    TryAddFirstAvailableUpdate(columns, updates, values, jump.DelaySeconds, "ZDELAY", "ZDELAYSECONDS", "ZDELAYINSECONDS");
                    TryAddFirstAvailableUpdate(columns, updates, values, jump.HeadingDegrees, "ZHEADING", "ZOPENINGHEADING", "ZTRACK");
                    values.Add(jump.Id);

                    await db.ExecuteAsync($"UPDATE ZLOGENTRY SET {string.Join(",", updates)} WHERE Z_PK=?;", values.ToArray());
                    return true;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        public async Task<bool> DeleteJumpAsync(JumpListItem jump)
        {
            var dbPath = ResolveDbPath();
            if (!File.Exists(dbPath)) return false;
            try
            {
                var db = new SQLiteAsyncConnection(new SQLiteConnectionString(dbPath, false));
                if (await HasTableAsync(db, "Jump"))
                {
                    await db.ExecuteAsync("DELETE FROM Jump WHERE Id=?;", jump.Id);
                    return true;
                }

                if (await HasTableAsync(db, "ZLOGENTRY"))
                {
                    await db.ExecuteAsync("DELETE FROM ZLOGENTRYIMAGE WHERE ZLOGENTRY=?;", jump.Id);
                    await db.ExecuteAsync("DELETE FROM ZLOGENTRY WHERE Z_PK=?;", jump.Id);
                    await db.ExecuteAsync("UPDATE ZLOGENTRY SET ZJUMPNUMBER = ZJUMPNUMBER - 1 WHERE ZJUMPNUMBER > ?;", jump.NumeroSalto);
                    return true;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }


        public async Task<bool> SupportsJumpNumberShiftAsync()
        {
            var dbPath = ResolveDbPath();
            if (!File.Exists(dbPath)) return false;

            try
            {
                var db = new SQLiteAsyncConnection(new SQLiteConnectionString(dbPath, false));
                if (await HasTableAsync(db, "ZLOGENTRY"))
                    return true;

                if (await HasTableAsync(db, "Jump"))
                {
                    var cols = await db.QueryAsync<PragmaColumn>("PRAGMA table_info('Jump');");
                    return cols.Any(c => string.Equals(c.Name, "JumpNumber", StringComparison.OrdinalIgnoreCase));
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        public async Task<bool> ShiftJumpNumbersUpFromAsync(int fromNumber, int? excludeId = null)
        {
            var dbPath = ResolveDbPath();
            if (!File.Exists(dbPath)) return false;

            try
            {
                var db = new SQLiteAsyncConnection(new SQLiteConnectionString(dbPath, false));

                if (await HasTableAsync(db, "ZLOGENTRY"))
                {
                    if (excludeId.HasValue)
                    {
                        await db.ExecuteAsync("UPDATE ZLOGENTRY SET ZJUMPNUMBER = ZJUMPNUMBER + 1 WHERE ZJUMPNUMBER >= ? AND Z_PK <> ?;", fromNumber, excludeId.Value);
                        await AppendDbLogAsync(dbPath, "NUMBER_SHIFT", "Shift numeri salto", new[] { $"ZLOGENTRY: +1 da {fromNumber} (escluso pk={excludeId.Value})" });
                    }
                    else
                    {
                        await db.ExecuteAsync("UPDATE ZLOGENTRY SET ZJUMPNUMBER = ZJUMPNUMBER + 1 WHERE ZJUMPNUMBER >= ?;", fromNumber);
                        await AppendDbLogAsync(dbPath, "NUMBER_SHIFT", "Shift numeri salto", new[] { $"ZLOGENTRY: +1 da {fromNumber}" });
                    }
                    return true;
                }

                if (await HasTableAsync(db, "Jump"))
                {
                    var cols = await db.QueryAsync<PragmaColumn>("PRAGMA table_info('Jump');");
                    if (!cols.Any(c => string.Equals(c.Name, "JumpNumber", StringComparison.OrdinalIgnoreCase)))
                        return false;

                    if (excludeId.HasValue)
                    {
                        await db.ExecuteAsync("UPDATE Jump SET JumpNumber = JumpNumber + 1 WHERE JumpNumber >= ? AND Id <> ?;", fromNumber, excludeId.Value);
                        await AppendDbLogAsync(dbPath, "NUMBER_SHIFT", "Shift numeri salto", new[] { $"Jump: +1 da {fromNumber} (escluso id={excludeId.Value})" });
                    }
                    else
                    {
                        await db.ExecuteAsync("UPDATE Jump SET JumpNumber = JumpNumber + 1 WHERE JumpNumber >= ?;", fromNumber);
                        await AppendDbLogAsync(dbPath, "NUMBER_SHIFT", "Shift numeri salto", new[] { $"Jump: +1 da {fromNumber}" });
                    }
                    return true;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        public async Task<bool> AddObjectAsync(string name, string? objectType, string? description, string? position, string? heightMeters, byte[]? photoBytes)
        {
            var dbPath = ResolveDbPath();
            if (!File.Exists(dbPath) || string.IsNullOrWhiteSpace(name)) return false;
            try
            {
                var db = new SQLiteAsyncConnection(new SQLiteConnectionString(dbPath, false));
                if (!await HasTableAsync(db, "ZOBJECT")) return false;

                var nextPk = await GetNextPrimaryKeyAsync(db, "ZOBJECT", "Z_PK");
                var columns = await GetTableColumnsAsync(db, "ZOBJECT");
                var pos = ParseLatLon(position);
                var notes = BuildObjectNotes(description);

                var insertColumns = new List<string> { "Z_PK", "Z_ENT", "Z_OPT", "ZNAME", "ZNOTES" };
                var values = new List<object?> { nextPk, 1, 1, name.Trim(), notes };
                TryAddObjectField(columns, insertColumns, values, "ZOBJECTTYPE", objectType?.Trim());
                TryAddObjectField(columns, insertColumns, values, "ZDESCRIPTION", "ZDESC", description?.Trim());
                TryAddObjectField(columns, insertColumns, values, "ZLATITUDE", pos?.lat);
                TryAddObjectField(columns, insertColumns, values, "ZLONGITUDE", pos?.lon);
                TryAddObjectField(columns, insertColumns, values, "ZHEIGHT", ToNullableDouble(heightMeters));
                TryAddObjectField(columns, insertColumns, values, "ZHEIGHTUNIT", "m");
                TryAddObjectField(columns, insertColumns, values, "ZUNIQUEID", Guid.NewGuid().ToString("D"));

                var placeholders = string.Join(",", Enumerable.Repeat("?", insertColumns.Count));
                await db.ExecuteAsync($"INSERT INTO ZOBJECT ({string.Join(",", insertColumns)}) VALUES ({placeholders});", values.ToArray());

                if (photoBytes is { Length: > 0 } && await HasTableAsync(db, "ZOBJECTIMAGE"))
                {
                    var nextImgPk = await GetNextPrimaryKeyAsync(db, "ZOBJECTIMAGE", "Z_PK");
                    var objectImageColumns = await GetTableColumnsAsync(db, "ZOBJECTIMAGE");
                    if (objectImageColumns.Contains("ZUNIQUEID"))
                        await db.ExecuteAsync("INSERT INTO ZOBJECTIMAGE (Z_PK, Z_ENT, Z_OPT, ZOBJECT, ZIMAGE, ZUNIQUEID) VALUES (?,1,1,?,?,?);", nextImgPk, nextPk, photoBytes, Guid.NewGuid().ToString("D"));
                    else
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
                var rigColumns = await GetTableColumnsAsync(db, "ZRIG");
                if (rigColumns.Contains("ZUNIQUEID"))
                    await db.ExecuteAsync("INSERT INTO ZRIG (Z_PK, Z_ENT, Z_OPT, ZNAME, ZNOTES, ZUNIQUEID) VALUES (?,1,1,?,?,?);", nextPk, name.Trim(), description?.Trim(), Guid.NewGuid().ToString("D"));
                else
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
                var jumpTypeColumns = await GetTableColumnsAsync(db, "ZJUMPTYPE");
                if (jumpTypeColumns.Contains("ZUNIQUEID"))
                    await db.ExecuteAsync("INSERT INTO ZJUMPTYPE (Z_PK, Z_ENT, Z_OPT, ZNAME, ZNOTES, ZUNIQUEID) VALUES (?,1,1,?,?,?);", nextPk, name.Trim(), notes?.Trim(), Guid.NewGuid().ToString("D"));
                else
                    await db.ExecuteAsync("INSERT INTO ZJUMPTYPE (Z_PK, Z_ENT, Z_OPT, ZNAME, ZNOTES) VALUES (?,1,1,?,?);", nextPk, name.Trim(), notes?.Trim());
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error inserting jump type: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> UpdateObjectAsync(int id, string name, string? objectType, string? description, string? position, string? heightMeters, byte[]? photoBytes)
        {
            var dbPath = ResolveDbPath();
            if (!File.Exists(dbPath) || string.IsNullOrWhiteSpace(name)) return false;
            try
            {
                var db = new SQLiteAsyncConnection(new SQLiteConnectionString(dbPath, false));
                if (!await HasTableAsync(db, "ZOBJECT")) return false;

                var columns = await GetTableColumnsAsync(db, "ZOBJECT");
                var pos = ParseLatLon(position);
                var notes = BuildObjectNotes(description);

                var updates = new List<string> { "ZNAME=?", "ZNOTES=?" };
                var values = new List<object?> { name.Trim(), notes };
                TryAddObjectUpdate(columns, updates, values, "ZOBJECTTYPE", objectType?.Trim());
                TryAddObjectUpdate(columns, updates, values, "ZDESCRIPTION", "ZDESC", description?.Trim());
                TryAddObjectUpdate(columns, updates, values, "ZLATITUDE", pos?.lat);
                TryAddObjectUpdate(columns, updates, values, "ZLONGITUDE", pos?.lon);
                TryAddObjectUpdate(columns, updates, values, "ZHEIGHT", ToNullableDouble(heightMeters));
                TryAddObjectUpdate(columns, updates, values, "ZHEIGHTUNIT", "m");
                values.Add(id);

                await db.ExecuteAsync($"UPDATE ZOBJECT SET {string.Join(",", updates)} WHERE Z_PK=?;", values.ToArray());

                if (photoBytes is { Length: > 0 } && await HasTableAsync(db, "ZOBJECTIMAGE"))
                {
                    var exists = await db.QueryAsync<ScalarInt>("SELECT COUNT(*) AS Value FROM ZOBJECTIMAGE WHERE ZOBJECT=?;", id);
                    if ((exists.FirstOrDefault()?.Value ?? 0) > 0)
                        await db.ExecuteAsync("UPDATE ZOBJECTIMAGE SET ZIMAGE=? WHERE ZOBJECT=?;", photoBytes, id);
                    else
                    {
                        var nextImgPk = await GetNextPrimaryKeyAsync(db, "ZOBJECTIMAGE", "Z_PK");
                        var objectImageColumns = await GetTableColumnsAsync(db, "ZOBJECTIMAGE");
                        if (objectImageColumns.Contains("ZUNIQUEID"))
                            await db.ExecuteAsync("INSERT INTO ZOBJECTIMAGE (Z_PK, Z_ENT, Z_OPT, ZOBJECT, ZIMAGE, ZUNIQUEID) VALUES (?,1,1,?,?,?);", nextImgPk, id, photoBytes, Guid.NewGuid().ToString("D"));
                        else
                            await db.ExecuteAsync("INSERT INTO ZOBJECTIMAGE (Z_PK, Z_ENT, Z_OPT, ZOBJECT, ZIMAGE) VALUES (?,1,1,?,?);", nextImgPk, id, photoBytes);
                    }
                }

                return true;
            }
            catch { return false; }
        }

        public async Task<bool> UpdateRigAsync(int id, string name, string? description)
        {
            var dbPath = ResolveDbPath();
            if (!File.Exists(dbPath) || string.IsNullOrWhiteSpace(name)) return false;
            try
            {
                var db = new SQLiteAsyncConnection(new SQLiteConnectionString(dbPath, false));
                if (!await HasTableAsync(db, "ZRIG")) return false;
                await db.ExecuteAsync("UPDATE ZRIG SET ZNAME=?, ZNOTES=? WHERE Z_PK=?;", name.Trim(), description?.Trim(), id);
                return true;
            }
            catch { return false; }
        }

        public async Task<bool> UpdateJumpTypeAsync(int id, string name, string? notes)
        {
            var dbPath = ResolveDbPath();
            if (!File.Exists(dbPath) || string.IsNullOrWhiteSpace(name)) return false;
            try
            {
                var db = new SQLiteAsyncConnection(new SQLiteConnectionString(dbPath, false));
                if (!await HasTableAsync(db, "ZJUMPTYPE")) return false;
                await db.ExecuteAsync("UPDATE ZJUMPTYPE SET ZNAME=?, ZNOTES=? WHERE Z_PK=?;", name.Trim(), notes?.Trim(), id);
                return true;
            }
            catch { return false; }
        }

        public async Task<int> NormalizeJumpNumbersAsync()
        {
            var dbPath = ResolveDbPath();
            if (!File.Exists(dbPath)) return 0;

            try
            {
                var db = new SQLiteAsyncConnection(new SQLiteConnectionString(dbPath, false));

                if (await HasTableAsync(db, "ZLOGENTRY"))
                {
                    var cols = await GetTableColumnsAsync(db, "ZLOGENTRY");
                    if (!cols.Contains("ZJUMPNUMBER")) return 0;

                    var rows = await db.QueryAsync<NormalizeRow>(@"SELECT Z_PK AS Pk,
                                                                         IFNULL(ZJUMPNUMBER,0) AS Number,
                                                                         CAST(ZDATE AS TEXT) AS DateText
                                                                  FROM ZLOGENTRY
                                                                  ORDER BY ZJUMPNUMBER ASC, Z_PK ASC;");

                    return await NormalizeSequenceWithDateCheckAsync(
                        dbPath,
                        rows,
                        parseDate: AppleSecondsToDateTime,
                        applyUpdate: async (pk, number) => await db.ExecuteAsync("UPDATE ZLOGENTRY SET ZJUMPNUMBER=? WHERE Z_PK=?;", number, pk),
                        sourceLabel: "ZLOGENTRY");
                }

                if (await HasTableAsync(db, "Jump"))
                {
                    var cols = await GetTableColumnsAsync(db, "Jump");
                    if (!cols.Contains("JumpNumber")) return 0;

                    var rows = await db.QueryAsync<NormalizeRow>(@"SELECT Id AS Pk,
                                                                         IFNULL(JumpNumber,0) AS Number,
                                                                         CAST(JumpDateUtc AS TEXT) AS DateText
                                                                  FROM Jump
                                                                  ORDER BY JumpNumber ASC, Id ASC;");

                    return await NormalizeSequenceWithDateCheckAsync(
                        dbPath,
                        rows,
                        parseDate: UnixSecondsToDateTime,
                        applyUpdate: async (pk, number) => await db.ExecuteAsync("UPDATE Jump SET JumpNumber=? WHERE Id=?;", number, pk),
                        sourceLabel: "Jump");
                }

                return 0;
            }
            catch
            {
                return 0;
            }
        }

        public async Task<(bool CanDelete, string? Reason)> CanDeleteObjectAsync(int id)
        {
            var dbPath = ResolveDbPath();
            if (!File.Exists(dbPath)) return (false, "DB non trovato.");

            try
            {
                var db = new SQLiteAsyncConnection(new SQLiteConnectionString(dbPath, false));
                var objectName = await GetObjectNameByIdAsync(db, id);
                var refs = 0;

                if (await HasTableAsync(db, "ZLOGENTRY"))
                {
                    var cols = await GetTableColumnsAsync(db, "ZLOGENTRY");
                    if (cols.Contains("ZOBJECT"))
                    {
                        refs += (await db.QueryAsync<ScalarInt>("SELECT COUNT(*) AS Value FROM ZLOGENTRY WHERE ZOBJECT=?;", id)).FirstOrDefault()?.Value ?? 0;
                    }
                }

                if (!string.IsNullOrWhiteSpace(objectName) && await HasTableAsync(db, "Jump"))
                {
                    refs += (await db.QueryAsync<ScalarInt>("SELECT COUNT(*) AS Value FROM Jump WHERE LOWER(TRIM(ObjectName)) = LOWER(TRIM(?));", objectName)).FirstOrDefault()?.Value ?? 0;
                }

                if (refs > 0)
                {
                    await AppendDbLogAsync(dbPath, "REFERENCE_INTEGRITY", "Delete object bloccato", new[] { $"Object id={id} associato a {refs} salto/i" });
                    return (false, $"Impossibile eliminare: object associato a {refs} salto/i.");
                }

                return (true, null);
            }
            catch
            {
                return (false, "Errore durante il controllo delle associazioni object.");
            }
        }

        public async Task<(bool CanDelete, string? Reason)> CanDeleteRigAsync(int id)
        {
            var dbPath = ResolveDbPath();
            if (!File.Exists(dbPath)) return (false, "DB non trovato.");

            try
            {
                var db = new SQLiteAsyncConnection(new SQLiteConnectionString(dbPath, false));
                var refs = 0;
                if (await HasTableAsync(db, "ZLOGENTRY"))
                {
                    var cols = await GetTableColumnsAsync(db, "ZLOGENTRY");
                    if (cols.Contains("ZRIG"))
                        refs += (await db.QueryAsync<ScalarInt>("SELECT COUNT(*) AS Value FROM ZLOGENTRY WHERE ZRIG=?;", id)).FirstOrDefault()?.Value ?? 0;
                }

                if (refs > 0)
                {
                    await AppendDbLogAsync(dbPath, "REFERENCE_INTEGRITY", "Delete rig bloccato", new[] { $"Rig id={id} associato a {refs} salto/i" });
                    return (false, $"Impossibile eliminare: rig associato a {refs} salto/i.");
                }

                return (true, null);
            }
            catch
            {
                return (false, "Errore durante il controllo delle associazioni rig.");
            }
        }

        public async Task<(bool CanDelete, string? Reason)> CanDeleteJumpTypeAsync(int id)
        {
            var dbPath = ResolveDbPath();
            if (!File.Exists(dbPath)) return (false, "DB non trovato.");

            try
            {
                var db = new SQLiteAsyncConnection(new SQLiteConnectionString(dbPath, false));
                var typeName = await GetJumpTypeNameByIdAsync(db, id);
                var refs = 0;

                if (await HasTableAsync(db, "ZLOGENTRY"))
                {
                    var cols = await GetTableColumnsAsync(db, "ZLOGENTRY");
                    if (cols.Contains("ZJUMPTYPE"))
                        refs += (await db.QueryAsync<ScalarInt>("SELECT COUNT(*) AS Value FROM ZLOGENTRY WHERE ZJUMPTYPE=?;", id)).FirstOrDefault()?.Value ?? 0;
                }

                if (!string.IsNullOrWhiteSpace(typeName) && await HasTableAsync(db, "Jump"))
                    refs += (await db.QueryAsync<ScalarInt>("SELECT COUNT(*) AS Value FROM Jump WHERE LOWER(TRIM(ExitName)) = LOWER(TRIM(?));", typeName)).FirstOrDefault()?.Value ?? 0;

                if (refs > 0)
                {
                    await AppendDbLogAsync(dbPath, "REFERENCE_INTEGRITY", "Delete tipo salto bloccato", new[] { $"JumpType id={id} associato a {refs} salto/i" });
                    return (false, $"Impossibile eliminare: tipo salto associato a {refs} salto/i.");
                }

                return (true, null);
            }
            catch
            {
                return (false, "Errore durante il controllo delle associazioni tipo salto.");
            }
        }

        public async Task<bool> DeleteObjectAsync(int id)
        {
            var check = await CanDeleteObjectAsync(id);
            if (!check.CanDelete) return false;

            var dbPath = ResolveDbPath();
            if (!File.Exists(dbPath)) return false;
            try
            {
                var db = new SQLiteAsyncConnection(new SQLiteConnectionString(dbPath, false));
                if (await HasTableAsync(db, "ZOBJECTIMAGE"))
                    await db.ExecuteAsync("DELETE FROM ZOBJECTIMAGE WHERE ZOBJECT=?;", id);
                if (await HasTableAsync(db, "ZOBJECT"))
                    await db.ExecuteAsync("DELETE FROM ZOBJECT WHERE Z_PK=?;", id);
                return true;
            }
            catch { return false; }
        }

        public async Task<bool> DeleteRigAsync(int id)
        {
            var check = await CanDeleteRigAsync(id);
            if (!check.CanDelete) return false;

            var dbPath = ResolveDbPath();
            if (!File.Exists(dbPath)) return false;
            try
            {
                var db = new SQLiteAsyncConnection(new SQLiteConnectionString(dbPath, false));
                if (await HasTableAsync(db, "ZRIG"))
                    await db.ExecuteAsync("DELETE FROM ZRIG WHERE Z_PK=?;", id);
                return true;
            }
            catch { return false; }
        }

        public async Task<bool> DeleteJumpTypeAsync(int id)
        {
            var check = await CanDeleteJumpTypeAsync(id);
            if (!check.CanDelete) return false;

            var dbPath = ResolveDbPath();
            if (!File.Exists(dbPath)) return false;
            try
            {
                var db = new SQLiteAsyncConnection(new SQLiteConnectionString(dbPath, false));
                if (await HasTableAsync(db, "ZJUMPTYPE"))
                    await db.ExecuteAsync("DELETE FROM ZJUMPTYPE WHERE Z_PK=?;", id);
                return true;
            }
            catch { return false; }
        }

        public async Task<bool> ExportLightweightJsonAsync(string filePath)
        {
            try
            {
                var data = await GetJumpsAsync();
                var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(filePath, json);
                await AppendDbLogAsync(ResolveDbPath(), "IMPORT_EXPORT", "Export JSON", new[] { $"Esportati {data.Count} salti in: {filePath}" });
                return true;
            }
            catch (Exception ex)
            {
                await AppendDbLogAsync(ResolveDbPath(), "RUNTIME_ERROR", "Export JSON fallito", new[] { ex.Message });
                return false;
            }
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

                await AppendDbLogAsync(ResolveDbPath(), "IMPORT_EXPORT", "Import JSON", new[] { $"Importati {jumps.Count} salti da: {filePath}", ok ? "Esito: OK" : "Esito: parziale/errore" });
                return ok;
            }
            catch (Exception ex)
            {
                await AppendDbLogAsync(ResolveDbPath(), "RUNTIME_ERROR", "Import JSON fallito", new[] { ex.Message });
                return false;
            }
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

        public async Task<bool> ImportFullDbAsync(string sourcePath)
        {
            try
            {
                var dbPath = ResolveDbPath();
                File.Copy(sourcePath, dbPath, true);
                await AppendDbLogAsync(dbPath, "IMPORT_EXPORT", "Import DB", new[] { $"Importato DB da: {sourcePath}" });
                await NormalizeJumpNumbersAsync();
                return true;
            }
            catch { return false; }
        }

        private static async Task<HashSet<string>> GetTableColumnsAsync(SQLiteAsyncConnection db, string table)
            => (await db.QueryAsync<PragmaColumn>($"PRAGMA table_info('{table}');"))
                .Select(c => c.Name ?? string.Empty)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

        private static string BuildColumnExpression(string alias, HashSet<string> columns, params string[] candidates)
        {
            var col = candidates.FirstOrDefault(columns.Contains);
            return col is null ? "NULL" : $"{alias}.{col}";
        }

        private static string BuildObjectNotes(string? description)
            => string.IsNullOrWhiteSpace(description) ? string.Empty : description.Trim();

        private static (double lat, double lon)? ParseLatLon(string? position)
        {
            if (string.IsNullOrWhiteSpace(position)) return null;
            var parts = position.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2) return null;
            if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var lat)) return null;
            if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var lon)) return null;
            return (lat, lon);
        }

        private static void PopulateObjectFieldsFromNotes(ObjectCatalogItem row)
        {
            if (string.IsNullOrWhiteSpace(row.Description) && !string.IsNullOrWhiteSpace(row.Notes))
                row.Description = row.Notes;

            if (string.IsNullOrWhiteSpace(row.Position) && !string.IsNullOrWhiteSpace(row.Latitude) && !string.IsNullOrWhiteSpace(row.Longitude))
                row.Position = $"{row.Latitude}, {row.Longitude}";

            if (!string.IsNullOrWhiteSpace(row.HeightMeters))
            {
                var cleaned = row.HeightMeters.Trim();
                row.HeightMeters = cleaned.Contains(',') ? cleaned.Replace(',', '.') : cleaned;
            }

            if (string.IsNullOrWhiteSpace(row.HeightUnit))
                row.HeightUnit = "m";
        }

        private static void TryAddObjectField(HashSet<string> columns, List<string> insertColumns, List<object?> values, string c1, object? value)
        {
            if (!columns.Contains(c1)) return;
            insertColumns.Add(c1);
            values.Add(value);
        }

        private static void TryAddObjectField(HashSet<string> columns, List<string> insertColumns, List<object?> values, string c1, string c2, string c3, string c4, string? value)
        {
            var c = new[] { c1, c2, c3, c4 }.FirstOrDefault(columns.Contains);
            if (c is null) return;
            insertColumns.Add(c);
            values.Add(value);
        }

        private static void TryAddObjectField(HashSet<string> columns, List<string> insertColumns, List<object?> values, string c1, string c2, string c3, string? value)
        {
            var c = new[] { c1, c2, c3 }.FirstOrDefault(columns.Contains);
            if (c is null) return;
            insertColumns.Add(c);
            values.Add(value);
        }

        private static void TryAddObjectField(HashSet<string> columns, List<string> insertColumns, List<object?> values, string c1, string c2, string? value)
        {
            var c = new[] { c1, c2 }.FirstOrDefault(columns.Contains);
            if (c is null) return;
            insertColumns.Add(c);
            values.Add(value);
        }

        private static void TryAddFirstAvailableField(HashSet<string> columns, List<string> insertColumns, List<object?> values, object? value, params string[] candidates)
        {
            var c = candidates.FirstOrDefault(columns.Contains);
            if (c is null) return;
            insertColumns.Add(c);
            values.Add(value);
        }

        private static void TryAddFirstAvailableUpdate(HashSet<string> columns, List<string> updates, List<object?> values, object? value, params string[] candidates)
        {
            var c = candidates.FirstOrDefault(columns.Contains);
            if (c is null) return;
            updates.Add($"{c}=?");
            values.Add(value);
        }

        private static void TryAddObjectUpdate(HashSet<string> columns, List<string> updates, List<object?> values, string c1, object? value)
        {
            if (!columns.Contains(c1)) return;
            updates.Add($"{c1}=?");
            values.Add(value);
        }

        private static void TryAddObjectUpdate(HashSet<string> columns, List<string> updates, List<object?> values, string c1, string c2, string c3, string c4, string? value)
        {
            var c = new[] { c1, c2, c3, c4 }.FirstOrDefault(columns.Contains);
            if (c is null) return;
            updates.Add($"{c}=?");
            values.Add(value);
        }

        private static void TryAddObjectUpdate(HashSet<string> columns, List<string> updates, List<object?> values, string c1, string c2, string c3, string? value)
        {
            var c = new[] { c1, c2, c3 }.FirstOrDefault(columns.Contains);
            if (c is null) return;
            updates.Add($"{c}=?");
            values.Add(value);
        }

        private static void TryAddObjectUpdate(HashSet<string> columns, List<string> updates, List<object?> values, string c1, string c2, string? value)
        {
            var c = new[] { c1, c2 }.FirstOrDefault(columns.Contains);
            if (c is null) return;
            updates.Add($"{c}=?");
            values.Add(value);
        }

        private static async Task<string?> GetObjectNameByIdAsync(SQLiteAsyncConnection db, int id)
            => (await db.QueryAsync<ObjectNameRow>("SELECT ZNAME AS Name FROM ZOBJECT WHERE Z_PK=? LIMIT 1;", id)).FirstOrDefault()?.Name;

        private static async Task<string?> GetJumpTypeNameByIdAsync(SQLiteAsyncConnection db, int id)
            => (await db.QueryAsync<ObjectNameRow>("SELECT ZNAME AS Name FROM ZJUMPTYPE WHERE Z_PK=? LIMIT 1;", id)).FirstOrDefault()?.Name;

        private static async Task AppendDbLogAsync(string dbPath, string category, string title, IEnumerable<string> lines)
        {
            try
            {
                var logPath = dbPath + ".log";
                var header = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [CAT:{category}] {title}";
                var content = string.Join(Environment.NewLine, lines.Select(l => $" - {l}"));
                var block = $"{header}{Environment.NewLine}{content}{Environment.NewLine}";
                await File.AppendAllTextAsync(logPath, block + Environment.NewLine);
            }
            catch
            {
                // best-effort log writing
            }
        }

        private static async Task<int> NormalizeSequenceWithDateCheckAsync(
            string dbPath,
            IReadOnlyList<NormalizeRow> rows,
            Func<string?, DateTime?> parseDate,
            Func<int, int, Task> applyUpdate,
            string sourceLabel)
        {
            if (rows.Count == 0)
                return 0;

            var logs = new List<string>();

            // Human-friendly incongruence report: jump number order should follow jump date order.
            for (var i = 1; i < rows.Count; i++)
            {
                var prevDate = parseDate(rows[i - 1].DateText);
                var curDate = parseDate(rows[i].DateText);
                if (prevDate.HasValue && curDate.HasValue && curDate < prevDate)
                {
                    logs.Add($"INCONGRUENZA DATA/NUMERO: #{rows[i - 1].Number} ({prevDate:yyyy-MM-dd HH:mm}) precede #{rows[i].Number} ({curDate:yyyy-MM-dd HH:mm}) ma la data è invertita.");
                }
            }

            var expected = 1;
            var changes = 0;
            foreach (var row in rows)
            {
                if (row.Number > expected)
                {
                    await applyUpdate(row.Pk, expected);
                    logs.Add($"{sourceLabel} pk/id={row.Pk}: buco numerico chiuso {row.Number} -> {expected} (shift successivi)");
                    changes++;
                }
                else if (row.Number < expected)
                {
                    // Duplicate/out-of-order numbers: do not auto-fix aggressively, only report.
                    logs.Add($"{sourceLabel} pk/id={row.Pk}: numero {row.Number} inferiore all'atteso {expected} (verifica manuale consigliata)");
                }

                expected++;
            }

            if (logs.Count > 0)
                await AppendDbLogAsync(dbPath, "DATA_CONSISTENCY", "Controllo coerenza numeri salto", logs);

            return changes;
        }

        private static DateTime? AppleSecondsToDateTime(string? text)
        {
            if (string.IsNullOrWhiteSpace(text) || !double.TryParse(text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
                return null;
            return new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(seconds).ToLocalTime();
        }

        private static DateTime? UnixSecondsToDateTime(string? text)
        {
            if (string.IsNullOrWhiteSpace(text) || !long.TryParse(text, out var seconds))
                return null;
            return DateTimeOffset.FromUnixTimeSeconds(seconds).ToLocalTime().DateTime;
        }

        public async Task<IReadOnlyList<string>> GetJumpTypeNamesAsync()
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
            return dt.ToString("dd/MM/yyyy HH:mm");
        }

        private static string UnixSecondsToDisplay(string? text)
            => string.IsNullOrWhiteSpace(text) || !long.TryParse(text, out var s) ? "" : DateTimeOffset.FromUnixTimeSeconds(s).ToLocalTime().ToString("dd/MM/yyyy HH:mm");

        private static long ToUnixSeconds(string? displayDate)
            => DateTime.TryParseExact(displayDate, new[] { "dd/MM/yyyy HH:mm", "dd/MM/yyyy" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
                ? new DateTimeOffset(parsed).ToUnixTimeSeconds()
                : DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        private static double ToAppleSeconds(string? displayDate)
        {
            if (!DateTime.TryParseExact(displayDate, new[] { "dd/MM/yyyy HH:mm", "dd/MM/yyyy" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
                parsed = DateTime.UtcNow;
            return (parsed.ToUniversalTime() - new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
        }

        private static double? ToNullableDouble(string? text)
            => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : null;

        private static int? ParseNullableInt(string? text)
            => int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;

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
            public string? DelaySecondsText { get; set; }
            public string? HeadingDegreesText { get; set; }
        }

        private sealed class ObjectNameRow { public string? Name { get; set; } }
        private sealed class PragmaColumn { public string? Name { get; set; } }
        private sealed class ScalarInt { public int Value { get; set; } }
        private sealed class ObjectCoordRow { public double? Latitude { get; set; } public double? Longitude { get; set; } }
        private sealed class NormalizeRow { public int Pk { get; set; } public int Number { get; set; } public string? DateText { get; set; } }
    }

    public async Task<bool> AddRigAsync(string name, string? description)
    {
        var db = await TryOpenDbAsync();
        if (db is null || string.IsNullOrWhiteSpace(name)) return false;

        try
        {
            if (await HasTableAsync(db, "Rig"))
                return await db.ExecuteAsync("INSERT INTO Rig (Name, Description) VALUES (?, ?);", name.Trim(), description) > 0;

            if (await HasTableAsync(db, "ZRIG"))
                return await db.ExecuteAsync("INSERT INTO ZRIG (ZNAME, ZDESCRIPTION) VALUES (?, ?);", name.Trim(), description) > 0;

            return false;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> AddJumpTypeAsync(string name, string? notes)
    {
        var db = await TryOpenDbAsync();
        if (db is null || string.IsNullOrWhiteSpace(name)) return false;

        try
        {
            if (await HasTableAsync(db, "JumpType"))
                return await db.ExecuteAsync("INSERT INTO JumpType (Name, Notes) VALUES (?, ?);", name.Trim(), notes) > 0;

            if (await HasTableAsync(db, "ZJUMPTYPE"))
                return await db.ExecuteAsync("INSERT INTO ZJUMPTYPE (ZNAME, ZNOTES) VALUES (?, ?);", name.Trim(), notes) > 0;

            return false;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> UpdateObjectAsync(int id, string name, string? objectType, string? description, string? position, string? heightMeters, byte[]? photoBytes)
    {
        var db = await TryOpenDbAsync();
        if (db is null || string.IsNullOrWhiteSpace(name)) return false;

        try
        {
            if (await HasTableAsync(db, "Object"))
            {
                var rows = await db.ExecuteAsync(
                    "UPDATE Object SET Name = ?, ObjectType = ?, Description = ?, Position = ?, HeightMeters = ?, PhotoBlob = COALESCE(?, PhotoBlob) WHERE Id = ?;",
                    name.Trim(), objectType, description, position, heightMeters, photoBytes, id);
                return rows > 0;
            }

            if (await HasTableAsync(db, "ZOBJECT"))
            {
                var rows = await db.ExecuteAsync(
                    "UPDATE ZOBJECT SET ZNAME = ?, ZOBJECTTYPE = ?, ZDESCRIPTION = ?, ZPOSITION = ?, ZHEIGHT = ? WHERE Z_PK = ?;",
                    name.Trim(), objectType, description, position, heightMeters, id);

                if (rows > 0 && photoBytes is { Length: > 0 } && await HasTableAsync(db, "ZOBJECTIMAGE"))
                {
                    await db.ExecuteAsync("DELETE FROM ZOBJECTIMAGE WHERE ZOBJECT = ?;", id);
                    await db.ExecuteAsync("INSERT INTO ZOBJECTIMAGE (ZOBJECT, ZIMAGE) VALUES (?, ?);", id, photoBytes);
                }

                return rows > 0;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> UpdateRigAsync(int id, string name, string? description)
    {
        var db = await TryOpenDbAsync();
        if (db is null || string.IsNullOrWhiteSpace(name)) return false;

        try
        {
            if (await HasTableAsync(db, "Rig"))
                return await db.ExecuteAsync("UPDATE Rig SET Name = ?, Description = ? WHERE Id = ?;", name.Trim(), description, id) > 0;

            if (await HasTableAsync(db, "ZRIG"))
                return await db.ExecuteAsync("UPDATE ZRIG SET ZNAME = ?, ZDESCRIPTION = ? WHERE Z_PK = ?;", name.Trim(), description, id) > 0;

            return false;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> UpdateJumpTypeAsync(int id, string name, string? notes)
    {
        var db = await TryOpenDbAsync();
        if (db is null || string.IsNullOrWhiteSpace(name)) return false;

        try
        {
            if (await HasTableAsync(db, "JumpType"))
                return await db.ExecuteAsync("UPDATE JumpType SET Name = ?, Notes = ? WHERE Id = ?;", name.Trim(), notes, id) > 0;

            if (await HasTableAsync(db, "ZJUMPTYPE"))
                return await db.ExecuteAsync("UPDATE ZJUMPTYPE SET ZNAME = ?, ZNOTES = ? WHERE Z_PK = ?;", name.Trim(), notes, id) > 0;

            return false;
        }
        catch
        {
            return false;
        }
    }

    public async Task<int> NormalizeJumpNumbersAsync()
    {
        var db = await TryOpenDbAsync();
        if (db is null) return 0;

        try
        {
            if (await HasTableAsync(db, "Jump"))
            {
                var rows = await db.QueryAsync<IdRow>("SELECT Id FROM Jump ORDER BY Id;");
                var expected = 1;
                var changes = 0;
                foreach (var row in rows)
                {
                    if (row.Id == expected)
                    {
                        expected++;
                        continue;
                    }

                    await db.ExecuteAsync("UPDATE Jump SET Id = ? WHERE Id = ?;", expected, row.Id);
                    expected++;
                    changes++;
                }
                return changes;
            }

            if (await HasTableAsync(db, "ZLOGENTRY"))
            {
                var rows = await db.QueryAsync<LegacyJumpNumberRow>("SELECT Z_PK, ZJUMPNUMBER FROM ZLOGENTRY ORDER BY ZJUMPNUMBER, Z_PK;");
                var expected = 1;
                var changes = 0;
                foreach (var row in rows)
                {
                    if (row.Number == expected)
                    {
                        expected++;
                        continue;
                    }

                    await db.ExecuteAsync("UPDATE ZLOGENTRY SET ZJUMPNUMBER = ? WHERE Z_PK = ?;", expected, row.Pk);
                    expected++;
                    changes++;
                }
                return changes;
            }

            return 0;
        }
        catch
        {
            return 0;
        }
    }

    public async Task<(bool CanDelete, string? Reason)> CanDeleteObjectAsync(int id)
    {
        var db = await TryOpenDbAsync();
        if (db is null) return (false, "Database not available");

        try
        {
            if (await HasTableAsync(db, "Jump") && await HasTableAsync(db, "Object"))
            {
                var name = await db.ExecuteScalarAsync<string?>("SELECT Name FROM Object WHERE Id = ? LIMIT 1;", id);
                if (string.IsNullOrWhiteSpace(name)) return (true, null);
                var refs = await db.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM Jump WHERE lower(trim(ObjectName)) = lower(trim(?));", name);
                return refs == 0 ? (true, null) : (false, "Object is used by one or more jumps");
            }

            if (await HasTableAsync(db, "ZLOGENTRY"))
            {
                var refs = await db.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM ZLOGENTRY WHERE ZOBJECT = ?;", id);
                return refs == 0 ? (true, null) : (false, "Object is used by one or more jumps");
            }

            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    public async Task<(bool CanDelete, string? Reason)> CanDeleteRigAsync(int id)
    {
        var db = await TryOpenDbAsync();
        if (db is null) return (false, "Database not available");

        try
        {
            if (await HasTableAsync(db, "ZLOGENTRY") && await HasTableAsync(db, "ZRIG"))
            {
                var refs = await db.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM ZLOGENTRY WHERE ZRIG = ?;", id);
                return refs == 0 ? (true, null) : (false, "Rig is used by one or more jumps");
            }

            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    public async Task<(bool CanDelete, string? Reason)> CanDeleteJumpTypeAsync(int id)
    {
        var db = await TryOpenDbAsync();
        if (db is null) return (false, "Database not available");

        try
        {
            if (await HasTableAsync(db, "Jump") && await HasTableAsync(db, "JumpType"))
            {
                var name = await db.ExecuteScalarAsync<string?>("SELECT Name FROM JumpType WHERE Id = ? LIMIT 1;", id);
                if (string.IsNullOrWhiteSpace(name)) return (true, null);
                var refs = await db.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM Jump WHERE lower(trim(ExitName)) = lower(trim(?));", name);
                return refs == 0 ? (true, null) : (false, "Jump type is used by one or more jumps");
            }

            if (await HasTableAsync(db, "ZLOGENTRY"))
            {
                var refs = await db.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM ZLOGENTRY WHERE ZJUMPTYPE = ?;", id);
                return refs == 0 ? (true, null) : (false, "Jump type is used by one or more jumps");
            }

            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    public async Task<bool> DeleteObjectAsync(int id)
    {
        var decision = await CanDeleteObjectAsync(id);
        if (!decision.CanDelete) return false;

        var db = await TryOpenDbAsync();
        if (db is null) return false;

        if (await HasTableAsync(db, "Object"))
            return await db.ExecuteAsync("DELETE FROM Object WHERE Id = ?;", id) > 0;

        if (await HasTableAsync(db, "ZOBJECT"))
        {
            if (await HasTableAsync(db, "ZOBJECTIMAGE"))
                await db.ExecuteAsync("DELETE FROM ZOBJECTIMAGE WHERE ZOBJECT = ?;", id);
            return await db.ExecuteAsync("DELETE FROM ZOBJECT WHERE Z_PK = ?;", id) > 0;
        }

        return false;
    }

    public async Task<bool> DeleteRigAsync(int id)
    {
        var decision = await CanDeleteRigAsync(id);
        if (!decision.CanDelete) return false;

        var db = await TryOpenDbAsync();
        if (db is null) return false;

        if (await HasTableAsync(db, "Rig"))
            return await db.ExecuteAsync("DELETE FROM Rig WHERE Id = ?;", id) > 0;

        if (await HasTableAsync(db, "ZRIG"))
            return await db.ExecuteAsync("DELETE FROM ZRIG WHERE Z_PK = ?;", id) > 0;

        return false;
    }

    public async Task<bool> DeleteJumpTypeAsync(int id)
    {
        var decision = await CanDeleteJumpTypeAsync(id);
        if (!decision.CanDelete) return false;

        var db = await TryOpenDbAsync();
        if (db is null) return false;

        if (await HasTableAsync(db, "JumpType"))
            return await db.ExecuteAsync("DELETE FROM JumpType WHERE Id = ?;", id) > 0;

        if (await HasTableAsync(db, "ZJUMPTYPE"))
            return await db.ExecuteAsync("DELETE FROM ZJUMPTYPE WHERE Z_PK = ?;", id) > 0;

        return false;
    }

    public async Task<bool> ExportLightweightJsonAsync(string filePath)
    {
        try
        {
            var payload = new LightweightExport
            {
                ExportedAtUtc = DateTime.UtcNow,
                Jumps = (await GetJumpsAsync()).ToList(),
                Objects = (await GetObjectsCatalogAsync()).ToList(),
                Rigs = (await GetRigsCatalogAsync()).ToList(),
                JumpTypes = (await GetJumpTypesCatalogAsync()).ToList()
            };

            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(filePath, json);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> ImportLightweightJsonAsync(string filePath)
    {
        try
        {
            if (!File.Exists(filePath)) return false;

            var json = await File.ReadAllTextAsync(filePath);
            var payload = JsonSerializer.Deserialize<LightweightExport>(json);
            if (payload is null) return false;

            foreach (var item in payload.Objects)
                await AddObjectAsync(item.Name, item.ObjectType, item.Description, item.Position, item.HeightMeters, item.PhotoBlob);

            foreach (var item in payload.Rigs)
                await AddRigAsync(item.Name, item.Notes);

            foreach (var item in payload.JumpTypes)
                await AddJumpTypeAsync(item.Name, item.Notes);

            foreach (var jump in payload.Jumps.OrderBy(x => x.NumeroSalto))
                await AddJumpAsync(jump);

            return true;
        }
        catch
        {
            return false;
        }
    }

    public Task<bool> ExportFullDbAsync(string destinationPath)
    {
        try
        {
            var sourcePath = ResolveDbPath();
            if (!File.Exists(sourcePath))
                return Task.FromResult(false);

            File.Copy(sourcePath, destinationPath, overwrite: true);
            return Task.FromResult(true);
        }
        catch
        {
            return Task.FromResult(false);
        }
    }

    public Task<bool> ImportFullDbAsync(string sourcePath)
    {
        try
        {
            if (!File.Exists(sourcePath))
                return Task.FromResult(false);

            var destinationPath = ResolveDbPath();
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            File.Copy(sourcePath, destinationPath, overwrite: true);
            return Task.FromResult(true);
        }
        catch
        {
            return Task.FromResult(false);
        }
    }

    private async Task<IReadOnlyList<JumpListItem>> GetModernJumpsAsync(SQLiteAsyncConnection db)
    {
        var sql = @"
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
    CAST(Longitude AS TEXT) AS Longitude,
    NULL AS DelaySecondsText,
    NULL AS HeadingDegreesText
FROM Jump
ORDER BY Id DESC;";
        var rows = await db.QueryAsync<JumpRow>(sql);
        return rows.Select(ToModernJumpItem).ToList();
    }

    private async Task<IReadOnlyList<JumpListItem>> GetLegacyJumpsAsync(SQLiteAsyncConnection db)
    {
        var objectColumns = await GetTableColumnsAsync(db, "ZOBJECT");
        var photoExpr = ColumnExpr("o", objectColumns, "ZPHOTOPATH", "ZIMAGEPATH");

        var logColumns = await GetTableColumnsAsync(db, "ZLOGENTRY");
        var delayExpr = ColumnExpr("l", logColumns, "ZDELAY", "ZDELAYSECONDS", "ZDELAYINSECONDS");
        var headingExpr = ColumnExpr("l", logColumns, "ZHEADING", "ZOPENINGHEADING", "ZTRACK");

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
    NULL AS Longitude,
    CAST({delayExpr} AS TEXT) AS DelaySecondsText,
    CAST({headingExpr} AS TEXT) AS HeadingDegreesText
FROM ZLOGENTRY l
LEFT JOIN ZOBJECT o ON o.Z_PK = l.ZOBJECT
LEFT JOIN ZJUMPTYPE jt ON jt.Z_PK = l.ZJUMPTYPE
ORDER BY l.ZJUMPNUMBER DESC;";

        var rows = await db.QueryAsync<JumpRow>(sql);
        return rows.Select(ToLegacyJumpItem).ToList();
    }

    private static JumpListItem ToModernJumpItem(JumpRow row)
        => new()
        {
            Id = row.Id,
            NumeroSalto = row.NumeroSalto,
            Data = FromUnixSecondsToDisplay(row.DateText),
            Oggetto = row.Oggetto,
            TipoSalto = row.TipoSalto,
            Note = row.Note,
            ObjectPhotoPath = NormalizePhotoPath(row.ObjectPhotoPath),
            Latitude = row.Latitude,
            Longitude = row.Longitude,
            DelaySeconds = ParseNullableInt(row.DelaySecondsText),
            HeadingDegrees = ParseNullableInt(row.HeadingDegreesText)
        };

    private static JumpListItem ToLegacyJumpItem(JumpRow row)
        => new()
        {
            Id = row.Id,
            NumeroSalto = row.NumeroSalto,
            Data = FromAppleSecondsToDisplay(row.DateText),
            Oggetto = row.Oggetto,
            TipoSalto = row.TipoSalto,
            Note = row.Note,
            ObjectPhotoPath = NormalizePhotoPath(row.ObjectPhotoPath),
            ObjectPhotoBlob = row.ObjectPhotoBlob,
            JumpPhotoBlob = row.JumpPhotoBlob,
            Latitude = row.Latitude,
            Longitude = row.Longitude,
            DelaySeconds = ParseNullableInt(row.DelaySecondsText),
            HeadingDegrees = ParseNullableInt(row.HeadingDegreesText)
        };

    private string ResolveDbPath()
    {
        var appDataPath = Path.Combine(FileSystem.AppDataDirectory, DefaultDbName);

        if (_profile == DbProfile.Modern)
            return appDataPath;

        if (File.Exists(_legacyFallbackWindowsPath))
            return _legacyFallbackWindowsPath;

        return appDataPath;
    }

    private SQLiteAsyncConnection Open(string dbPath)
        => new(new SQLiteConnectionString(dbPath, storeDateTimeAsTicks: false));

    private async Task<SQLiteAsyncConnection?> TryOpenDbAsync()
    {
        var dbPath = ResolveDbPath();
        if (!File.Exists(dbPath)) return null;

        try
        {
            return Open(dbPath);
        }
        catch
        {
            return null;
        }
    }

    private static async Task<bool> HasTableAsync(SQLiteAsyncConnection db, string tableName)
        => await db.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM sqlite_master WHERE type='table' AND name=?;", tableName) > 0;

    private static async Task<HashSet<string>> GetTableColumnsAsync(SQLiteAsyncConnection db, string tableName)
    {
        var rows = await db.QueryAsync<TableInfoRow>($"PRAGMA table_info('{tableName}');");
        return rows
            .Select(x => x.Name)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.ToUpperInvariant())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static string ColumnExpr(string alias, HashSet<string> columns, params string[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (columns.Contains(candidate))
                return $"{alias}.{candidate}";
        }

        return "NULL";
    }

    private static string? NormalizePhotoPath(string? path)
        => string.IsNullOrWhiteSpace(path) ? null : path.Trim();

    private static int? ParseNullableInt(string? value)
        => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number : null;

    private static string FromUnixSecondsToDisplay(string? value)
    {
        if (!long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds))
            return string.Empty;

        return DateTimeOffset.FromUnixTimeSeconds(seconds).ToLocalTime().ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);
    }

    private static string FromAppleSecondsToDisplay(string? value)
    {
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
            return string.Empty;

        var date = DateTime.UnixEpoch.AddSeconds(seconds + 978307200d).ToLocalTime();
        return date.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);
    }

    private static DateTime? ParseDisplayDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        if (DateTime.TryParseExact(value, new[] { "dd/MM/yyyy HH:mm", "dd/MM/yyyy" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            return parsed;

        return DateTime.TryParse(value, CultureInfo.CurrentCulture, DateTimeStyles.None, out parsed) ? parsed : null;
    }

    private static long? ParseDisplayDateToUnixSeconds(string? value)
    {
        var date = ParseDisplayDate(value);
        return date is null ? null : new DateTimeOffset(date.Value).ToUnixTimeSeconds();
    }

    private static double ToAppleReferenceSeconds(DateTime date)
        => (date.ToUniversalTime() - new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;

    private static double? ToNullableDouble(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result)
            ? result
            : null;
    }

    private static ObjectCatalogItem ToObjectCatalogItem(ObjectCatalogRow row)
        => new()
        {
            Id = row.Id,
            Name = row.Name ?? string.Empty,
            ObjectType = row.ObjectType,
            Description = row.Notes,
            HeightMeters = row.HeightMeters,
            HeightUnit = row.HeightUnit,
            Position = row.Position,
            Latitude = row.Latitude,
            Longitude = row.Longitude,
            PhotoBlob = row.PhotoBlob
        };

    private static async Task<int?> FindLegacyIdByNameAsync(SQLiteAsyncConnection db, string tableName, string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;

        var id = await db.ExecuteScalarAsync<int?>(
            $"SELECT Z_PK FROM {tableName} WHERE lower(trim(ZNAME)) = lower(trim(?)) LIMIT 1;",
            name.Trim());

        return id;
    }

    private sealed class NameRow
    {
        public string Name { get; set; } = string.Empty;
    }

    private sealed class CatalogRow
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public string? Notes { get; set; }
    }

    private sealed class CoordinateRow
    {
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
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
        public byte[]? ObjectPhotoBlob { get; set; }
        public byte[]? JumpPhotoBlob { get; set; }
        public string? Latitude { get; set; }
        public string? Longitude { get; set; }
        public string? DelaySecondsText { get; set; }
        public string? HeadingDegreesText { get; set; }
    }

    private sealed class ObjectCatalogRow
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public string? ObjectType { get; set; }
        public string? Notes { get; set; }
        public string? HeightMeters { get; set; }
        public string? HeightUnit { get; set; }
        public string? Position { get; set; }
        public string? Latitude { get; set; }
        public string? Longitude { get; set; }
        public byte[]? PhotoBlob { get; set; }
    }

    private sealed class TableInfoRow
    {
        public int Cid { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public int NotNull { get; set; }
        public string? DfltValue { get; set; }
        public int Pk { get; set; }
    }

    private sealed class LegacyJumpNumberRow
    {
        public int Pk { get; set; }
        public int Number { get; set; }
    }

    private sealed class IdRow
    {
        public int Id { get; set; }
    }

    private sealed class LightweightExport
    {
        public DateTime ExportedAtUtc { get; set; }
        public List<JumpListItem> Jumps { get; set; } = new();
        public List<ObjectCatalogItem> Objects { get; set; } = new();
        public List<CatalogItem> Rigs { get; set; } = new();
        public List<CatalogItem> JumpTypes { get; set; } = new();
    }
}
