using BoardGames.Models;
using BoardGames.Models.Avalon;
using Microsoft.EntityFrameworkCore;

namespace BoardGames.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users { get; set; }
    public DbSet<GameBalance> GameBalances { get; set; }
    public DbSet<RefreshToken> RefreshTokens { get; set; }

    public DbSet<AvalonGameHistory> AvalonGameHistories { get; set; }
    public DbSet<AvalonGamePlayer> AvalonGamePlayers { get; set; }
    public DbSet<AvalonGameProposal> AvalonGameProposals { get; set; }
    public DbSet<AvalonGameVote> AvalonGameVotes { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<GameBalance>()
            .HasIndex(b => new { b.UserId, b.GameType })
            .IsUnique();

        modelBuilder.Entity<AvalonGameHistory>(entity =>
        {
            entity.HasIndex(e => e.EndedAt).IsDescending();
            entity.Property(e => e.Winner).HasConversion<string>().HasMaxLength(10);
            entity.Property(e => e.RoomId).HasMaxLength(20);
            entity.Property(e => e.WinReason).HasMaxLength(200);

            entity.HasMany(e => e.Players)
                .WithOne(p => p.Game)
                .HasForeignKey(p => p.GameId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(e => e.Proposals)
                .WithOne(p => p.Game)
                .HasForeignKey(p => p.GameId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AvalonGamePlayer>(entity =>
        {
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => e.GameId);
            entity.Property(e => e.Role).HasConversion<string>().HasMaxLength(20);
            entity.Property(e => e.Nickname).HasMaxLength(50);
            entity.Property(e => e.BalanceDelta).HasPrecision(10, 2);
        });

        modelBuilder.Entity<AvalonGameProposal>(entity =>
        {
            entity.HasIndex(e => e.GameId);
            entity.Property(e => e.MissionResult).HasConversion<string>().HasMaxLength(10);

            entity.HasMany(e => e.Votes)
                .WithOne(v => v.Proposal)
                .HasForeignKey(v => v.ProposalId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AvalonGameVote>(entity =>
        {
            entity.HasIndex(e => e.ProposalId);
        });
    }
}