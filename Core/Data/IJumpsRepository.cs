using BaseLogApp.Models;

namespace BaseLogApp.Data;

public interface IJumpsRepository
{
    Task<IList<Jump>> GetAllAsync();
    Task<Jump?> GetByIdAsync(int id);
    Task<int> UpsertAsync(Jump jump);
    Task<int> DeleteAsync(int id);
}
