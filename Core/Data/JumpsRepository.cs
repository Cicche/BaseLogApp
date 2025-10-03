using SQLite;
using BaseLog.Models;

namespace BaseLog.Data;

public class JumpsRepository : IJumpsRepository
{
    private readonly SQLiteAsyncConnection _db;

    public JumpsRepository(string dbPath)
    {
        _db = new SQLiteAsyncConnection(dbPath);
        _ = _db.CreateTableAsync<Jump>();
    }

    public async Task<IList<Jump>> GetAllAsync()
    {
        var list = await _db.Table<Jump>()
            .OrderByDescending(j => j.JumpDateUtc)
            .ToListAsync();
        return list;
    }

    public Task<Jump?> GetByIdAsync(int id) => _db.FindAsync<Jump>(id);

    public Task<int> UpsertAsync(Jump jump)
        => jump.Id == 0 ? _db.InsertAsync(jump) : _db.UpdateAsync(jump);

    public Task<int> DeleteAsync(int id) => _db.DeleteAsync<Jump>(id);
}
