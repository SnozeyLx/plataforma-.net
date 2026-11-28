namespace Plataforma.Core.Configuration;

/// <summary>Parses configured CORS origin lists for HTTP APIs serving WebGL and portal clients.</summary>
public static class CorsOriginHelper
{
    /// <summary>Splits a comma- or semicolon-delimited origin string into normalized origin URLs.</summary>
    public static string[] ParseAllowedOrigins(string? rawOrigins)
    {
        if (string.IsNullOrWhiteSpace(rawOrigins))
        {
            return [];
        }

        var origins = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in rawOrigins.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!Uri.TryCreate(entry, UriKind.Absolute, out var uri))
            {
                continue;
            }

            if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var normalizedOrigin = uri.GetLeftPart(UriPartial.Authority);
            if (seen.Add(normalizedOrigin))
            {
                origins.Add(normalizedOrigin);
            }
        }

        return [.. origins];
    }

    /// <summary>Returns whether a request origin points to a loopback host for local WebGL development.</summary>
    public static bool IsLoopbackOrigin(string? origin) =>
        Uri.TryCreate(origin, UriKind.Absolute, out var uri)
        && (string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            || string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        && uri.IsLoopback;
}
