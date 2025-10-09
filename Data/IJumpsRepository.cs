using BaseLogApp.Models;

namespace BaseLogApp.Data;

public interface IJumpsRepository
{
    Task<IList<Jump>> GetAllAsync();
    Task<Jump?> GetByIdAsync(int id);

    // lookup oggetto collegato
    Task<ExitObject?> GetObjectAsync(int id);
    Task<byte[]?> GetObjectThumbnailAsync(int objectId);
    Task<JumpType?> GetJumpTypeAsync(int id);
}
