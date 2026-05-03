namespace LeadScoring.Api.Services;

/// <summary>
/// Allowlists redirects to <c>*.hiperbrains.com</c> over HTTPS (or HTTP on localhost-only for development).
/// </summary>
public static class RedirectSafety
{
    /// <summary>If <paramref name="url"/> is absent or fails validation, returns <paramref name="validatedDefault"/>.</summary>
    public static Uri SafeRedirectDestination(string? url, Uri validatedDefault)
    {
        if (TryGetAllowedAbsoluteRedirect(url, out var resolved) && resolved is not null)
        {
            return resolved;
        }

        return validatedDefault;
    }

    public static bool TryGetAllowedAbsoluteRedirect(string? url, out Uri? absolute)
    {
        absolute = null;
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        url = url.Trim();
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (!(string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
              string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        var allowHttp = string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase);
        var hostOk = AllowHttpLocallyAndHiperbrainsOnly(uri, allowHttp);
        if (!hostOk)
        {
            return false;
        }

        try
        {
            var ub = new UriBuilder(uri);
            ub.Fragment = string.Empty;
            absolute = ub.Uri;
        }
        catch
        {
            absolute = uri;
        }

        return true;
    }

    /// <summary>Build absolute base URL for the email gate SPA (typically same deployment host).</summary>
    public static Uri BuildEmailGateUri(HttpRequest request, string? configuredOriginOverride)
    {
        if (!string.IsNullOrWhiteSpace(configuredOriginOverride) &&
            Uri.TryCreate(configuredOriginOverride.Trim().TrimEnd('/'), UriKind.Absolute, out var o))
        {
            return o;
        }

        return new Uri($"{request.Scheme}://{request.Host.Value}");
    }

    public static Uri DefaultHiperbrainsSite { get; } = new Uri("https://www.hiperbrains.com/");

    private static bool AllowHttpLocallyAndHiperbrainsOnly(Uri uri, bool isHttpScheme)
    {
        var host = uri.IdnHost;
        var isLocalDev = host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
                         || host == "127.0.0.1"
                         || host == "[::1]";

        if (isHttpScheme && !isLocalDev)
        {
            return false;
        }

        if (isHttpScheme && isLocalDev)
        {
            return true;
        }

        return host.Equals("hiperbrains.com", StringComparison.OrdinalIgnoreCase)
               || host.EndsWith(".hiperbrains.com", StringComparison.OrdinalIgnoreCase);
    }
}
