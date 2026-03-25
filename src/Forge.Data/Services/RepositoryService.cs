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

    public async Task<int> SyncFromDiskAsync(string repositoriesRoot)
    {
        if (!Directory.Exists(repositoriesRoot))
        {
            return 0;
        }

        var existingPaths = await _db.Repositories
            .Select(r => r.Path)
            .ToHashSetAsync();

        var discovered = Directory.EnumerateDirectories(repositoriesRoot, "*.git", SearchOption.AllDirectories);
        var imported = 0;

        foreach (var repoDir in discovered)
        {
            var relativePath = Path.GetRelativePath(repositoriesRoot, repoDir).Replace('\\', '/');
            if (existingPaths.Contains(relativePath))
            {
                continue;
            }

            var owner = Path.GetDirectoryName(relativePath)?.Replace('\\', '/');
            var name = Path.GetFileNameWithoutExtension(relativePath);

            if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            _db.Repositories.Add(new Repository
            {
                Id = Guid.NewGuid(),
                Owner = owner,
                Name = name,
                Path = relativePath,
                DefaultBranch = ReadDefaultBranch(repoDir),
                IsPrivate = false
            });

            existingPaths.Add(relativePath);
            imported++;
        }

        if (imported > 0)
        {
            await _db.SaveChangesAsync();
        }

        return imported;
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

    private static string ReadDefaultBranch(string repoDir)
    {
        var headPath = Path.Combine(repoDir, "HEAD");
        if (!File.Exists(headPath))
        {
            return "main";
        }

        var head = File.ReadAllText(headPath).Trim();
        const string prefix = "ref: refs/heads/";

        return head.StartsWith(prefix, StringComparison.Ordinal)
            ? head[prefix.Length..]
            : "main";
    }
}
