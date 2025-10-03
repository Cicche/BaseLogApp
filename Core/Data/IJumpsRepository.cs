using BaseLog.Models;

namespace BaseLog.Data;

public interface IJumpsRepository
{
    Task<IList<Jump>> GetAllAsync();
    Task<Jump?> GetByIdAsync(int id);
    Task<int> UpsertAsync(Jump jump);
    Task<int> DeleteAsync(int id);
}
