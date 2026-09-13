namespace AnalystAI.Api.Security;

/// <summary>
/// Middleware that holds every response to the same rules: headers that stop a
/// browser sniffing, framing or leaking the app, and checks that refuse
/// requests a page on another site could have made with a visitor's session.
/// </summary>
public static class RequestGuards
{
    /// <summary>Sent by the app on every request that changes data.</summary>
    public const string CsrfHeader = "X-DataMind-Csrf";

    /// <summary>
    /// The app's own origin, plus Google Fonts. React sets inline styles through
    /// the DOM, which CSP does not govern, so no 'unsafe-inline' is needed.
    /// </summary>
    private const string ContentSecurityPolicy =
        "default-src 'self'; " +
        "script-src 'self'; " +
        "style-src 'self' https://fonts.googleapis.com; " +
        "font-src 'self' https://fonts.gstatic.com; " +
        "img-src 'self' data:; " +
        "connect-src 'self'; " +
        "object-src 'none'; " +
        "base-uri 'none'; " +
        "form-action 'self'; " +
        "frame-ancestors 'none'";

    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app, bool isDevelopment) =>
        app.Use(async (context, next) =>
        {
            context.Response.OnStarting(() =>
            {
                var headers = context.Response.Headers;
                headers.XContentTypeOptions = "nosniff";
                headers.XFrameOptions = "DENY";
                headers["Referrer-Policy"] = "no-referrer";
                headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=(), usb=()";
                headers["Cross-Origin-Opener-Policy"] = "same-origin";
                headers["Cross-Origin-Resource-Policy"] = "same-origin";

                // Swagger UI runs inline script, and is only served in development.
                if (!(isDevelopment && context.Request.Path.StartsWithSegments("/swagger")))
                    headers.ContentSecurityPolicy = ContentSecurityPolicy;

                // API responses carry one account's data: nothing along the way should keep a copy.
                if (context.Request.Path.StartsWithSegments("/api"))
                    headers.CacheControl = "no-store";

                return Task.CompletedTask;
            });

            await next();
        });

    /// <param name="trustLoopbackOrigins">
    /// Development only. The app is then served by a Vite dev or preview server
    /// on a local port and proxied here, so its origin is not this host's — and
    /// Vite moves to the next free port when its usual one is taken, which a
    /// fixed list of ports cannot follow. Fetch Metadata still refuses a page on
    /// another local port that calls the API directly: the browser labels that
    /// request same-site.
    /// </param>
    public static IApplicationBuilder UseCrossSiteRequestChecks(
        this IApplicationBuilder app, IReadOnlyCollection<string> trustedOrigins, bool trustLoopbackOrigins) =>
        app.Use(async (context, next) =>
        {
            var request = context.Request;
            if (!request.Path.StartsWithSegments("/api"))
            {
                await next();
                return;
            }

            // Fetch Metadata. A browser labels every request with where it came
            // from, and nothing in this API is meant to be called by another
            // site. Clients that are not browsers send no label and pass.
            var site = request.Headers["Sec-Fetch-Site"].ToString();
            if (site is "cross-site" or "same-site")
            {
                await Refuse(context, "Cross-site request refused",
                    "This API only answers requests made by the DataMind app itself.");
                return;
            }

            if (!HttpMethods.IsGet(request.Method) && !HttpMethods.IsHead(request.Method)
                && !HttpMethods.IsOptions(request.Method))
            {
                // A form on another site can post with the visitor's cookie, but
                // it cannot add a header — so a forged submission stops here even
                // in a browser too old to send Fetch Metadata.
                if (request.Headers[CsrfHeader] != "1")
                {
                    await Refuse(context, "Missing request header",
                        $"Requests that change data must carry the {CsrfHeader}: 1 header. The app sends it on every such request.");
                    return;
                }

                var origin = request.Headers.Origin.ToString();
                if (origin.Length > 0 && !IsTrusted(origin, request, trustedOrigins, trustLoopbackOrigins))
                {
                    await Refuse(context, "Request origin refused", $"'{origin}' is not this app's origin.");
                    return;
                }
            }

            await next();
        });

    private static bool IsTrusted(
        string origin, HttpRequest request, IReadOnlyCollection<string> trustedOrigins, bool trustLoopbackOrigins)
    {
        if (trustedOrigins.Contains(origin, StringComparer.OrdinalIgnoreCase)) return true;
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri)) return false;

        return string.Equals(uri.Authority, request.Host.Value, StringComparison.OrdinalIgnoreCase)
               || (trustLoopbackOrigins && uri.IsLoopback);
    }

    private static Task Refuse(HttpContext context, string title, string detail) =>
        Results.Problem(title: title, detail: detail, statusCode: StatusCodes.Status403Forbidden)
            .ExecuteAsync(context);
}
