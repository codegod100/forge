using Forge.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace Forge.Data.Services;

public class RepositoryService : IRepositoryService
{
    private readonly ForgeDbContext _db;

    public RepositoryService(ForgeDbContext db)
    {
        _db = db;
    }

    public async Task<IEnumerable<Repository>> GetAllAsync()
    {
        return await _db.Repositories
            .OrderBy(r => r.Owner)
            .ThenBy(r => r.Name)
            .ToListAsync();
    }

    public async Task<IEnumerable<Repository>> GetByOwnerAsync(string owner)
    {
        return await _db.Repositories
            .Where(r => r.Owner == owner)
            .OrderBy(r => r.Name)
            .ToListAsync();
    }

    public async Task<Repository?> GetByIdAsync(Guid id)
    {
        return await _db.Repositories.FindAsync(id);
    }

    public async Task<Repository?> GetByOwnerAndNameAsync(string owner, string name)
    {
        return await _db.Repositories
            .FirstOrDefaultAsync(r => r.Owner == owner && r.Name == name);
    }

    public async Task<Repository> CreateAsync(Repository repository)
    {
        repository.CreatedAt = DateTime.UtcNow;
        repository.UpdatedAt = DateTime.UtcNow;
        
        _db.Repositories.Add(repository);
        await _db.SaveChangesAsync();
        
        return repository;
    }

    public async Task<Repository> UpdateAsync(Repository repository)
    {
        repository.UpdatedAt = DateTime.UtcNow;
        _db.Repositories.Update(repository);
        await _db.SaveChangesAsync();
        
        return repository;
    }

    public async Task DeleteAsync(Guid id)
    {
        var repo = await _db.Repositories.FindAsync(id);
        if (repo != null)
        {
            _db.Repositories.Remove(repo);
            await _db.SaveChangesAsync();
        }
    }
}
