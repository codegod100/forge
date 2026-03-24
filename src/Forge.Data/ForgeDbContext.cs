using Microsoft.EntityFrameworkCore;
using Forge.Core.Models;

namespace Forge.Data;

public class ForgeDbContext : DbContext
{
    public DbSet<Repository> Repositories => Set<Repository>();
    
    public ForgeDbContext(DbContextOptions<ForgeDbContext> options) : base(options)
    {
    }
    
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Repository>(entity =>
        {
            entity.HasKey(r => r.Id);
            entity.HasIndex(r => new { r.Owner, r.Name }).IsUnique();
            entity.Property(r => r.Name).IsRequired().HasMaxLength(100);
            entity.Property(r => r.Owner).IsRequired().HasMaxLength(100);
            entity.Property(r => r.Description).HasMaxLength(500);
            entity.Property(r => r.Path).IsRequired().HasMaxLength(500);
            entity.Property(r => r.DefaultBranch).IsRequired().HasMaxLength(100);
        });
    }
}
