using BaseLogApp.Models;
using SQLite;

namespace BaseLogApp.Data;

public class JumpsRepository : IJumpsRepository
{
    private readonly SQLiteAsyncConnection _db;

    public JumpsRepository(string dbPath)
    {
        _db = new SQLiteAsyncConnection(dbPath);
        // non creare tabelle sul DB legacy
    }

    public async Task<IList<Jump>> GetAllAsync()
    {
        var list = await _db.Table<Jump>()
            .OrderByDescending(j => j.JumpNumber) // usa la proprieta mappata a ZJUMPNUMBER
            .ToListAsync();
        return list;
    }

    public Task<Jump?> GetByIdAsync(int id) =>
        _db.FindAsync<Jump>(id);

    public Task<ExitObject?> GetObjectAsync(int id) =>
        _db.FindAsync<ExitObject>(id);

    public async Task<byte[]?> GetObjectThumbnailAsync(int objectId)
    {
        const string sql = "SELECT ZIMAGE FROM ZOBJECTIMAGE WHERE ZOBJECT = ? LIMIT 1";
        var rows = await _db.QueryScalarsAsync<byte[]>(sql, objectId);
        return rows.FirstOrDefault();
    }

    public Task<JumpType?> GetJumpTypeAsync(int id) =>
        _db.FindAsync<JumpType>(id);
}
