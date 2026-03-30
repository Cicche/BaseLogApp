using BaseLogApp.Core.Diagnostics;
using BaseLogApp.Core.Models;
using SQLite;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
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
    bool SetCustomDbPath(string? dbPath);
    string GetCurrentDbPath();
    Task<IReadOnlyList<JumpListItem>> GetJumpsAsync();
    Task<IReadOnlyList<string>> GetObjectNamesAsync();
    Task<IReadOnlyList<string>> GetJumpTypeNamesAsync();
    Task<IReadOnlyList<string>> GetRigNamesAsync();
    Task<IReadOnlyList<ObjectCatalogItem>> GetObjectsCatalogAsync();
    Task<(double? Latitude, double? Longitude)> GetObjectCoordinatesAsync(string? objectName);
    Task<IReadOnlyList<CatalogItem>> GetRigsCatalogAsync();
    Task<IReadOnlyList<CatalogItem>> GetJumpTypesCatalogAsync();
    Task<IReadOnlyList<string>> GetDeploymentTypeNamesAsync();
    Task<IReadOnlyList<string>> GetSliderTypeNamesAsync();
    Task<IReadOnlyList<string>> GetPilotChuteTypeNamesAsync();
    Task<IReadOnlyList<string>> GetBrakeSettingNamesAsync();
    Task<bool> AddJumpAsync(JumpListItem jump);
    Task<bool> UpdateJumpAsync(JumpListItem jump);
    Task<bool> DeleteJumpAsync(JumpListItem jump);
    Task<bool> ShiftJumpNumbersUpFromAsync(int fromNumber, int? excludeId = null);
    Task<bool> SupportsJumpNumberShiftAsync();
    Task<bool> AddObjectAsync(string name, string? objectType, string? description, string? coordinatesText, string? region, string? heightMeters, byte[]? photoBytes);
    Task<bool> AddRigAsync(string name, string? description);
    Task<bool> AddJumpTypeAsync(string name, string? notes);
    Task<bool> AddDeploymentTypeAsync(string name, string? notes);
    Task<bool> AddSliderTypeAsync(string name, string? notes);
    Task<bool> AddPilotChuteTypeAsync(string name, string? notes);
    Task<bool> AddBrakeSettingAsync(string name, string? notes);
    Task<bool> UpdateObjectAsync(int id, string name, string? objectType, string? description, string? coordinatesText, string? region, string? heightMeters, byte[]? photoBytes);
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
    private const string CustomDbPathPreferenceKey = "baselogapp.custom_db_path";
    private static readonly object DbBootstrapSync = new();
    private static readonly string[] RequiredLegacyTables = ["ZLOGENTRY", "ZOBJECT", "ZRIG", "ZJUMPTYPE", "Z_PRIMARYKEY"];
    private string? _customDbPath;
    private bool _runtimeStorageChecked;

    private const int EntJumpType = 3;
    private const int EntLogEntry = 5;
    private const int EntLogEntryImage = 6;
    private const int EntObject = 7;
    private const int EntObjectImage = 8;
    private const int EntPilotChuteType = 9;
    private const int EntRig = 10;
    private const int EntSliderType = 13;
    private const int EntBrakeSetting = 1;
    private const int EntDeploymentType = 2;
    private const string LegacyRigLinkTable = "Z_5RIGS";

    public void SetDbProfile(DbProfile profile) { }
    public bool SetCustomDbPath(string? dbPath)
    {
        if (string.IsNullOrWhiteSpace(dbPath))
        {
            _customDbPath = null;
            Preferences.Default.Remove(CustomDbPathPreferenceKey);
            return true;
        }

        if (!File.Exists(dbPath))
            return false;

        _customDbPath = dbPath;
        Preferences.Default.Set(CustomDbPathPreferenceKey, dbPath);
        return true;
    }
    public string GetCurrentDbPath() => ResolveDbPath();

    private string ResolveLogPath() => ResolveDbPath() + ".log";

    private T ReturnWithLog<T>(T fallback, Exception ex, string? details = null, string? category = null, [CallerMemberName] string operation = "")
    {
        LogFailure(ex, details, category, operation);
        return fallback;
    }

    private void LogFailure(Exception ex, string? details = null, string? category = null, [CallerMemberName] string operation = "")
    {
        var effectiveCategory = string.IsNullOrWhiteSpace(category)
            ? CategoryForOperation(operation)
            : category!;

        AppLog.Error(
            ResolveLogPath(),
            effectiveCategory,
            nameof(JumpsReader),
            operation,
            "Operation failed.",
            details,
            ex);
    }

    private static string CategoryForOperation(string operation)
    {
        if (operation.Contains("Import", StringComparison.OrdinalIgnoreCase)
            || operation.Contains("Export", StringComparison.OrdinalIgnoreCase))
            return LogCategories.ImportExport;

        if (operation.Contains("Shift", StringComparison.OrdinalIgnoreCase)
            || operation.Contains("Normalize", StringComparison.OrdinalIgnoreCase))
            return LogCategories.NumberShift;

        if (operation.Contains("CanDelete", StringComparison.OrdinalIgnoreCase)
            || operation.Contains("Delete", StringComparison.OrdinalIgnoreCase))
            return LogCategories.ReferenceIntegrity;

        if (operation.StartsWith("Get", StringComparison.OrdinalIgnoreCase)
            || operation.StartsWith("Supports", StringComparison.OrdinalIgnoreCase))
            return LogCategories.DataConsistency;

        return LogCategories.RuntimeError;
    }

    private string ResolveDbPath()
    {
        if (string.IsNullOrWhiteSpace(_customDbPath))
        {
            var stored = Preferences.Default.Get(CustomDbPathPreferenceKey, string.Empty);
            if (!string.IsNullOrWhiteSpace(stored))
                _customDbPath = stored;
        }

        if (!string.IsNullOrWhiteSpace(_customDbPath) && File.Exists(_customDbPath))
            return _customDbPath;

        if (!string.IsNullOrWhiteSpace(_customDbPath) && !File.Exists(_customDbPath))
        {
            AppLog.Warn(
                AppLog.DefaultLogPath,
                LogCategories.DataConsistency,
                nameof(JumpsReader),
                nameof(ResolveDbPath),
                "Custom DB path missing. Fallback to default app data DB.",
                details: $"customPath={_customDbPath}");
            _customDbPath = null;
            Preferences.Default.Remove(CustomDbPathPreferenceKey);
        }

        var appDataPath = Path.Combine(FileSystem.AppDataDirectory, DefaultDbName);
        EnsureRuntimeStorageAndBootstrap(appDataPath);

        if (File.Exists(appDataPath))
            return appDataPath;
        if (File.Exists(_legacyFallbackWindowsPath))
            return _legacyFallbackWindowsPath;
        return appDataPath;
    }

    private void EnsureRuntimeStorageAndBootstrap(string appDataPath)
    {
        lock (DbBootstrapSync)
        {
            if (_runtimeStorageChecked)
                return;
            _runtimeStorageChecked = true;

            try
            {
                var folder = Path.GetDirectoryName(appDataPath);
                var logPath = appDataPath + ".log";
                if (string.IsNullOrWhiteSpace(folder))
                {
                    AppLog.Error(
                        logPath,
                        LogCategories.RuntimeError,
                        nameof(JumpsReader),
                        nameof(EnsureRuntimeStorageAndBootstrap),
                        "Invalid app data folder path.",
                        details: $"appDataPath={appDataPath}");
                    return;
                }

                Directory.CreateDirectory(folder);
                var canWrite = CanWriteToFolder(folder);
                var appDataExists = File.Exists(appDataPath);
                var appDataValid = appDataExists && HasRequiredTables(appDataPath);
                var fallbackExists = File.Exists(_legacyFallbackWindowsPath);
                var fallbackValid = fallbackExists && HasRequiredTables(_legacyFallbackWindowsPath);

                AppLog.Info(
                    logPath,
                    LogCategories.DataConsistency,
                    nameof(JumpsReader),
                    nameof(EnsureRuntimeStorageAndBootstrap),
                    "Runtime storage check completed.",
                    details: $"appDataPath={appDataPath};appDataExists={appDataExists};appDataValid={appDataValid};folderWritable={canWrite};fallbackExists={fallbackExists};fallbackValid={fallbackValid}");

                if (appDataExists)
                    return;

                if (!canWrite)
                {
                    AppLog.Error(
                        logPath,
                        LogCategories.RuntimeError,
                        nameof(JumpsReader),
                        nameof(EnsureRuntimeStorageAndBootstrap),
                        "AppData folder is not writable. Bootstrap skipped.",
                        details: $"folder={folder}");
                    return;
                }

                if (fallbackValid)
                {
                    File.Copy(_legacyFallbackWindowsPath, appDataPath, overwrite: false);
                    AppLog.Info(
                        logPath,
                        LogCategories.DataConsistency,
                        nameof(JumpsReader),
                        nameof(EnsureRuntimeStorageAndBootstrap),
                        "Default DB bootstrapped from legacy fallback DB.",
                        details: $"source={_legacyFallbackWindowsPath};target={appDataPath}");
                    return;
                }

                using var seedStream = FileSystem.OpenAppPackageFileAsync(DefaultDbName).GetAwaiter().GetResult();
                using var output = File.Create(appDataPath);
                seedStream.CopyTo(output);

                AppLog.Info(
                    logPath,
                    LogCategories.DataConsistency,
                    nameof(JumpsReader),
                    nameof(EnsureRuntimeStorageAndBootstrap),
                    "Default DB bootstrapped from packaged seed DB.",
                    details: $"target={appDataPath}");
            }
            catch (Exception ex)
            {
                var logPath = appDataPath + ".log";
                AppLog.Error(
                    logPath,
                    LogCategories.RuntimeError,
                    nameof(JumpsReader),
                    nameof(EnsureRuntimeStorageAndBootstrap),
                    "Unable to initialize default DB in AppData.",
                    details: $"target={appDataPath}",
                    ex: ex);
            }
        }
    }

    private static bool CanWriteToFolder(string folderPath)
    {
        try
        {
            var probePath = Path.Combine(folderPath, ".write_probe_" + Guid.NewGuid().ToString("N") + ".tmp");
            File.WriteAllText(probePath, "ok");
            File.Delete(probePath);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool HasRequiredTables(string dbPath)
    {
        try
        {
            if (!File.Exists(dbPath))
                return false;

            using var db = new SQLiteConnection(new SQLiteConnectionString(dbPath, storeDateTimeAsTicks: false));
            foreach (var table in RequiredLegacyTables)
            {
                var exists = db.ExecuteScalar<int>(
                    "SELECT COUNT(1) FROM sqlite_master WHERE type='table' AND name=?;",
                    table);
                if (exists <= 0)
                    return false;
            }

            return true;
        }
        catch
        {
            return false;
        }
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
            return ReturnWithLog(Array.Empty<JumpListItem>(), ex, details: $"dbPath={dbPath}");
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
        catch (Exception ex)
        {
            return ReturnWithLog(Array.Empty<string>(), ex);
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
        catch (Exception ex)
        {
            return ReturnWithLog(Array.Empty<string>(), ex);
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
        catch (Exception ex)
        {
            return ReturnWithLog(Array.Empty<string>(), ex);
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
                var objectCols = await GetTableColumnsAsync(db, "Object");
                var hasRegion = objectCols.Contains("Region");
                var hasPosition = objectCols.Contains("Position");
                var hasLat = objectCols.Contains("Latitude");
                var hasLon = objectCols.Contains("Longitude");
                var regionExpr = hasRegion ? "Region" : "NULL";
                var coordsExpr = hasPosition
                    ? "Position"
                    : (hasLat && hasLon
                        ? "CAST(Latitude AS TEXT) || ', ' || CAST(Longitude AS TEXT)"
                        : "NULL");
                var latExpr = hasLat ? "CAST(Latitude AS TEXT)" : "NULL";
                var lonExpr = hasLon ? "CAST(Longitude AS TEXT)" : "NULL";

                var sql = $@"
SELECT
    Id,
    Name,
    ObjectType,
    Description AS Notes,
    HeightMeters,
    HeightUnit,
    {regionExpr} AS Region,
    {coordsExpr} AS Position,
    {latExpr} AS Latitude,
    {lonExpr} AS Longitude,
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
                var regionExpr = ColumnExpr("o", objectCols, "ZREGION", "ZLOCATION", "ZPOSITION");
                var latExpr = ColumnExpr("o", objectCols, "ZLATITUDE");
                var lonExpr = ColumnExpr("o", objectCols, "ZLONGITUDE");
                var coordsExpr = $"CASE WHEN {latExpr} IS NOT NULL AND {lonExpr} IS NOT NULL THEN CAST({latExpr} AS TEXT) || ', ' || CAST({lonExpr} AS TEXT) ELSE NULL END";

                var sql = $@"
SELECT
    o.Z_PK AS Id,
    o.ZNAME AS Name,
    {typeExpr} AS ObjectType,
    {descExpr} AS Notes,
    CAST({heightExpr} AS TEXT) AS HeightMeters,
    {unitExpr} AS HeightUnit,
    {regionExpr} AS Region,
    {coordsExpr} AS Position,
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
        catch (Exception ex)
        {
            return ReturnWithLog(Array.Empty<ObjectCatalogItem>(), ex);
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
                var latExpr = ColumnExpr("o", cols, "ZLATITUDE", "ZLAT");
                var lonExpr = ColumnExpr("o", cols, "ZLONGITUDE", "ZLON", "ZLNG");
                var row = await db.FindWithQueryAsync<CoordinateRow>(
                    $"SELECT {latExpr} AS Latitude, {lonExpr} AS Longitude FROM ZOBJECT o WHERE o.Z_ENT = 7 AND lower(trim(o.ZNAME)) = lower(trim(?)) LIMIT 1;",
                    objectName.Trim());
                return (row?.Latitude, row?.Longitude);
            }

            return (null, null);
        }
        catch (Exception ex)
        {
            return ReturnWithLog<(double? Latitude, double? Longitude)>((null, null), ex);
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
        catch (Exception ex)
        {
            return ReturnWithLog(Array.Empty<CatalogItem>(), ex);
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
        catch (Exception ex)
        {
            return ReturnWithLog(Array.Empty<CatalogItem>(), ex);
        }
    }

    public Task<IReadOnlyList<string>> GetDeploymentTypeNamesAsync()
        => GetLegacySimpleNamesAsync("ZDEPLOYMENTTYPE", EntDeploymentType);

    public Task<IReadOnlyList<string>> GetSliderTypeNamesAsync()
        => GetLegacySimpleNamesAsync("ZSLIDERTYPE", EntSliderType);

    public Task<IReadOnlyList<string>> GetPilotChuteTypeNamesAsync()
        => GetLegacySimpleNamesAsync("ZPILOTCHUTETYPE", EntPilotChuteType);

    public Task<IReadOnlyList<string>> GetBrakeSettingNamesAsync()
        => GetLegacySimpleNamesAsync("ZBRAKESETTING", EntBrakeSetting);

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

                var jumpColumns = await GetTableColumnsAsync(db, "Jump");
                await UpdateModernJumpOptionalFieldsAsync(db, jump.NumeroSalto, jumpColumns, jump);
                return true;
            }

            if (await HasTableAsync(db, "ZLOGENTRY"))
            {
                var date = ParseDisplayDate(jump.Data) ?? DateTime.Now;
                var appleSeconds = ToAppleReferenceSeconds(date);
                var objectId = await FindLegacyIdByNameAsync(db, "ZOBJECT", jump.Oggetto);
                var typeId = await FindLegacyIdByNameAsync(db, "ZJUMPTYPE", jump.TipoSalto);
                var rigLink = await GetLegacyRigLinkInfoAsync(db);
                var rigIds = await ResolveLegacyRigIdsAsync(db, jump.RigNames);
                var hasLogEntryImage = await HasTableAsync(db, "ZLOGENTRYIMAGE");

                var logEntryColumns = await GetTableColumnsAsync(db, "ZLOGENTRY");
                var hasLastModifiedUtc = logEntryColumns.Contains("ZLASTMODIFIEDUTC");
                var lastModifiedUtc = CurrentLegacyTimestamp();

                var createdPk = 0;
                await db.RunInTransactionAsync(conn =>
                {
                    createdPk = AllocateCoreDataPk(conn, EntLogEntry, "ZLOGENTRY");

                    if (hasLastModifiedUtc)
                    {
                        conn.Execute(
                            "INSERT INTO ZLOGENTRY (Z_PK, Z_ENT, Z_OPT, ZJUMPNUMBER, ZDATE, ZOBJECT, ZJUMPTYPE, ZNOTES, ZLASTMODIFIEDUTC) VALUES (?, ?, 1, ?, ?, ?, ?, ?, ?);",
                            createdPk,
                            EntLogEntry,
                            jump.NumeroSalto,
                            appleSeconds,
                            objectId,
                            typeId,
                            jump.Note,
                            lastModifiedUtc);
                    }
                    else
                    {
                        conn.Execute(
                            "INSERT INTO ZLOGENTRY (Z_PK, Z_ENT, Z_OPT, ZJUMPNUMBER, ZDATE, ZOBJECT, ZJUMPTYPE, ZNOTES) VALUES (?, ?, 1, ?, ?, ?, ?, ?);",
                            createdPk,
                            EntLogEntry,
                            jump.NumeroSalto,
                            appleSeconds,
                            objectId,
                            typeId,
                            jump.Note);
                    }

                    UpdateLegacyUniqueIdIfPresent(conn, "ZLOGENTRY", createdPk, EntLogEntry, logEntryColumns);
                    UpdateLegacyJumpOptionalFields(conn, createdPk, logEntryColumns, jump);

                    if (rigLink is not null)
                        ReplaceLegacyRigLinks(conn, rigLink, createdPk, rigIds);

                    if (hasLogEntryImage && jump.NewPhotoBytes is { Length: > 0 })
                    {
                        var imagePk = AllocateCoreDataPk(conn, EntLogEntryImage, "ZLOGENTRYIMAGE");
                        conn.Execute(
                            "INSERT INTO ZLOGENTRYIMAGE (Z_PK, Z_ENT, Z_OPT, ZLOGENTRY, ZIMAGE) VALUES (?, ?, 1, ?, ?);",
                            imagePk,
                            EntLogEntryImage,
                            createdPk,
                            jump.NewPhotoBytes);
                    }
                });

                return createdPk > 0;
            }

            return false;
        }
        catch (Exception ex)
        {
            return ReturnWithLog(false, ex);
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

                if (rows > 0)
                {
                    var jumpColumns = await GetTableColumnsAsync(db, "Jump");
                    await UpdateModernJumpOptionalFieldsAsync(db, jump.NumeroSalto, jumpColumns, jump);
                }
                return rows > 0;
            }

            if (await HasTableAsync(db, "ZLOGENTRY"))
            {
                var date = ParseDisplayDate(jump.Data) ?? DateTime.Now;
                var appleSeconds = ToAppleReferenceSeconds(date);
                var objectId = await FindLegacyIdByNameAsync(db, "ZOBJECT", jump.Oggetto);
                var typeId = await FindLegacyIdByNameAsync(db, "ZJUMPTYPE", jump.TipoSalto);
                var rigLink = await GetLegacyRigLinkInfoAsync(db);
                var rigIds = await ResolveLegacyRigIdsAsync(db, jump.RigNames);

                var logEntryColumns = await GetTableColumnsAsync(db, "ZLOGENTRY");
                var hasLastModifiedUtc = logEntryColumns.Contains("ZLASTMODIFIEDUTC");
                var lastModifiedUtc = CurrentLegacyTimestamp();

                var sql = hasLastModifiedUtc
                    ? "UPDATE ZLOGENTRY SET Z_OPT = COALESCE(Z_OPT, 0) + 1, ZJUMPNUMBER = ?, ZDATE = ?, ZOBJECT = ?, ZJUMPTYPE = ?, ZNOTES = ?, ZLASTMODIFIEDUTC = ? WHERE Z_PK = ? AND Z_ENT = ?;"
                    : "UPDATE ZLOGENTRY SET Z_OPT = COALESCE(Z_OPT, 0) + 1, ZJUMPNUMBER = ?, ZDATE = ?, ZOBJECT = ?, ZJUMPTYPE = ?, ZNOTES = ? WHERE Z_PK = ? AND Z_ENT = ?;";

                var rows = hasLastModifiedUtc
                    ? await db.ExecuteAsync(sql, jump.NumeroSalto, appleSeconds, objectId, typeId, jump.Note, lastModifiedUtc, jump.Id, EntLogEntry)
                    : await db.ExecuteAsync(sql, jump.NumeroSalto, appleSeconds, objectId, typeId, jump.Note, jump.Id, EntLogEntry);

                if (rows > 0)
                {
                    await db.RunInTransactionAsync(conn =>
                    {
                        UpdateLegacyJumpOptionalFields(conn, jump.Id, logEntryColumns, jump);
                        if (rigLink is not null)
                        ReplaceLegacyRigLinks(conn, rigLink, jump.Id, rigIds);
                    });

                    if (logEntryColumns.Contains("ZUNIQUEID"))
                    {
                        await db.ExecuteAsync(
                            "UPDATE ZLOGENTRY SET ZUNIQUEID = COALESCE(NULLIF(ZUNIQUEID, ''), ?) WHERE Z_PK = ? AND Z_ENT = ?;",
                            Guid.NewGuid().ToString("D"),
                            jump.Id,
                            EntLogEntry);
                    }
                }

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
                else if (rows > 0 && jump.RemoveJumpPhoto && await HasTableAsync(db, "ZLOGENTRYIMAGE"))
                {
                    await db.ExecuteAsync("DELETE FROM ZLOGENTRYIMAGE WHERE ZLOGENTRY = ? AND Z_ENT = ?;", jump.Id, EntLogEntryImage);
                }

                return rows > 0;
            }

            return false;
        }
        catch (Exception ex)
        {
            return ReturnWithLog(false, ex);
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

                var rigLink = await GetLegacyRigLinkInfoAsync(db);
                if (rigLink is not null)
                    await db.ExecuteAsync($"DELETE FROM {LegacyRigLinkTable} WHERE {rigLink.LogEntryColumn} = ?;", jump.Id);

                return await db.ExecuteAsync("DELETE FROM ZLOGENTRY WHERE Z_PK = ? AND Z_ENT = ?;", jump.Id, EntLogEntry) > 0;
            }

            return false;
        }
        catch (Exception ex)
        {
            return ReturnWithLog(false, ex);
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
                var logEntryColumns = await GetTableColumnsAsync(db, "ZLOGENTRY");
                var hasLastModifiedUtc = logEntryColumns.Contains("ZLASTMODIFIEDUTC");
                var lastModifiedUtc = CurrentLegacyTimestamp();

                if (excludeId.HasValue)
                {
                    if (hasLastModifiedUtc)
                    {
                        await db.ExecuteAsync(
                            "UPDATE ZLOGENTRY SET ZJUMPNUMBER = ZJUMPNUMBER + 1, ZLASTMODIFIEDUTC = ? WHERE Z_ENT = ? AND ZJUMPNUMBER >= ? AND Z_PK <> ?;",
                            lastModifiedUtc,
                            EntLogEntry,
                            fromNumber,
                            excludeId.Value);
                    }
                    else
                    {
                        await db.ExecuteAsync(
                            "UPDATE ZLOGENTRY SET ZJUMPNUMBER = ZJUMPNUMBER + 1 WHERE Z_ENT = ? AND ZJUMPNUMBER >= ? AND Z_PK <> ?;",
                            EntLogEntry,
                            fromNumber,
                            excludeId.Value);
                    }
                }
                else
                {
                    if (hasLastModifiedUtc)
                    {
                        await db.ExecuteAsync(
                            "UPDATE ZLOGENTRY SET ZJUMPNUMBER = ZJUMPNUMBER + 1, ZLASTMODIFIEDUTC = ? WHERE Z_ENT = ? AND ZJUMPNUMBER >= ?;",
                            lastModifiedUtc,
                            EntLogEntry,
                            fromNumber);
                    }
                    else
                    {
                        await db.ExecuteAsync(
                            "UPDATE ZLOGENTRY SET ZJUMPNUMBER = ZJUMPNUMBER + 1 WHERE Z_ENT = ? AND ZJUMPNUMBER >= ?;",
                            EntLogEntry,
                            fromNumber);
                    }
                }

                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            return ReturnWithLog(false, ex);
        }
    }

    public async Task<bool> SupportsJumpNumberShiftAsync()
    {
        var db = await TryOpenDbAsync();
        if (db is null) return false;
        return await HasTableAsync(db, "Jump") || await HasTableAsync(db, "ZLOGENTRY");
    }

    public async Task<bool> AddObjectAsync(string name, string? objectType, string? description, string? coordinatesText, string? region, string? heightMeters, byte[]? photoBytes)
    {
        var db = await TryOpenDbAsync();
        if (db is null || string.IsNullOrWhiteSpace(name)) return false;

        try
        {
            if (await HasTableAsync(db, "Object"))
            {
                await db.ExecuteAsync(
                    "INSERT INTO Object (Name, ObjectType, Description, Position, HeightMeters, PhotoBlob) VALUES (?, ?, ?, ?, ?, ?);",
                    name.Trim(), objectType, description, coordinatesText, heightMeters, photoBytes);
                return true;
            }

            if (await HasTableAsync(db, "ZOBJECT"))
            {
                var hasObjectImages = await HasTableAsync(db, "ZOBJECTIMAGE");
                var objectColumns = await GetTableColumnsAsync(db, "ZOBJECT");
                var hasLastModifiedUtc = objectColumns.Contains("ZLASTMODIFIEDUTC");
                var lastModifiedUtc = CurrentLegacyTimestamp();
                var objectTypeColumn = FindFirstExistingColumn(objectColumns, "ZOBJECTTYPE");
                var notesColumn = FindFirstExistingColumn(objectColumns, "ZNOTES", "ZDESCRIPTION");
                var positionColumn = FindFirstExistingColumn(objectColumns, "ZPOSITION", "ZLOCATION", "ZREGION");
                var heightColumn = FindFirstExistingColumn(objectColumns, "ZHEIGHT", "ZHEIGHTMETERS", "ZALTITUDE");
                var latColumn = FindFirstExistingColumn(objectColumns, "ZLATITUDE", "ZLAT");
                var lonColumn = FindFirstExistingColumn(objectColumns, "ZLONGITUDE", "ZLON", "ZLNG");
                var parsedLatLon = ParseLatLonFromPosition(coordinatesText);

                await db.RunInTransactionAsync(conn =>
                {
                    var objectPk = AllocateCoreDataPk(conn, EntObject, "ZOBJECT");
                    var columns = new List<string> { "Z_PK", "Z_ENT", "Z_OPT", "ZNAME" };
                    var values = new List<object?> { objectPk, EntObject, 1, name.Trim() };

                    if (!string.IsNullOrWhiteSpace(objectTypeColumn))
                    {
                        columns.Add(objectTypeColumn);
                        values.Add(objectType);
                    }

                    if (!string.IsNullOrWhiteSpace(notesColumn))
                    {
                        columns.Add(notesColumn);
                        values.Add(description);
                    }

                    if (!string.IsNullOrWhiteSpace(positionColumn))
                    {
                        columns.Add(positionColumn);
                        values.Add(region);
                    }

                    if (!string.IsNullOrWhiteSpace(heightColumn))
                    {
                        columns.Add(heightColumn);
                        values.Add(heightMeters);
                    }

                    if (!string.IsNullOrWhiteSpace(latColumn))
                    {
                        columns.Add(latColumn);
                        values.Add(parsedLatLon.Latitude);
                    }

                    if (!string.IsNullOrWhiteSpace(lonColumn))
                    {
                        columns.Add(lonColumn);
                        values.Add(parsedLatLon.Longitude);
                    }

                    if (hasLastModifiedUtc)
                    {
                        columns.Add("ZLASTMODIFIEDUTC");
                        values.Add(lastModifiedUtc);
                    }

                    var placeholders = string.Join(", ", Enumerable.Repeat("?", columns.Count));
                    var sql = $"INSERT INTO ZOBJECT ({string.Join(", ", columns)}) VALUES ({placeholders});";
                    conn.Execute(sql, values.ToArray());

                    UpdateLegacyUniqueIdIfPresent(conn, "ZOBJECT", objectPk, EntObject, objectColumns);

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
        catch (Exception ex)
        {
            return ReturnWithLog(false, ex);
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
                var rigColumns = await GetTableColumnsAsync(db, "ZRIG");
                var hasLastModifiedUtc = rigColumns.Contains("ZLASTMODIFIEDUTC");
                var lastModifiedUtc = CurrentLegacyTimestamp();

                await db.RunInTransactionAsync(conn =>
                {
                    var rigPk = AllocateCoreDataPk(conn, EntRig, "ZRIG");
                    if (hasLastModifiedUtc)
                    {
                        conn.Execute(
                            "INSERT INTO ZRIG (Z_PK, Z_ENT, Z_OPT, ZNAME, ZNOTES, ZLASTMODIFIEDUTC) VALUES (?, ?, 1, ?, ?, ?);",
                            rigPk,
                            EntRig,
                            name.Trim(),
                            description,
                            lastModifiedUtc);
                    }
                    else
                    {
                        conn.Execute("INSERT INTO ZRIG (Z_PK, Z_ENT, Z_OPT, ZNAME, ZNOTES) VALUES (?, ?, 1, ?, ?);", rigPk, EntRig, name.Trim(), description);
                    }

                    UpdateLegacyUniqueIdIfPresent(conn, "ZRIG", rigPk, EntRig, rigColumns);
                });
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            return ReturnWithLog(false, ex);
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
                var jumpTypeColumns = await GetTableColumnsAsync(db, "ZJUMPTYPE");
                var hasLastModifiedUtc = jumpTypeColumns.Contains("ZLASTMODIFIEDUTC");
                var lastModifiedUtc = CurrentLegacyTimestamp();

                await db.RunInTransactionAsync(conn =>
                {
                    var jumpTypePk = AllocateCoreDataPk(conn, EntJumpType, "ZJUMPTYPE");
                    if (hasLastModifiedUtc)
                    {
                        conn.Execute(
                            "INSERT INTO ZJUMPTYPE (Z_PK, Z_ENT, Z_OPT, ZNAME, ZNOTES, ZLASTMODIFIEDUTC) VALUES (?, ?, 1, ?, ?, ?);",
                            jumpTypePk,
                            EntJumpType,
                            name.Trim(),
                            notes,
                            lastModifiedUtc);
                    }
                    else
                    {
                        conn.Execute("INSERT INTO ZJUMPTYPE (Z_PK, Z_ENT, Z_OPT, ZNAME, ZNOTES) VALUES (?, ?, 1, ?, ?);", jumpTypePk, EntJumpType, name.Trim(), notes);
                    }

                    UpdateLegacyUniqueIdIfPresent(conn, "ZJUMPTYPE", jumpTypePk, EntJumpType, jumpTypeColumns);
                });
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            return ReturnWithLog(false, ex);
        }
    }

    public Task<bool> AddDeploymentTypeAsync(string name, string? notes)
        => AddLegacySimpleNameAsync("ZDEPLOYMENTTYPE", EntDeploymentType, name, notes);

    public Task<bool> AddSliderTypeAsync(string name, string? notes)
        => AddLegacySimpleNameAsync("ZSLIDERTYPE", EntSliderType, name, notes);

    public Task<bool> AddPilotChuteTypeAsync(string name, string? notes)
        => AddLegacySimpleNameAsync("ZPILOTCHUTETYPE", EntPilotChuteType, name, notes);

    public Task<bool> AddBrakeSettingAsync(string name, string? notes)
        => AddLegacySimpleNameAsync("ZBRAKESETTING", EntBrakeSetting, name, notes);

    public async Task<bool> UpdateObjectAsync(int id, string name, string? objectType, string? description, string? coordinatesText, string? region, string? heightMeters, byte[]? photoBytes)
    {
        var db = await TryOpenDbAsync();
        if (db is null || string.IsNullOrWhiteSpace(name)) return false;

        try
        {
            if (await HasTableAsync(db, "Object"))
            {
                var rows = await db.ExecuteAsync(
                    "UPDATE Object SET Name = ?, ObjectType = ?, Description = ?, Position = ?, HeightMeters = ?, PhotoBlob = COALESCE(?, PhotoBlob) WHERE Id = ?;",
                    name.Trim(), objectType, description, coordinatesText, heightMeters, photoBytes, id);
                return rows > 0;
            }

            if (await HasTableAsync(db, "ZOBJECT"))
            {
                var objectColumns = await GetTableColumnsAsync(db, "ZOBJECT");
                var hasLastModifiedUtc = objectColumns.Contains("ZLASTMODIFIEDUTC");
                var lastModifiedUtc = CurrentLegacyTimestamp();
                var objectTypeColumn = FindFirstExistingColumn(objectColumns, "ZOBJECTTYPE");
                var notesColumn = FindFirstExistingColumn(objectColumns, "ZNOTES", "ZDESCRIPTION");
                var positionColumn = FindFirstExistingColumn(objectColumns, "ZPOSITION", "ZLOCATION", "ZREGION");
                var heightColumn = FindFirstExistingColumn(objectColumns, "ZHEIGHT", "ZHEIGHTMETERS", "ZALTITUDE");
                var latColumn = FindFirstExistingColumn(objectColumns, "ZLATITUDE", "ZLAT");
                var lonColumn = FindFirstExistingColumn(objectColumns, "ZLONGITUDE", "ZLON", "ZLNG");
                var parsedLatLon = ParseLatLonFromPosition(coordinatesText);

                var setClauses = new List<string> { "Z_OPT = COALESCE(Z_OPT, 0) + 1", "ZNAME = ?" };
                var args = new List<object?> { name.Trim() };

                if (!string.IsNullOrWhiteSpace(objectTypeColumn))
                {
                    setClauses.Add($"{objectTypeColumn} = ?");
                    args.Add(objectType);
                }

                if (!string.IsNullOrWhiteSpace(notesColumn))
                {
                    setClauses.Add($"{notesColumn} = ?");
                    args.Add(description);
                }

                if (!string.IsNullOrWhiteSpace(positionColumn))
                {
                    setClauses.Add($"{positionColumn} = ?");
                    args.Add(region);
                }

                if (!string.IsNullOrWhiteSpace(heightColumn))
                {
                    setClauses.Add($"{heightColumn} = ?");
                    args.Add(heightMeters);
                }

                if (!string.IsNullOrWhiteSpace(latColumn))
                {
                    setClauses.Add($"{latColumn} = ?");
                    args.Add(parsedLatLon.Latitude);
                }

                if (!string.IsNullOrWhiteSpace(lonColumn))
                {
                    setClauses.Add($"{lonColumn} = ?");
                    args.Add(parsedLatLon.Longitude);
                }

                if (hasLastModifiedUtc)
                {
                    setClauses.Add("ZLASTMODIFIEDUTC = ?");
                    args.Add(lastModifiedUtc);
                }

                args.Add(id);
                args.Add(EntObject);

                var sql = $"UPDATE ZOBJECT SET {string.Join(", ", setClauses)} WHERE Z_PK = ? AND Z_ENT = ?;";
                var rows = await db.ExecuteAsync(sql, args.ToArray());

                if (rows > 0 && objectColumns.Contains("ZUNIQUEID"))
                {
                    await db.ExecuteAsync(
                        "UPDATE ZOBJECT SET ZUNIQUEID = COALESCE(NULLIF(ZUNIQUEID, ''), ?) WHERE Z_PK = ? AND Z_ENT = ?;",
                        Guid.NewGuid().ToString("D"),
                        id,
                        EntObject);
                }

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
        catch (Exception ex)
        {
            return ReturnWithLog(false, ex);
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
            {
                var rigColumns = await GetTableColumnsAsync(db, "ZRIG");
                var hasLastModifiedUtc = rigColumns.Contains("ZLASTMODIFIEDUTC");
                var lastModifiedUtc = CurrentLegacyTimestamp();

                var sql = hasLastModifiedUtc
                    ? "UPDATE ZRIG SET Z_OPT = COALESCE(Z_OPT, 0) + 1, ZNAME = ?, ZNOTES = ?, ZLASTMODIFIEDUTC = ? WHERE Z_PK = ? AND Z_ENT = ?;"
                    : "UPDATE ZRIG SET Z_OPT = COALESCE(Z_OPT, 0) + 1, ZNAME = ?, ZNOTES = ? WHERE Z_PK = ? AND Z_ENT = ?;";

                var rows = hasLastModifiedUtc
                    ? await db.ExecuteAsync(sql, name.Trim(), description, lastModifiedUtc, id, EntRig)
                    : await db.ExecuteAsync(sql, name.Trim(), description, id, EntRig);

                if (rows > 0 && rigColumns.Contains("ZUNIQUEID"))
                {
                    await db.ExecuteAsync(
                        "UPDATE ZRIG SET ZUNIQUEID = COALESCE(NULLIF(ZUNIQUEID, ''), ?) WHERE Z_PK = ? AND Z_ENT = ?;",
                        Guid.NewGuid().ToString("D"),
                        id,
                        EntRig);
                }

                return rows > 0;
            }

            return false;
        }
        catch (Exception ex)
        {
            return ReturnWithLog(false, ex);
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
            {
                var jumpTypeColumns = await GetTableColumnsAsync(db, "ZJUMPTYPE");
                var hasLastModifiedUtc = jumpTypeColumns.Contains("ZLASTMODIFIEDUTC");
                var lastModifiedUtc = CurrentLegacyTimestamp();

                var sql = hasLastModifiedUtc
                    ? "UPDATE ZJUMPTYPE SET Z_OPT = COALESCE(Z_OPT, 0) + 1, ZNAME = ?, ZNOTES = ?, ZLASTMODIFIEDUTC = ? WHERE Z_PK = ? AND Z_ENT = ?;"
                    : "UPDATE ZJUMPTYPE SET Z_OPT = COALESCE(Z_OPT, 0) + 1, ZNAME = ?, ZNOTES = ? WHERE Z_PK = ? AND Z_ENT = ?;";

                var rows = hasLastModifiedUtc
                    ? await db.ExecuteAsync(sql, name.Trim(), notes, lastModifiedUtc, id, EntJumpType)
                    : await db.ExecuteAsync(sql, name.Trim(), notes, id, EntJumpType);

                if (rows > 0 && jumpTypeColumns.Contains("ZUNIQUEID"))
                {
                    await db.ExecuteAsync(
                        "UPDATE ZJUMPTYPE SET ZUNIQUEID = COALESCE(NULLIF(ZUNIQUEID, ''), ?) WHERE Z_PK = ? AND Z_ENT = ?;",
                        Guid.NewGuid().ToString("D"),
                        id,
                        EntJumpType);
                }

                return rows > 0;
            }

            return false;
        }
        catch (Exception ex)
        {
            return ReturnWithLog(false, ex);
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
                var rows = await db.QueryAsync<ModernNormalizeRow>("SELECT Id, CAST(JumpDateUtc AS TEXT) AS DateText FROM Jump ORDER BY Id, JumpDateUtc;");
                return await NormalizeByJumpNumberModernAsync(db, rows);
            }

            if (await HasTableAsync(db, "ZLOGENTRY"))
            {
                var rows = await db.QueryAsync<LegacyNormalizeRow>(
                    "SELECT Z_PK AS Pk, ZJUMPNUMBER AS Number, CAST(ZDATE AS TEXT) AS DateText FROM ZLOGENTRY WHERE Z_ENT = 5 ORDER BY ZJUMPNUMBER, Z_PK;");
                return await NormalizeByJumpNumberLegacyAsync(db, rows);
            }

            return 0;
        }
        catch (Exception ex)
        {
            return ReturnWithLog(0, ex);
        }
    }

    private async Task<int> NormalizeByJumpNumberModernAsync(SQLiteAsyncConnection db, IReadOnlyList<ModernNormalizeRow> rows)
    {
        if (rows.Count == 0)
            return 0;

        var mismatches = new List<string>();
        var applied = new List<string>();
        var expected = 1;
        foreach (var row in rows)
        {
            if (row.Id != expected)
                mismatches.Add($"expected={expected};actual={row.Id};date={FromUnixSecondsToDisplay(row.DateText)}");
            expected++;
        }

        if (mismatches.Count == 0)
            return 0;

        var details = BuildNormalizationDetails("modern", mismatches);
        AppLog.Warn(
            ResolveLogPath(),
            LogCategories.NumberShift,
            nameof(JumpsReader),
            nameof(NormalizeJumpNumbersAsync),
            "Jump-number discrepancies found (ordered by jump number).",
            details: details);

        var changes = 0;
        expected = 1;
        foreach (var row in rows)
        {
            if (row.Id != expected)
            {
                applied.Add($"id:{row.Id}->{expected};date={FromUnixSecondsToDisplay(row.DateText)}");
                await db.ExecuteAsync("UPDATE Jump SET Id = ? WHERE Id = ?;", expected, row.Id);
                changes++;
            }
            expected++;
        }

        AppLog.Warn(
            ResolveLogPath(),
            LogCategories.NumberShift,
            nameof(JumpsReader),
            nameof(NormalizeJumpNumbersAsync),
            "Jump-number normalization applied (ordered by jump number).",
            details: $"changes={changes};{details}{Environment.NewLine}{BuildAppliedNormalizationDetails(applied)}");

        return changes;
    }

    private async Task<int> NormalizeByJumpNumberLegacyAsync(SQLiteAsyncConnection db, IReadOnlyList<LegacyNormalizeRow> rows)
    {
        if (rows.Count == 0)
            return 0;

        var mismatches = new List<string>();
        var applied = new List<string>();
        var expected = 1;
        foreach (var row in rows)
        {
            if (row.Number != expected)
                mismatches.Add($"pk={row.Pk};expected={expected};actual={row.Number};date={FromAppleSecondsToDisplay(row.DateText)}");
            expected++;
        }

        if (mismatches.Count == 0)
            return 0;

        var details = BuildNormalizationDetails("legacy", mismatches);
        AppLog.Warn(
            ResolveLogPath(),
            LogCategories.NumberShift,
            nameof(JumpsReader),
            nameof(NormalizeJumpNumbersAsync),
            "Jump-number discrepancies found (ordered by jump number).",
            details: details);

        var logEntryColumns = await GetTableColumnsAsync(db, "ZLOGENTRY");
        var hasLastModifiedUtc = logEntryColumns.Contains("ZLASTMODIFIEDUTC");
        var lastModifiedUtc = CurrentLegacyTimestamp();

        var changes = 0;
        expected = 1;
        foreach (var row in rows)
        {
            if (row.Number != expected)
            {
                applied.Add($"pk:{row.Pk};number:{row.Number}->{expected};date={FromAppleSecondsToDisplay(row.DateText)}");
                if (hasLastModifiedUtc)
                {
                    await db.ExecuteAsync(
                        "UPDATE ZLOGENTRY SET ZJUMPNUMBER = ?, ZLASTMODIFIEDUTC = ? WHERE Z_PK = ? AND Z_ENT = ?;",
                        expected,
                        lastModifiedUtc,
                        row.Pk,
                        EntLogEntry);
                }
                else
                {
                    await db.ExecuteAsync("UPDATE ZLOGENTRY SET ZJUMPNUMBER = ? WHERE Z_PK = ? AND Z_ENT = ?;", expected, row.Pk, EntLogEntry);
                }
                changes++;
            }
            expected++;
        }

        AppLog.Warn(
            ResolveLogPath(),
            LogCategories.NumberShift,
            nameof(JumpsReader),
            nameof(NormalizeJumpNumbersAsync),
            "Jump-number normalization applied (ordered by jump number).",
            details: $"changes={changes};{details}{Environment.NewLine}{BuildAppliedNormalizationDetails(applied)}");

        return changes;
    }

    private static string BuildNormalizationDetails(string mode, IReadOnlyList<string> mismatches)
    {
        const int maxLines = 40;
        var shown = mismatches.Take(maxLines);
        var details = $"mode={mode};count={mismatches.Count};rule=jump-number-only;note=date-not-used-for-order" +
                      Environment.NewLine +
                      string.Join(Environment.NewLine, shown);

        if (mismatches.Count > maxLines)
            details += Environment.NewLine + $"... +{mismatches.Count - maxLines} more";

        return details;
    }

    private static string BuildAppliedNormalizationDetails(IReadOnlyList<string> appliedRows)
    {
        if (appliedRows.Count == 0)
            return "applied:none";

        const int maxLines = 40;
        var shown = appliedRows.Take(maxLines);
        var details = "applied-changes:" + Environment.NewLine + string.Join(Environment.NewLine, shown);
        if (appliedRows.Count > maxLines)
            details += Environment.NewLine + $"... +{appliedRows.Count - maxLines} more";

        return details;
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
            LogFailure(ex, details: $"id={id}");
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
                var rigLink = await GetLegacyRigLinkInfoAsync(db);
                if (rigLink is not null)
                {
                    var refs = await db.ExecuteScalarAsync<int>($"SELECT COUNT(1) FROM {LegacyRigLinkTable} WHERE {rigLink.RigColumn} = ?;", id);
                    return refs == 0 ? (true, null) : (false, "Rig is used by one or more jumps");
                }

                var refsFallback = await db.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM ZLOGENTRY WHERE Z_ENT = ? AND ZRIG = ?;", EntLogEntry, id);
                return refsFallback == 0 ? (true, null) : (false, "Rig is used by one or more jumps");
            }

            return (true, null);
        }
        catch (Exception ex)
        {
            LogFailure(ex, details: $"id={id}");
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
            LogFailure(ex, details: $"id={id}");
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
        catch (Exception ex)
        {
            return ReturnWithLog(false, ex);
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
                await AddObjectAsync(item.Name, item.ObjectType, item.Description, item.Position, item.Region, item.HeightMeters, item.PhotoBlob);

            foreach (var item in payload.Rigs)
                await AddRigAsync(item.Name, item.Notes);

            foreach (var item in payload.JumpTypes)
                await AddJumpTypeAsync(item.Name, item.Notes);

            foreach (var jump in payload.Jumps.OrderBy(x => x.NumeroSalto))
                await AddJumpAsync(jump);

            return true;
        }
        catch (Exception ex)
        {
            return ReturnWithLog(false, ex);
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
        catch (Exception ex)
        {
            return ReturnWithLog(Task.FromResult(false), ex);
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
        catch (Exception ex)
        {
            return ReturnWithLog(Task.FromResult(false), ex);
        }
    }

    private async Task<IReadOnlyList<JumpListItem>> GetModernJumpsAsync(SQLiteAsyncConnection db)
    {
        var jumpColumns = await GetTableColumnsAsync(db, "Jump");
        var delayExpr = FindFirstExistingColumn(jumpColumns, "DelaySeconds", "Delay", "OpeningDelay", "DelayInSeconds");
        var headingExpr = FindFirstExistingColumn(jumpColumns, "HeadingDegrees", "OpeningHeading", "Heading", "OpeningDegrees", "ApertureDegrees", "OpeningAngle");

        var delaySelect = string.IsNullOrWhiteSpace(delayExpr) ? "NULL" : $"CAST({delayExpr} AS TEXT)";
        var headingSelect = string.IsNullOrWhiteSpace(headingExpr) ? "NULL" : $"CAST({headingExpr} AS TEXT)";

        var sql = $@"
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
    {delaySelect} AS DelaySecondsText,
    {headingSelect} AS HeadingDegreesText
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
        var headingExpr = ColumnExpr("l", logColumns, "ZOPENINGDIRECTION", "ZHEADING", "ZOPENINGHEADING", "ZTRACK");

        var rigLink = await GetLegacyRigLinkInfoAsync(db);
        var rigNamesExpr = "NULL";
        if (rigLink is not null && await HasTableAsync(db, "ZRIG"))
        {
            rigNamesExpr = $"(SELECT GROUP_CONCAT(r2.ZNAME, ', ') FROM {LegacyRigLinkTable} lr LEFT JOIN ZRIG r2 ON r2.Z_PK = lr.{rigLink.RigColumn} AND r2.Z_ENT = {EntRig} WHERE lr.{rigLink.LogEntryColumn} = l.Z_PK)";
        }
        var objectPhotoBlobExpr = $"CASE WHEN {photoExpr} IS NULL OR TRIM({photoExpr}) = '' THEN (SELECT oi.ZIMAGE FROM ZOBJECTIMAGE oi WHERE oi.ZOBJECT = o.Z_PK AND oi.Z_ENT = 8 LIMIT 1) ELSE NULL END";

        var sql = $@"
SELECT
    l.Z_PK AS Id,
    l.ZJUMPNUMBER AS NumeroSalto,
    CAST(l.ZDATE AS TEXT) AS DateText,
    o.ZNAME AS Oggetto,
    jt.ZNAME AS TipoSalto,
    l.ZNOTES AS Note,
    {photoExpr} AS ObjectPhotoPath,
    {objectPhotoBlobExpr} AS ObjectPhotoBlob,
    (SELECT li.ZIMAGE FROM ZLOGENTRYIMAGE li WHERE li.ZLOGENTRY = l.Z_PK AND li.Z_ENT = 6 LIMIT 1) AS JumpPhotoBlob,
    NULL AS Latitude,
    NULL AS Longitude,
    CAST({delayExpr} AS TEXT) AS DelaySecondsText,
    CAST({headingExpr} AS TEXT) AS HeadingDegreesText,
    {rigNamesExpr} AS RigNamesCsv
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
            HeadingDegrees = ParseNullableInt(row.HeadingDegreesText),
            RigNames = ParseRigNames(row.RigNamesCsv)
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
            HeadingDegrees = ParseNullableInt(row.HeadingDegreesText),
            RigNames = ParseRigNames(row.RigNamesCsv)
        };

    private SQLiteAsyncConnection Open(string dbPath)
        => new(new SQLiteConnectionString(dbPath, storeDateTimeAsTicks: false));

    private Task<SQLiteAsyncConnection?> TryOpenDbAsync()
    {
        var dbPath = ResolveDbPath();
        if (!File.Exists(dbPath))
            return Task.FromResult<SQLiteAsyncConnection?>(null);

        try
        {
            return Task.FromResult<SQLiteAsyncConnection?>(Open(dbPath));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ReturnWithLog<SQLiteAsyncConnection?>(null, ex, category: LogCategories.RuntimeError));
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

    private static (double? Latitude, double? Longitude) ParseLatLonFromPosition(string? position)
    {
        if (string.IsNullOrWhiteSpace(position))
            return (null, null);

        var matches = Regex.Matches(position, @"[-+]?\d+(?:[.,]\d+)?");
        if (matches.Count < 2)
            return (null, null);

        if (!TryParseCoordinate(matches[0].Value, out var lat))
            return (null, null);
        if (!TryParseCoordinate(matches[1].Value, out var lon))
            return (null, null);

        return (lat, lon);
    }

    private static bool TryParseCoordinate(string token, out double value)
    {
        var normalized = token.Trim().Replace(',', '.');
        return double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

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

    private static List<string> ParseRigNames(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv))
            return [];

        return csv
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private async Task<IReadOnlyList<string>> GetLegacySimpleNamesAsync(string tableName, int ent)
    {
        var db = await TryOpenDbAsync();
        if (db is null)
            return Array.Empty<string>();

        try
        {
            if (!await HasTableAsync(db, tableName))
                return Array.Empty<string>();

            return (await db.QueryAsync<NameRow>(
                    $"SELECT ZNAME AS Name FROM {tableName} WHERE Z_ENT = ? AND ZNAME IS NOT NULL AND TRIM(ZNAME) <> '' ORDER BY ZNAME COLLATE NOCASE;",
                    ent))
                .Select(x => x.Name.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception ex)
        {
            return ReturnWithLog(Array.Empty<string>(), ex, details: $"table={tableName};ent={ent}");
        }
    }

    private async Task<bool> AddLegacySimpleNameAsync(string tableName, int ent, string name, string? notes)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        var db = await TryOpenDbAsync();
        if (db is null)
            return false;

        try
        {
            if (!await HasTableAsync(db, tableName))
                return false;

            var columns = await GetTableColumnsAsync(db, tableName);
            var hasLastModifiedUtc = columns.Contains("ZLASTMODIFIEDUTC");
            var hasNotes = columns.Contains("ZNOTES");
            var hasIsActive = columns.Contains("ZISACTIVE");
            var hasIsDefault = columns.Contains("ZISDEFAULT");
            var lastModifiedUtc = CurrentLegacyTimestamp();

            await db.RunInTransactionAsync(conn =>
            {
                var pk = AllocateCoreDataPk(conn, ent, tableName);
                var insertColumns = new List<string> { "Z_PK", "Z_ENT", "Z_OPT", "ZNAME" };
                var values = new List<object?> { pk, ent, 1, name.Trim() };

                if (hasIsActive)
                {
                    insertColumns.Add("ZISACTIVE");
                    values.Add(1);
                }

                if (hasIsDefault)
                {
                    insertColumns.Add("ZISDEFAULT");
                    values.Add(0);
                }

                if (hasNotes)
                {
                    insertColumns.Add("ZNOTES");
                    values.Add(notes);
                }

                if (hasLastModifiedUtc)
                {
                    insertColumns.Add("ZLASTMODIFIEDUTC");
                    values.Add(lastModifiedUtc);
                }

                var placeholders = string.Join(", ", Enumerable.Repeat("?", insertColumns.Count));
                conn.Execute(
                    $"INSERT INTO {tableName} ({string.Join(", ", insertColumns)}) VALUES ({placeholders});",
                    values.ToArray());

                UpdateLegacyUniqueIdIfPresent(conn, tableName, pk, ent, columns);
            });

            return true;
        }
        catch (Exception ex)
        {
            return ReturnWithLog(false, ex, details: $"table={tableName};ent={ent};name={name.Trim()}");
        }
    }

    private static double CurrentLegacyTimestamp()
        => ToAppleReferenceSeconds(DateTime.UtcNow);

    private static async Task<IReadOnlyList<int>> ResolveLegacyRigIdsAsync(SQLiteAsyncConnection db, IEnumerable<string>? rigNames)
    {
        if (rigNames is null)
            return Array.Empty<int>();

        var normalized = rigNames
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (normalized.Count == 0)
            return Array.Empty<int>();

        var ids = new List<int>(normalized.Count);
        foreach (var rigName in normalized)
        {
            var id = await FindLegacyIdByNameAsync(db, "ZRIG", rigName);
            if (id.HasValue)
                ids.Add(id.Value);
        }

        return ids
            .Distinct()
            .ToList();
    }

    private static async Task<LegacyRigLinkInfo?> GetLegacyRigLinkInfoAsync(SQLiteAsyncConnection db)
    {
        if (!await HasTableAsync(db, LegacyRigLinkTable))
            return null;

        var columns = await db.QueryAsync<TableInfoRow>($"PRAGMA table_info('{LegacyRigLinkTable}');");
        if (columns.Count == 0)
            return null;

        var names = columns
            .Select(x => x.Name)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList();

        var logEntryColumn = names.FirstOrDefault(x => x.Contains("LOGENTR", StringComparison.OrdinalIgnoreCase));
        var rigColumn = names.FirstOrDefault(x => x.Contains("RIG", StringComparison.OrdinalIgnoreCase));

        if (string.IsNullOrWhiteSpace(logEntryColumn) || string.IsNullOrWhiteSpace(rigColumn))
            return null;

        return new LegacyRigLinkInfo(logEntryColumn, rigColumn);
    }

    private static void ReplaceLegacyRigLinks(SQLiteConnection conn, LegacyRigLinkInfo linkInfo, int logEntryPk, IReadOnlyList<int> rigIds)
    {
        conn.Execute($"DELETE FROM {LegacyRigLinkTable} WHERE {linkInfo.LogEntryColumn} = ?;", logEntryPk);

        if (rigIds.Count == 0)
            return;

        foreach (var rigId in rigIds.Distinct())
        {
            conn.Execute(
                $"INSERT INTO {LegacyRigLinkTable} ({linkInfo.LogEntryColumn}, {linkInfo.RigColumn}) VALUES (?, ?);",
                logEntryPk,
                rigId);
        }
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
            Region = row.Region,
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
            "ZDEPLOYMENTTYPE" => EntDeploymentType,
            "ZPILOTCHUTETYPE" => EntPilotChuteType,
            "ZSLIDERTYPE" => EntSliderType,
            "ZBRAKESETTING" => EntBrakeSetting,
            "ZLOGENTRY" => EntLogEntry,
            "ZLOGENTRYIMAGE" => EntLogEntryImage,
            "ZOBJECTIMAGE" => EntObjectImage,
            _ => null
        };

    private static async Task UpdateModernJumpOptionalFieldsAsync(SQLiteAsyncConnection db, int jumpId, HashSet<string> columns, JumpListItem jump)
    {
        var delayColumn = FindFirstExistingColumn(columns, "DelaySeconds", "Delay", "OpeningDelay", "DelayInSeconds");
        if (!string.IsNullOrWhiteSpace(delayColumn))
            await db.ExecuteAsync($"UPDATE Jump SET {delayColumn} = ? WHERE Id = ?;", jump.DelaySeconds, jumpId);

        var headingColumn = FindFirstExistingColumn(columns, "HeadingDegrees", "OpeningHeading", "Heading", "OpeningDegrees", "ApertureDegrees", "OpeningAngle");
        if (!string.IsNullOrWhiteSpace(headingColumn))
            await db.ExecuteAsync($"UPDATE Jump SET {headingColumn} = ? WHERE Id = ?;", jump.HeadingDegrees, jumpId);
    }

    private static void UpdateLegacyJumpOptionalFields(SQLiteConnection conn, int jumpPk, HashSet<string> columns, JumpListItem jump)
    {
        var delayColumn = FindFirstExistingColumn(columns, "ZDELAY", "ZDELAYSECONDS", "ZDELAYINSECONDS");
        if (!string.IsNullOrWhiteSpace(delayColumn))
            conn.Execute($"UPDATE ZLOGENTRY SET {delayColumn} = ? WHERE Z_PK = ? AND Z_ENT = ?;", jump.DelaySeconds, jumpPk, EntLogEntry);

        var headingColumn = FindFirstExistingColumn(columns, "ZOPENINGDIRECTION", "ZHEADING", "ZOPENINGHEADING", "ZTRACK");
        if (!string.IsNullOrWhiteSpace(headingColumn))
            conn.Execute($"UPDATE ZLOGENTRY SET {headingColumn} = ? WHERE Z_PK = ? AND Z_ENT = ?;", jump.HeadingDegrees, jumpPk, EntLogEntry);
    }

    private static void UpdateLegacyUniqueIdIfPresent(SQLiteConnection conn, string tableName, int pk, int ent, HashSet<string> columns)
    {
        if (!columns.Contains("ZUNIQUEID"))
            return;

        conn.Execute(
            $"UPDATE {tableName} SET ZUNIQUEID = COALESCE(NULLIF(ZUNIQUEID, ''), ?) WHERE Z_PK = ? AND Z_ENT = ?;",
            Guid.NewGuid().ToString("D"),
            pk,
            ent);
    }

    private static string? FindFirstExistingColumn(HashSet<string> columns, params string[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (columns.Contains(candidate))
                return candidate;
        }

        return null;
    }

    private static int AllocateCoreDataPk(SQLiteConnection conn, int entityId, string tableName)
    {
        var seed = conn.ExecuteScalar<int?>($"SELECT IFNULL(MAX(Z_PK), 0) FROM {tableName};") ?? 0;
        conn.Execute(
            "INSERT OR IGNORE INTO Z_PRIMARYKEY (Z_ENT, Z_NAME, Z_SUPER, Z_MAX) VALUES (?, ?, 0, ?);",
            entityId,
            LegacyPrimaryKeyNameForTable(tableName),
            seed);

        var updated = conn.Execute(
            "UPDATE Z_PRIMARYKEY SET Z_MAX = COALESCE(Z_MAX, 0) + 1 WHERE Z_ENT = ?;",
            entityId);

        if (updated == 0)
            throw new InvalidOperationException($"Unable to allocate primary key for entity {entityId}.");

        return conn.ExecuteScalar<int>("SELECT Z_MAX FROM Z_PRIMARYKEY WHERE Z_ENT = ? LIMIT 1;", entityId);
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
            "ZDEPLOYMENTTYPE" => "DeploymentType",
            "ZPILOTCHUTETYPE" => "PilotChuteType",
            "ZSLIDERTYPE" => "SliderType",
            "ZBRAKESETTING" => "BrakeSetting",
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
        public string? RigNamesCsv { get; set; }
    }

    private sealed class ObjectCatalogRow
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public string? ObjectType { get; set; }
        public string? Notes { get; set; }
        public string? HeightMeters { get; set; }
        public string? HeightUnit { get; set; }
        public string? Region { get; set; }
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

    private sealed class LegacyRigLinkInfo
    {
        public LegacyRigLinkInfo(string logEntryColumn, string rigColumn)
        {
            LogEntryColumn = logEntryColumn;
            RigColumn = rigColumn;
        }

        public string LogEntryColumn { get; }
        public string RigColumn { get; }
    }

    private sealed class ModernNormalizeRow
    {
        public int Id { get; set; }
        public string? DateText { get; set; }
    }

    private sealed class LegacyNormalizeRow
    {
        public int Pk { get; set; }
        public int Number { get; set; }
        public string? DateText { get; set; }
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








