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
                            CAST(Longitude AS TEXT) AS Longitude,
                            NULL AS DelaySecondsText,
                            NULL AS HeadingDegreesText
                        FROM Jump
                        ORDER BY Id DESC;";

                    var jumpRows = await db.QueryAsync<JumpRow>(jumpSql);
                    if (jumpRows.Count > 0)
                    {
                        return jumpRows.Select(ToJumpItemModern).ToList();
                    }
                }

                var photoExpr = await ResolveObjectPhotoExpressionAsync(db);
                var logColumns = await GetTableColumnsAsync(db, "ZLOGENTRY");
                var delayExpr = BuildColumnExpression("l", logColumns, "ZDELAY", "ZDELAYSECONDS", "ZDELAYINSECONDS");
                var headingExpr = BuildColumnExpression("l", logColumns, "ZHEADING", "ZOPENINGHEADING", "ZTRACK");
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
            Longitude = r.Longitude,
            DelaySeconds = ParseNullableInt(r.DelaySecondsText),
            HeadingDegrees = ParseNullableInt(r.HeadingDegreesText)
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
            JumpPhotoBlob = r.JumpPhotoBlob,
            DelaySeconds = ParseNullableInt(r.DelaySecondsText),
            HeadingDegrees = ParseNullableInt(r.HeadingDegreesText)
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

        public async Task<IReadOnlyList<string>> GetJumpTypeNamesAsync()
        {
            var dbPath = ResolveDbPath();
            if (!File.Exists(dbPath)) return Array.Empty<string>();

            try
            {
                var db = new SQLiteAsyncConnection(new SQLiteConnectionString(dbPath, false));
                if (await HasTableAsync(db, "ZJUMPTYPE"))
                {
                    return (await db.QueryAsync<ObjectNameRow>("SELECT ZNAME AS Name FROM ZJUMPTYPE WHERE ZNAME IS NOT NULL AND TRIM(ZNAME) <> '';"))
                        .Select(x => x.Name!.Trim())
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(x => x)
                        .ToList();
                }

                if (await HasTableAsync(db, "Jump"))
                {
                    return (await db.QueryAsync<ObjectNameRow>("SELECT ExitName AS Name FROM Jump WHERE ExitName IS NOT NULL AND TRIM(ExitName) <> '';"))
                        .Select(x => x.Name!.Trim())
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
            var dbPath = ResolveDbPath();
            if (!File.Exists(dbPath)) return Array.Empty<string>();

            try
            {
                var db = new SQLiteAsyncConnection(new SQLiteConnectionString(dbPath, false));
                if (!await HasTableAsync(db, "ZRIG"))
                    return Array.Empty<string>();

                return (await db.QueryAsync<ObjectNameRow>("SELECT ZNAME AS Name FROM ZRIG WHERE ZNAME IS NOT NULL AND TRIM(ZNAME) <> '';"))
                    .Select(x => x.Name!.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x => x)
                    .ToList();
            }
            catch
            {
                return Array.Empty<string>();
            }
        }

        public async Task<IReadOnlyList<ObjectCatalogItem>> GetObjectsCatalogAsync()
        {
            var dbPath = ResolveDbPath();
            if (!File.Exists(dbPath)) return Array.Empty<ObjectCatalogItem>();

            try
            {
                var db = new SQLiteAsyncConnection(new SQLiteConnectionString(dbPath, false));
                if (!await HasTableAsync(db, "ZOBJECT")) return Array.Empty<ObjectCatalogItem>();

                var columns = await GetTableColumnsAsync(db, "ZOBJECT");
                var typeExpr = BuildColumnExpression("o", columns, "ZOBJECTTYPE");
                var descExpr = BuildColumnExpression("o", columns, "ZDESCRIPTION", "ZDESC");
                var heightExpr = BuildColumnExpression("o", columns, "ZHEIGHT", "ZHEIGHTMETERS", "ZALTITUDE");
                var heightUnitExpr = BuildColumnExpression("o", columns, "ZHEIGHTUNIT");
                var latExpr = BuildColumnExpression("o", columns, "ZLATITUDE");
                var lonExpr = BuildColumnExpression("o", columns, "ZLONGITUDE");

                var sql = $@"SELECT o.Z_PK AS Id,
                                    o.ZNAME AS Name,
                                    o.ZNOTES AS Notes,
                                    {typeExpr} AS ObjectType,
                                    {descExpr} AS Description,
                                    CAST({heightExpr} AS TEXT) AS HeightMeters,
                                    {heightUnitExpr} AS HeightUnit,
                                    CAST({latExpr} AS TEXT) AS Latitude,
                                    CAST({lonExpr} AS TEXT) AS Longitude,
                                    (SELECT oi.ZIMAGE FROM ZOBJECTIMAGE oi WHERE oi.ZOBJECT = o.Z_PK LIMIT 1) AS PhotoBlob
                             FROM ZOBJECT o
                             ORDER BY o.ZNAME;";

                var rows = await db.QueryAsync<ObjectCatalogItem>(sql);
                foreach (var row in rows)
                    PopulateObjectFieldsFromNotes(row);

                return rows.Where(x => !string.IsNullOrWhiteSpace(x.Name)).ToList();
            }
            catch { return Array.Empty<ObjectCatalogItem>(); }
        }

        public async Task<(double? Latitude, double? Longitude)> GetObjectCoordinatesAsync(string? objectName)
        {
            if (string.IsNullOrWhiteSpace(objectName))
                return (null, null);

            var dbPath = ResolveDbPath();
            if (!File.Exists(dbPath))
                return (null, null);

            try
            {
                var db = new SQLiteAsyncConnection(new SQLiteConnectionString(dbPath, false));
                if (!await HasTableAsync(db, "ZOBJECT"))
                    return (null, null);

                var row = (await db.QueryAsync<ObjectCoordRow>("SELECT ZLATITUDE AS Latitude, ZLONGITUDE AS Longitude FROM ZOBJECT WHERE LOWER(TRIM(ZNAME)) = LOWER(TRIM(?)) LIMIT 1;", objectName.Trim())).FirstOrDefault();
                return row is null ? (null, null) : (row.Latitude, row.Longitude);
            }
            catch
            {
                return (null, null);
            }
        }

        public async Task<IReadOnlyList<CatalogItem>> GetRigsCatalogAsync()
        {
            var dbPath = ResolveDbPath();
            if (!File.Exists(dbPath)) return Array.Empty<CatalogItem>();

            try
            {
                var db = new SQLiteAsyncConnection(new SQLiteConnectionString(dbPath, false));
                if (!await HasTableAsync(db, "ZRIG")) return Array.Empty<CatalogItem>();
                return (await db.QueryAsync<CatalogItem>("SELECT Z_PK AS Id, ZNAME AS Name, ZNOTES AS Notes FROM ZRIG ORDER BY ZNAME;"))
                    .Where(x => !string.IsNullOrWhiteSpace(x.Name))
                    .ToList();
            }
            catch { return Array.Empty<CatalogItem>(); }
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
                        await AppendDbLogAsync(dbPath, "Shift numeri salto", new[] { $"ZLOGENTRY: +1 da {fromNumber} (escluso pk={excludeId.Value})" });
                    }
                    else
                    {
                        await db.ExecuteAsync("UPDATE ZLOGENTRY SET ZJUMPNUMBER = ZJUMPNUMBER + 1 WHERE ZJUMPNUMBER >= ?;", fromNumber);
                        await AppendDbLogAsync(dbPath, "Shift numeri salto", new[] { $"ZLOGENTRY: +1 da {fromNumber}" });
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
                        await AppendDbLogAsync(dbPath, "Shift numeri salto", new[] { $"Jump: +1 da {fromNumber} (escluso id={excludeId.Value})" });
                    }
                    else
                    {
                        await db.ExecuteAsync("UPDATE Jump SET JumpNumber = JumpNumber + 1 WHERE JumpNumber >= ?;", fromNumber);
                        await AppendDbLogAsync(dbPath, "Shift numeri salto", new[] { $"Jump: +1 da {fromNumber}" });
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
                var logLines = new List<string>();

                if (await HasTableAsync(db, "ZLOGENTRY"))
                {
                    var cols = await GetTableColumnsAsync(db, "ZLOGENTRY");
                    if (!cols.Contains("ZJUMPNUMBER")) return 0;

                    var rows = await db.QueryAsync<LegacyOrderRow>("SELECT Z_PK AS Pk, IFNULL(ZJUMPNUMBER,0) AS Number FROM ZLOGENTRY ORDER BY Z_PK ASC;");
                    var expected = 1;
                    var changes = 0;
                    foreach (var row in rows)
                    {
                        if (row.Number != expected)
                        {
                            await db.ExecuteAsync("UPDATE ZLOGENTRY SET ZJUMPNUMBER=? WHERE Z_PK=?;", expected, row.Pk);
                            logLines.Add($"ZLOGENTRY pk={row.Pk}: numero salto {row.Number} -> {expected}");
                            changes++;
                        }
                        expected++;
                    }

                    if (changes > 0)
                        await AppendDbLogAsync(dbPath, "Normalizzazione salti (ordine Z_PK)", logLines);

                    return changes;
                }

                if (await HasTableAsync(db, "Jump"))
                {
                    var cols = await GetTableColumnsAsync(db, "Jump");
                    if (!cols.Contains("JumpNumber")) return 0;

                    var rows = await db.QueryAsync<LegacyOrderRow>("SELECT Id AS Pk, IFNULL(JumpNumber,0) AS Number FROM Jump ORDER BY Id ASC;");
                    var expected = 1;
                    var changes = 0;
                    foreach (var row in rows)
                    {
                        if (row.Number != expected)
                        {
                            await db.ExecuteAsync("UPDATE Jump SET JumpNumber=? WHERE Id=?;", expected, row.Pk);
                            logLines.Add($"Jump id={row.Pk}: numero salto {row.Number} -> {expected}");
                            changes++;
                        }
                        expected++;
                    }

                    if (changes > 0)
                        await AppendDbLogAsync(dbPath, "Normalizzazione salti (ordine Id)", logLines);

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

                return refs > 0
                    ? (false, $"Impossibile eliminare: object associato a {refs} salto/i.")
                    : (true, null);
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

                return refs > 0
                    ? (false, $"Impossibile eliminare: rig associato a {refs} salto/i.")
                    : (true, null);
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

                return refs > 0
                    ? (false, $"Impossibile eliminare: tipo salto associato a {refs} salto/i.")
                    : (true, null);
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

        public async Task<bool> ImportFullDbAsync(string sourcePath)
        {
            try
            {
                var dbPath = ResolveDbPath();
                File.Copy(sourcePath, dbPath, true);
                await AppendDbLogAsync(dbPath, "Import DB", new[] { $"Importato DB da: {sourcePath}" });
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

        private static async Task AppendDbLogAsync(string dbPath, string title, IEnumerable<string> lines)
        {
            try
            {
                var logPath = dbPath + ".log";
                var header = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {title}";
                var content = string.Join(Environment.NewLine, lines.Select(l => $" - {l}"));
                var block = $"{header}{Environment.NewLine}{content}{Environment.NewLine}";
                await File.AppendAllTextAsync(logPath, block + Environment.NewLine);
            }
            catch
            {
                // best-effort log writing
            }
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
        private sealed class LegacyOrderRow { public int Pk { get; set; } public int Number { get; set; } }
    }
}
