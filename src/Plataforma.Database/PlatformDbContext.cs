using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Plataforma.Database.Entities;
using Plataforma.Database.Identity;

namespace Plataforma.Database;

/// <summary>
/// Database context for platform accounts and session tokens.
/// </summary>
public sealed class PlatformDbContext(
    DbContextOptions<PlatformDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options)
{
    /// <summary>
    /// Gets the refresh token records.
    /// </summary>
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    /// <summary>
    /// Gets the games in the platform catalog.
    /// </summary>
    public DbSet<Game> Games => Set<Game>();

    /// <summary>
    /// Gets aggregate player progression for each game.
    /// </summary>
    public DbSet<GamePlayerProgress> GamePlayerProgress => Set<GamePlayerProgress>();

    /// <summary>
    /// Gets the recorded game sessions.
    /// </summary>
    public DbSet<GameSession> GameSessions => Set<GameSession>();

    /// <summary>
    /// Gets the achievement definitions for games.
    /// </summary>
    public DbSet<GameAchievement> GameAchievements => Set<GameAchievement>();

    /// <summary>
    /// Gets player progress and unlocks for game achievements.
    /// </summary>
    public DbSet<UserAchievementProgress> UserAchievementProgress => Set<UserAchievementProgress>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<ApplicationUser>()
            .HasIndex(user => user.NormalizedEmail)
            .IsUnique();

        builder.Entity<RefreshToken>(entity =>
        {
            entity.HasKey(token => token.Id);
            entity.Property(token => token.TokenHash)
                .HasMaxLength(64)
                .IsRequired();
            entity.HasIndex(token => token.TokenHash)
                .IsUnique();
            entity.Property(token => token.ReplacedByTokenHash)
                .HasMaxLength(64);
            entity.Property(token => token.ConcurrencyStamp)
                .HasMaxLength(32)
                .IsConcurrencyToken();
            entity.HasIndex(token => new { token.FamilyId, token.RevokedAtUtc });
            entity.HasOne(token => token.User)
                .WithMany()
                .HasForeignKey(token => token.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<Game>(entity =>
        {
            entity.HasKey(game => game.Id);
            entity.Property(game => game.Name)
                .HasMaxLength(120)
                .IsRequired();
            entity.Property(game => game.Slug)
                .HasMaxLength(80)
                .IsRequired();
            entity.HasIndex(game => game.Slug)
                .IsUnique();
            entity.Property(game => game.Description)
                .HasMaxLength(2000);
            entity.Property(game => game.IconUrl)
                .HasMaxLength(2048);
            entity.Property(game => game.Status)
                .HasConversion<string>()
                .HasMaxLength(16)
                .IsRequired();
            entity.HasIndex(game => new { game.Status, game.Name });
        });

        builder.Entity<GamePlayerProgress>(entity =>
        {
            entity.HasKey(progress => new { progress.UserId, progress.GameId });
            entity.Property(progress => progress.Level)
                .HasDefaultValue(1);
            entity.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(progress => progress.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<Game>()
                .WithMany()
                .HasForeignKey(progress => progress.GameId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(progress => new { progress.GameId, progress.Experience });
        });

        builder.Entity<GameSession>(entity =>
        {
            entity.HasKey(session => session.Id);
            entity.Property(session => session.Outcome)
                .HasConversion<string>()
                .HasMaxLength(16);
            entity.HasOne<GamePlayerProgress>()
                .WithMany()
                .HasForeignKey(session => new { session.UserId, session.GameId })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(session => new { session.UserId, session.GameId, session.StartedAtUtc });
            entity.HasIndex(session => new { session.GameId, session.StartedAtUtc });
        });

        builder.Entity<GameAchievement>(entity =>
        {
            entity.HasKey(achievement => achievement.Id);
            entity.Property(achievement => achievement.Code)
                .HasMaxLength(64)
                .IsRequired();
            entity.HasIndex(achievement => new { achievement.GameId, achievement.Code })
                .IsUnique();
            entity.Property(achievement => achievement.Name)
                .HasMaxLength(120)
                .IsRequired();
            entity.Property(achievement => achievement.Description)
                .HasMaxLength(2000);
            entity.Property(achievement => achievement.CriteriaJson)
                .HasColumnType("json");
            entity.Property(achievement => achievement.Status)
                .HasConversion<string>()
                .HasMaxLength(16)
                .IsRequired();
            entity.HasOne<Game>()
                .WithMany()
                .HasForeignKey(achievement => achievement.GameId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(achievement => new { achievement.GameId, achievement.Status });
        });

        builder.Entity<UserAchievementProgress>(entity =>
        {
            entity.HasKey(progress => new { progress.UserId, progress.AchievementId });
            entity.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(progress => progress.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<GameAchievement>()
                .WithMany()
                .HasForeignKey(progress => progress.AchievementId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(progress => new { progress.AchievementId, progress.CompletedAtUtc });
        });
    }
}
