using BoardGames.Models;
using Microsoft.EntityFrameworkCore;

namespace BoardGames.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users { get; set; }
    public DbSet<GameBalance> GameBalances { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<GameBalance>()
            .HasIndex(b => new { b.UserId, b.GameType })
            .IsUnique();
    }
}