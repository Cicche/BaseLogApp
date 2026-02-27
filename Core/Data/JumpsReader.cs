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

    private const int EntJumpType = 3;
    private const int EntLogEntry = 5;
    private const int EntLogEntryImage = 6;
    private const int EntObject = 7;
    private const int EntObjectImage = 8;
    private const int EntRig = 10;

    public void SetDbProfile(DbProfile profile) => _profile = profile;
    public string GetCurrentDbPath() => ResolveDbPath();

    private string ResolveDbPath()
    {
        var appDataPath = Path.Combine(FileSystem.AppDataDirectory, DefaultDbName);

        if (_profile == DbProfile.Modern)
            return appDataPath;

        if (File.Exists(_legacyFallbackWindowsPath))
            return _legacyFallbackWindowsPath;

        return appDataPath;
    }

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
                var rows = await db.QueryAsync<NameRow>("SELECT ZNAME AS Name FROM ZOBJECT WHERE Z_ENT = 7 AND ZNAME IS NOT NULL AND TRIM(ZNAME) <> ''; ");
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
                return (await db.QueryAsync<NameRow>("SELECT ZNAME AS Name FROM ZJUMPTYPE WHERE Z_ENT = 3 AND ZNAME IS NOT NULL AND TRIM(ZNAME) <> '';"))
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
                return (await db.QueryAsync<NameRow>("SELECT ZNAME AS Name FROM ZRIG WHERE Z_ENT = 10 AND ZNAME IS NOT NULL AND TRIM(ZNAME) <> '';"))
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
                var descExpr = ColumnExpr("o", objectCols, "ZNOTES", "ZDESCRIPTION", "ZDESC");
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
    (SELECT oi.ZIMAGE FROM ZOBJECTIMAGE oi WHERE oi.ZOBJECT = o.Z_PK AND oi.Z_ENT = 8 LIMIT 1) AS PhotoBlob
FROM ZOBJECT o
WHERE o.Z_ENT = 7
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
                    $"SELECT {latExpr} AS Latitude, {lonExpr} AS Longitude FROM ZOBJECT o WHERE o.Z_ENT = 7 AND lower(trim(o.ZNAME)) = lower(trim(?)) LIMIT 1;",
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
                var rows = await db.QueryAsync<CatalogRow>($"SELECT r.Z_PK AS Id, r.ZNAME AS Name, {notesExpr} AS Notes FROM ZRIG r WHERE r.Z_ENT = 10 ORDER BY r.ZNAME COLLATE NOCASE;");
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
                var rows = await db.QueryAsync<CatalogRow>($"SELECT jt.Z_PK AS Id, jt.ZNAME AS Name, {notesExpr} AS Notes FROM ZJUMPTYPE jt WHERE jt.Z_ENT = 3 ORDER BY jt.ZNAME COLLATE NOCASE;");
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

                await db.RunInTransactionAsync(conn =>
                {
                    var pk = AllocateCoreDataPk(conn, EntLogEntry, "ZLOGENTRY");
                    conn.Execute(
                        "INSERT INTO ZLOGENTRY (Z_PK, Z_ENT, Z_OPT, ZJUMPNUMBER, ZDATE, ZOBJECT, ZJUMPTYPE, ZNOTES) VALUES (?, ?, 1, ?, ?, ?, ?, ?);",
                        pk,
                        EntLogEntry,
                        jump.NumeroSalto,
                        appleSeconds,
                        objectId,
                        typeId,
                        jump.Note);
                });
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
                    "UPDATE ZLOGENTRY SET Z_OPT = COALESCE(Z_OPT, 0) + 1, ZJUMPNUMBER = ?, ZDATE = ?, ZOBJECT = ?, ZJUMPTYPE = ?, ZNOTES = ? WHERE Z_PK = ? AND Z_ENT = ?;",
                    jump.NumeroSalto,
                    appleSeconds,
                    objectId,
                    typeId,
                    jump.Note,
                    jump.Id,
                    EntLogEntry);

                if (rows > 0 && jump.NewPhotoBytes is { Length: > 0 })
                {
                    if (await HasTableAsync(db, "ZLOGENTRYIMAGE"))
                    {
                        await db.RunInTransactionAsync(conn =>
                        {
                            conn.Execute("DELETE FROM ZLOGENTRYIMAGE WHERE ZLOGENTRY = ? AND Z_ENT = ?;", jump.Id, EntLogEntryImage);
                            var imagePk = AllocateCoreDataPk(conn, EntLogEntryImage, "ZLOGENTRYIMAGE");
                            conn.Execute("INSERT INTO ZLOGENTRYIMAGE (Z_PK, Z_ENT, Z_OPT, ZLOGENTRY, ZIMAGE) VALUES (?, ?, 1, ?, ?);", imagePk, EntLogEntryImage, jump.Id, jump.NewPhotoBytes);
                        });
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
                    await db.ExecuteAsync("DELETE FROM ZLOGENTRYIMAGE WHERE ZLOGENTRY = ? AND Z_ENT = ?;", jump.Id, EntLogEntryImage);
                return await db.ExecuteAsync("DELETE FROM ZLOGENTRY WHERE Z_PK = ? AND Z_ENT = ?;", jump.Id, EntLogEntry) > 0;
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
                        "UPDATE ZLOGENTRY SET ZJUMPNUMBER = ZJUMPNUMBER + 1 WHERE Z_ENT = ? AND ZJUMPNUMBER >= ? AND Z_PK <> ?;",
                        EntLogEntry,
                        fromNumber,
                        excludeId.Value);
                }
                else
                {
                    await db.ExecuteAsync("UPDATE ZLOGENTRY SET ZJUMPNUMBER = ZJUMPNUMBER + 1 WHERE Z_ENT = ? AND ZJUMPNUMBER >= ?;", EntLogEntry, fromNumber);
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
                var hasObjectImages = await HasTableAsync(db, "ZOBJECTIMAGE");
                await db.RunInTransactionAsync(conn =>
                {
                    var objectPk = AllocateCoreDataPk(conn, EntObject, "ZOBJECT");
                    conn.Execute(
                        "INSERT INTO ZOBJECT (Z_PK, Z_ENT, Z_OPT, ZNAME, ZOBJECTTYPE, ZNOTES, ZPOSITION, ZHEIGHT) VALUES (?, ?, 1, ?, ?, ?, ?, ?);",
                        objectPk,
                        EntObject,
                        name.Trim(),
                        objectType,
                        description,
                        position,
                        heightMeters);

                    if (photoBytes is { Length: > 0 } && hasObjectImages)
                    {
                        var imagePk = AllocateCoreDataPk(conn, EntObjectImage, "ZOBJECTIMAGE");
                        conn.Execute("INSERT INTO ZOBJECTIMAGE (Z_PK, Z_ENT, Z_OPT, ZOBJECT, ZIMAGE) VALUES (?, ?, 1, ?, ?);", imagePk, EntObjectImage, objectPk, photoBytes);
                    }
                });

                return true;
            }

            return false;
        }
        catch
        {
            return false;
        }
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
            {
                await db.RunInTransactionAsync(conn =>
                {
                    var rigPk = AllocateCoreDataPk(conn, EntRig, "ZRIG");
                    conn.Execute("INSERT INTO ZRIG (Z_PK, Z_ENT, Z_OPT, ZNAME, ZNOTES) VALUES (?, ?, 1, ?, ?);", rigPk, EntRig, name.Trim(), description);
                });
                return true;
            }

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
            {
                await db.RunInTransactionAsync(conn =>
                {
                    var jumpTypePk = AllocateCoreDataPk(conn, EntJumpType, "ZJUMPTYPE");
                    conn.Execute("INSERT INTO ZJUMPTYPE (Z_PK, Z_ENT, Z_OPT, ZNAME, ZNOTES) VALUES (?, ?, 1, ?, ?);", jumpTypePk, EntJumpType, name.Trim(), notes);
                });
                return true;
            }

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
                    "UPDATE ZOBJECT SET Z_OPT = COALESCE(Z_OPT, 0) + 1, ZNAME = ?, ZOBJECTTYPE = ?, ZNOTES = ?, ZPOSITION = ?, ZHEIGHT = ? WHERE Z_PK = ? AND Z_ENT = ?;",
                    name.Trim(), objectType, description, position, heightMeters, id, EntObject);

                if (rows > 0 && photoBytes is { Length: > 0 } && await HasTableAsync(db, "ZOBJECTIMAGE"))
                {
                    await db.RunInTransactionAsync(conn =>
                    {
                        conn.Execute("DELETE FROM ZOBJECTIMAGE WHERE ZOBJECT = ? AND Z_ENT = ?;", id, EntObjectImage);
                        var imagePk = AllocateCoreDataPk(conn, EntObjectImage, "ZOBJECTIMAGE");
                        conn.Execute("INSERT INTO ZOBJECTIMAGE (Z_PK, Z_ENT, Z_OPT, ZOBJECT, ZIMAGE) VALUES (?, ?, 1, ?, ?);", imagePk, EntObjectImage, id, photoBytes);
                    });
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
                return await db.ExecuteAsync("UPDATE ZRIG SET Z_OPT = COALESCE(Z_OPT, 0) + 1, ZNAME = ?, ZNOTES = ? WHERE Z_PK = ? AND Z_ENT = ?;", name.Trim(), description, id, EntRig) > 0;

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
                return await db.ExecuteAsync("UPDATE ZJUMPTYPE SET Z_OPT = COALESCE(Z_OPT, 0) + 1, ZNAME = ?, ZNOTES = ? WHERE Z_PK = ? AND Z_ENT = ?;", name.Trim(), notes, id, EntJumpType) > 0;

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
                var rows = await db.QueryAsync<LegacyJumpNumberRow>("SELECT Z_PK, ZJUMPNUMBER FROM ZLOGENTRY WHERE Z_ENT = 5 ORDER BY ZJUMPNUMBER, Z_PK;");
                var expected = 1;
                var changes = 0;
                foreach (var row in rows)
                {
                    if (row.Number == expected)
                    {
                        expected++;
                        continue;
                    }

                    await db.ExecuteAsync("UPDATE ZLOGENTRY SET ZJUMPNUMBER = ? WHERE Z_PK = ? AND Z_ENT = ?;", expected, row.Pk, EntLogEntry);
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
                var refs = await db.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM ZLOGENTRY WHERE Z_ENT = ? AND ZOBJECT = ?;", EntLogEntry, id);
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
                var refs = await db.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM ZLOGENTRY WHERE Z_ENT = ? AND ZRIG = ?;", EntLogEntry, id);
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
                var refs = await db.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM ZLOGENTRY WHERE Z_ENT = ? AND ZJUMPTYPE = ?;", EntLogEntry, id);
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
                await db.ExecuteAsync("DELETE FROM ZOBJECTIMAGE WHERE ZOBJECT = ? AND Z_ENT = ?;", id, EntObjectImage);
            return await db.ExecuteAsync("DELETE FROM ZOBJECT WHERE Z_PK = ? AND Z_ENT = ?;", id, EntObject) > 0;
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
            return await db.ExecuteAsync("DELETE FROM ZRIG WHERE Z_PK = ? AND Z_ENT = ?;", id, EntRig) > 0;

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
            return await db.ExecuteAsync("DELETE FROM ZJUMPTYPE WHERE Z_PK = ? AND Z_ENT = ?;", id, EntJumpType) > 0;

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
    (SELECT oi.ZIMAGE FROM ZOBJECTIMAGE oi WHERE oi.ZOBJECT = o.Z_PK AND oi.Z_ENT = 8 LIMIT 1) AS ObjectPhotoBlob,
    (SELECT li.ZIMAGE FROM ZLOGENTRYIMAGE li WHERE li.ZLOGENTRY = l.Z_PK AND li.Z_ENT = 6 LIMIT 1) AS JumpPhotoBlob,
    NULL AS Latitude,
    NULL AS Longitude,
    CAST({delayExpr} AS TEXT) AS DelaySecondsText,
    CAST({headingExpr} AS TEXT) AS HeadingDegreesText
FROM ZLOGENTRY l
LEFT JOIN ZOBJECT o ON o.Z_PK = l.ZOBJECT AND o.Z_ENT = 7
LEFT JOIN ZJUMPTYPE jt ON jt.Z_PK = l.ZJUMPTYPE AND jt.Z_ENT = 3
WHERE l.Z_ENT = 5
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

        var ent = LegacyEntityForTable(tableName);
        if (ent.HasValue)
        {
            return await db.ExecuteScalarAsync<int?>(
                $"SELECT Z_PK FROM {tableName} WHERE Z_ENT = ? AND lower(trim(ZNAME)) = lower(trim(?)) LIMIT 1;",
                ent.Value,
                name.Trim());
        }

        return await db.ExecuteScalarAsync<int?>(
            $"SELECT Z_PK FROM {tableName} WHERE lower(trim(ZNAME)) = lower(trim(?)) LIMIT 1;",
            name.Trim());
    }

    private static int? LegacyEntityForTable(string tableName)
        => tableName.ToUpperInvariant() switch
        {
            "ZJUMPTYPE" => EntJumpType,
            "ZOBJECT" => EntObject,
            "ZRIG" => EntRig,
            "ZLOGENTRY" => EntLogEntry,
            "ZLOGENTRYIMAGE" => EntLogEntryImage,
            "ZOBJECTIMAGE" => EntObjectImage,
            _ => null
        };

    private static int AllocateCoreDataPk(SQLiteConnection conn, int entityId, string tableName)
    {
        var currentMax = conn.ExecuteScalar<int?>("SELECT Z_MAX FROM Z_PRIMARYKEY WHERE Z_ENT = ? LIMIT 1;", entityId);
        var next = (currentMax ?? conn.ExecuteScalar<int?>($"SELECT IFNULL(MAX(Z_PK), 0) FROM {tableName};") ?? 0) + 1;

        if (currentMax.HasValue)
        {
            conn.Execute("UPDATE Z_PRIMARYKEY SET Z_MAX = ? WHERE Z_ENT = ?;", next, entityId);
        }
        else
        {
            conn.Execute("INSERT INTO Z_PRIMARYKEY (Z_ENT, Z_NAME, Z_SUPER, Z_MAX) VALUES (?, ?, 0, ?);", entityId, LegacyPrimaryKeyNameForTable(tableName), next);
        }

        return next;
    }

    private static string LegacyPrimaryKeyNameForTable(string tableName)
        => tableName.ToUpperInvariant() switch
        {
            "ZLOGENTRY" => "LogEntry",
            "ZLOGENTRYIMAGE" => "LogEntryImage",
            "ZOBJECT" => "Object",
            "ZOBJECTIMAGE" => "ObjectImage",
            "ZRIG" => "Rig",
            "ZJUMPTYPE" => "JumpType",
            _ => tableName.TrimStart('Z')
        };

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
