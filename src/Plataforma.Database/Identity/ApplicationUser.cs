using Microsoft.AspNetCore.Identity;

namespace Plataforma.Database.Identity;

/// <summary>
/// Represents a platform account managed by ASP.NET Core Identity.
/// </summary>
public sealed class ApplicationUser : IdentityUser<Guid>
{
    public string? PlayerName { get; set; }

    public string? NormalizedPlayerName { get; set; }

    public int ProfileIconId { get; set; } = 1;
}
