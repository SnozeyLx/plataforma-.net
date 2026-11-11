using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Plataforma.AuthApi.Configuration;
using Plataforma.AuthApi.Contracts;
using Plataforma.Database;
using Plataforma.Database.Entities;
using Plataforma.Database.Identity;

namespace Plataforma.AuthApi.Services;

/// <summary>
/// Creates signed access tokens and rotates opaque refresh tokens.
/// </summary>
public sealed class TokenService(
    PlatformDbContext dbContext,
    IOptions<JwtOptions> jwtOptions,
    TimeProvider timeProvider,
    ILogger<TokenService> logger) : ITokenService
{
    private readonly JwtOptions _options = jwtOptions.Value;
    private readonly byte[] _signingKey = Convert.FromBase64String(jwtOptions.Value.SigningKeyBase64);

    /// <inheritdoc />
    public async Task<AuthTokensResponse> CreateTokenPairAsync(
        ApplicationUser user,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var familyId = Guid.NewGuid();
        var (rawToken, refreshToken) = CreateRefreshToken(user.Id, familyId, now);

        dbContext.RefreshTokens.Add(refreshToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        return CreateResponse(user, rawToken, refreshToken.ExpiresAtUtc, now);
    }

    /// <inheritdoc />
    public async Task<AuthTokensResponse?> RotateRefreshTokenAsync(
        string refreshToken,
        CancellationToken cancellationToken)
    {
        var tokenHash = HashToken(refreshToken);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var storedToken = await dbContext.RefreshTokens
            .Include(token => token.User)
            .SingleOrDefaultAsync(token => token.TokenHash == tokenHash, cancellationToken);

        if (storedToken is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        var now = timeProvider.GetUtcNow();
        if (storedToken.RevokedAtUtc is not null)
        {
            await transaction.RollbackAsync(cancellationToken);
            await RevokeFamilyAsync(storedToken.FamilyId, now.UtcDateTime, cancellationToken);
            logger.LogWarning(
                "A previously used refresh token was presented for user {UserId}; its token family was revoked.",
                storedToken.UserId);
            return null;
        }

        if (storedToken.ExpiresAtUtc <= now.UtcDateTime)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        var (replacementRawToken, replacement) =
            CreateRefreshToken(storedToken.UserId, storedToken.FamilyId, now);
        storedToken.RevokedAtUtc = now.UtcDateTime;
        storedToken.ReplacedByTokenHash = replacement.TokenHash;
        storedToken.ConcurrencyStamp = Guid.NewGuid().ToString("N");

        dbContext.RefreshTokens.Add(replacement);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(cancellationToken);
            await RevokeFamilyAsync(storedToken.FamilyId, now.UtcDateTime, cancellationToken);
            logger.LogWarning(
                "Concurrent refresh token use detected for user {UserId}; its token family was revoked.",
                storedToken.UserId);
            return null;
        }

        return CreateResponse(storedToken.User, replacementRawToken, replacement.ExpiresAtUtc, now);
    }

    /// <inheritdoc />
    public async Task RevokeRefreshTokenAsync(
        string refreshToken,
        CancellationToken cancellationToken)
    {
        var tokenHash = HashToken(refreshToken);
        var storedToken = await dbContext.RefreshTokens
            .AsNoTracking()
            .SingleOrDefaultAsync(token => token.TokenHash == tokenHash, cancellationToken);

        if (storedToken is not null)
        {
            await RevokeFamilyAsync(storedToken.FamilyId, timeProvider.GetUtcNow().UtcDateTime, cancellationToken);
        }
    }

    private async Task RevokeFamilyAsync(
        Guid familyId,
        DateTime revokedAtUtc,
        CancellationToken cancellationToken)
    {
        await dbContext.RefreshTokens
            .Where(token => token.FamilyId == familyId && token.RevokedAtUtc == null)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(token => token.RevokedAtUtc, revokedAtUtc)
                    .SetProperty(token => token.ConcurrencyStamp, Guid.NewGuid().ToString("N")),
                cancellationToken);
    }

    private (string RawToken, RefreshToken Entity) CreateRefreshToken(
        Guid userId,
        Guid familyId,
        DateTimeOffset now)
    {
        var rawToken = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(64));
        var entity = new RefreshToken
        {
            UserId = userId,
            FamilyId = familyId,
            TokenHash = HashToken(rawToken),
            CreatedAtUtc = now.UtcDateTime,
            ExpiresAtUtc = now.AddDays(_options.RefreshTokenDays).UtcDateTime
        };

        return (rawToken, entity);
    }

    private AuthTokensResponse CreateResponse(
        ApplicationUser user,
        string rawRefreshToken,
        DateTime refreshExpiresAtUtc,
        DateTimeOffset now)
    {
        var accessExpiresAt = now.AddMinutes(_options.AccessTokenMinutes);
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString("D")),
            new Claim(JwtRegisteredClaimNames.Email, user.Email ?? string.Empty),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N"))
        };
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(_signingKey),
            SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: accessExpiresAt.UtcDateTime,
            signingCredentials: credentials);

        return new AuthTokensResponse(
            "Bearer",
            new JwtSecurityTokenHandler().WriteToken(token),
            accessExpiresAt.UtcDateTime,
            rawRefreshToken,
            refreshExpiresAtUtc);
    }

    private static string HashToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
