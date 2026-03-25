using Forge.Core.Models;

namespace Forge.Data.Services;

public interface IRepositoryService
{
    Task<IEnumerable<Repository>> GetAllAsync();
    Task<IEnumerable<Repository>> GetByOwnerAsync(string owner);
    Task<Repository?> GetByIdAsync(Guid id);
    Task<Repository?> GetByOwnerAndNameAsync(string owner, string name);
    Task<int> SyncFromDiskAsync(string repositoriesRoot);
    Task<Repository> CreateAsync(Repository repository);
    Task<Repository> UpdateAsync(Repository repository);
    Task DeleteAsync(Guid id);
}
