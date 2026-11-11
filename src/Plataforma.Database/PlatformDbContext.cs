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
    }
}
