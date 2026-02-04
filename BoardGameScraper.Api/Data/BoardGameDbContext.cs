using Microsoft.EntityFrameworkCore;
using BoardGameScraper.Api.Data.Entities;

namespace BoardGameScraper.Api.Data;

public class BoardGameDbContext : DbContext
{
    public BoardGameDbContext(DbContextOptions<BoardGameDbContext> options)
        : base(options)
    {
    }

    public DbSet<Game> Games => Set<Game>();
    public DbSet<Rulebook> Rulebooks => Set<Rulebook>();
    public DbSet<CafeInventory> CafeInventories => Set<CafeInventory>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Game configuration
        modelBuilder.Entity<Game>(entity =>
        {
            entity.HasIndex(e => e.BggId).IsUnique();
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.BggRank);
        });

        // Rulebook configuration
        modelBuilder.Entity<Rulebook>(entity =>
        {
            entity.HasIndex(e => e.GameId);
            entity.HasIndex(e => e.Status);

            entity.HasOne(r => r.Game)
                  .WithMany(g => g.Rulebooks)
                  .HasForeignKey(r => r.GameId);
        });

        // CafeInventory configuration
        modelBuilder.Entity<CafeInventory>(entity =>
        {
            entity.HasIndex(e => e.GameId).IsUnique();

            entity.HasOne(i => i.Game)
                  .WithOne(g => g.Inventory)
                  .HasForeignKey<CafeInventory>(i => i.GameId);
        });
    }
}
