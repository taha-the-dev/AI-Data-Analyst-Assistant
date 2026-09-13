using System.Globalization;
using System.Security.Claims;
using System.Threading.RateLimiting;
using AnalystAI.Api.Data;
using AnalystAI.Api.Endpoints;
using AnalystAI.Api.Models;
using AnalystAI.Api.Query;
using AnalystAI.Api.Security;
using AnalystAI.Api.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;

var builder = WebApplication.CreateBuilder(args);

const string FrontendCors = "frontend";

builder.WebHost.ConfigureKestrel(options =>
{
    // No product name and version on every response.
    options.AddServerHeader = false;

    // The largest body the API accepts is an upload; anything bigger is refused
    // before it is read. The margin covers the multipart framing around a file.
    options.Limits.MaxRequestBodySize = InputLimits.UploadBytes + 1024 * 1024;
});

builder.Services.Configure<FormOptions>(options =>
    options.MultipartBodyLengthLimit = InputLimits.UploadBytes + 1024 * 1024);

// SQLite by default, for local development. A PostgreSQL connection string —
// key=value, or the postgresql:// URL a host such as Render provides — switches
// to PostgreSQL, for hosts whose disk does not survive a restart. Each has its
// own context type, because migrations are specific to a provider.
var connectionString = builder.Configuration.GetConnectionString("Default") ?? "Data Source=analystai.db";
if (DatabaseConnection.IsPostgres(connectionString))
    builder.Services.AddDbContext<AppDbContext, PostgresAppDbContext>(options =>
        options.UseNpgsql(DatabaseConnection.ToNpgsql(connectionString)));
else
    builder.Services.AddDbContext<AppDbContext, SqliteAppDbContext>(options =>
        options.UseSqlite(connectionString));

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();

builder.Services.AddScoped<QueryEngine>();
builder.Services.AddScoped<ReportComposer>();
builder.Services.AddScoped<IDatasetContext, DatasetContext>();
builder.Services.AddScoped<SchemaSummary>();

// Planners. The engine only ever receives a QuerySpec, so which planner produced
// it changes nothing downstream. The resolver picks one per request from the
// provider the signed-in account saved.
builder.Services.AddSingleton<KeywordPlanner>();
builder.Services.AddHttpClient<GeminiPlanner>(client =>
    client.Timeout = TimeSpan.FromSeconds(30));
builder.Services.AddHttpClient<OpenRouterPlanner>(client =>
    client.Timeout = TimeSpan.FromSeconds(45));
builder.Services.AddScoped<IPlannerResolver, PlannerResolver>();

// Accounts. ASP.NET Core Identity stores them and hashes passwords with PBKDF2;
// the session is a cookie that no script on the page can read.
builder.Services
    .AddIdentityCore<AppUser>(options =>
    {
        options.User.RequireUniqueEmail = true;
        // The sign-in name is the email address, validated as an address at
        // sign-up. Identity's default character whitelist refused real ones.
        options.User.AllowedUserNameCharacters = "";

        options.Password.RequiredLength = AccountRules.MinPasswordLength;
        options.Password.RequireDigit = false;
        options.Password.RequireLowercase = false;
        options.Password.RequireUppercase = false;
        options.Password.RequireNonAlphanumeric = false;
        options.Password.RequiredUniqueChars = 1;

        options.Lockout.AllowedForNewUsers = true;
        options.Lockout.MaxFailedAccessAttempts = AccountRules.MaxFailedAttempts;
        options.Lockout.DefaultLockoutTimeSpan = AccountRules.LockoutDuration;
    })
    .AddEntityFrameworkStores<AppDbContext>()
    .AddSignInManager();

builder.Services
    .AddAuthentication(IdentityConstants.ApplicationScheme)
    .AddIdentityCookies();

builder.Services.ConfigureApplicationCookie(options =>
{
    var development = builder.Environment.IsDevelopment();

    // __Host- binds the cookie to this exact origin over HTTPS. Development runs
    // on plain http://localhost, where that prefix is not allowed.
    options.Cookie.Name = development ? "DataMind.Session" : "__Host-DataMind.Session";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.SecurePolicy = development ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
    options.Cookie.Path = "/";
    options.ExpireTimeSpan = AccountRules.SessionLifetime;
    options.SlidingExpiration = true;

    // An API answers with a status code, never with a redirect to a sign-in
    // page it does not serve.
    options.Events.OnRedirectToLogin = context =>
        AuthEndpoints.SignInRequired().ExecuteAsync(context.HttpContext);
    options.Events.OnRedirectToAccessDenied = context =>
        Results.Problem(title: "Not allowed", statusCode: StatusCodes.Status403Forbidden)
            .ExecuteAsync(context.HttpContext);
});

// A deleted account's sessions end within minutes rather than at cookie expiry.
builder.Services.Configure<SecurityStampValidatorOptions>(options =>
    options.ValidationInterval = TimeSpan.FromMinutes(5));

builder.Services.AddAuthorization();

// Sessions are encrypted with Data Protection keys, kept in the database beside
// the accounts they protect. In the user profile or on a container's disk they
// were lost on every deploy, and on hosts that wipe the disk whenever a service
// sleeps — signing everyone out each time.
builder.Services.AddDataProtection()
    .SetApplicationName("DataMind")
    .PersistKeysToDbContext<AppDbContext>();

// Behind reverse proxies — Vercel's /api rewrite, then the host's load balancer —
// every connection comes from a proxy. Forwarded headers are honoured only when
// configured, and only ForwardLimit hops deep: trusting them from anyone would
// let a client choose the address its rate limit is counted against.
var trustForwardedHeaders = builder.Configuration.GetValue("Security:TrustForwardedHeaders", false);
if (trustForwardedHeaders)
{
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.ForwardLimit = builder.Configuration.GetValue("Security:ForwardLimit", 1);
        // Hosted proxies have no fixed addresses to list, so the hop count is the control.
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();
    });
}

var authRequestsPerMinute = builder.Configuration.GetValue("Security:AuthRequestsPerMinute", 20);

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, _) =>
    {
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
            context.HttpContext.Response.Headers.RetryAfter =
                ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);

        await Results.Problem(
                title: "Too many requests",
                detail: "Wait a minute, then try again.",
                statusCode: StatusCodes.Status429TooManyRequests)
            .ExecuteAsync(context.HttpContext);
    };

    // Guessing passwords across many accounts from one address. Lockout covers
    // guessing at any one account.
    options.AddPolicy(RateLimits.Auth, http => RateLimitPartition.GetSlidingWindowLimiter(
        ClientAddress(http),
        _ => new SlidingWindowRateLimiterOptions
        {
            PermitLimit = authRequestsPerMinute,
            Window = TimeSpan.FromMinutes(1),
            SegmentsPerWindow = 6,
            QueueLimit = 0,
        }));

    // The assistant may call a paid model on every question.
    options.AddPolicy(RateLimits.Assistant, http => RateLimitPartition.GetFixedWindowLimiter(
        Caller(http),
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 30,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        }));

    // A ceiling on everything else, so no one caller can monopolise the service.
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(http =>
        RateLimitPartition.GetFixedWindowLimiter(
            Caller(http),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 600,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            }));
});

// The OpenAPI document is generated from the endpoints themselves, so the names,
// summaries and tags each one carries are what Swagger shows. Only the covering
// information has to be written here.
builder.Services.AddOpenApi(options => options.AddDocumentTransformer((document, _, _) =>
{
    document.Info = new OpenApiInfo
    {
        Title = "DataMind AI API",
        Version = "v1",
        Description =
            "Every figure this API returns is computed from stored rows — the assistant plans a "
          + "QuerySpec and writes prose, but never does arithmetic.\n\n"
          + "Everything except health and the account endpoints needs a signed-in session, and an "
          + "account only ever sees its own data. Requests that change data must carry the "
          + "`X-DataMind-Csrf: 1` header. Dataset-scoped endpoints take an optional `datasetId`; leave "
          + "it off and the API uses the account's most recently updated file that holds rows. "
          + "Failures come back as ProblemDetails with a `detail` that says what to do next.",
    };

    return Task.CompletedTask;
}));

builder.Services.AddProblemDetails();

var trustedOrigins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>()
                     ?? ["http://localhost:5173", "http://localhost:5174", "http://127.0.0.1:5173"];

// The site that proxies /api to this API from another origin — the Vercel
// deployment, for instance. Empty unless configured.
var productionTrustedOrigins = builder.Configuration.GetSection("Security:TrustedOrigins").Get<string[]>() ?? [];

builder.Services.AddCors(options => options.AddPolicy(FrontendCors, policy => policy
    .WithOrigins(trustedOrigins)
    .AllowAnyHeader()
    .AllowAnyMethod()));

var app = builder.Build();

// First, so everything after it — HTTPS redirection, rate limits, logging — sees
// the client rather than the proxy.
if (trustForwardedHeaders)
    app.UseForwardedHeaders();

// Any unhandled failure comes back as ProblemDetails, so a client never has to
// parse an HTML error page.
app.UseExceptionHandler(errorApp => errorApp.Run(async context =>
{
    var feature = context.Features.Get<IExceptionHandlerFeature>();

    // A body the model binder cannot read is the caller's mistake, not the
    // server's. ASP.NET has already classified these, so its status is kept.
    if (feature?.Error is BadHttpRequestException badRequest)
    {
        app.Logger.LogInformation(badRequest, "Rejected a malformed request");

        var tooLarge = badRequest.StatusCode == StatusCodes.Status413PayloadTooLarge;
        await Results.Problem(
                title: tooLarge ? "That upload is too large" : "The request body could not be read",
                detail: tooLarge
                    ? $"Files up to {InputLimits.UploadBytes / (1024 * 1024)} MB can be uploaded."
                    : "The body is not valid JSON for this endpoint. Check the field names, the quoting, "
                      + "and that the text is UTF-8 encoded.",
                statusCode: badRequest.StatusCode)
            .ExecuteAsync(context);

        return;
    }

    app.Logger.LogError(feature?.Error, "Unhandled request failure");

    await Results.Problem(
            title: "The request could not be completed",
            detail: app.Environment.IsDevelopment()
                ? feature?.Error.Message
                : "Something went wrong handling this request.",
            statusCode: StatusCodes.Status500InternalServerError)
        .ExecuteAsync(context);
}));

app.UseSecurityHeaders(app.Environment.IsDevelopment());

if (!app.Environment.IsDevelopment())
{
    // Browsers are told to reach this host over HTTPS only. The session cookie
    // is Secure in production and would not be sent over plain HTTP anyway.
    app.UseHsts();
    app.UseHttpsRedirection();
}

// Swagger is development-only: it describes every route and lets anyone call
// them, which is a browsable API in development and an open door in production.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();

    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/openapi/v1.json", "DataMind AI API v1");
        options.RoutePrefix = "swagger";
        options.DocumentTitle = "DataMind AI API";
        options.DisplayRequestDuration();
        // "Try it out" sends the same header the app does, so requests that
        // change data are not refused by the cross-site check.
        options.UseRequestInterceptor("(request) => { request.headers['X-DataMind-Csrf'] = '1'; return request; }");
    });
}

// The built frontend, when it has been copied in beside the API. `dotnet
// publish` picks up frontend/dist automatically (see the csproj), so one
// artefact serves both from one origin — which the session cookie requires.
// With no wwwroot the API runs alone, and the frontend is served by Vite, or by
// a host such as Vercel that proxies /api here.
var spaIndex = Path.Combine(app.Environment.WebRootPath ?? "", "index.html");
var servesSpa = File.Exists(spaIndex);

if (servesSpa)
{
    // The app shell and its assets are public: the sign-in page is part of it.
    app.UseDefaultFiles();
    app.UseStaticFiles();
}

// Trusted origins, besides the one serving the request. In development the app
// arrives through a Vite dev or preview server on another local port, so
// loopback origins are trusted too. In production only the origins configured
// in Security:TrustedOrigins are: the site that proxies /api here.
app.UseCrossSiteRequestChecks(
    app.Environment.IsDevelopment() ? trustedOrigins : productionTrustedOrigins,
    trustLoopbackOrigins: app.Environment.IsDevelopment());
app.UseCors(FrontendCors);
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

var api = app.MapGroup("/api");
api.MapHealthEndpoint();
api.MapAuthEndpoints();

// Everything else needs a signed-in account.
var signedIn = api.MapGroup("").RequireAuthorization();
signedIn.MapDatasetEndpoints();
signedIn.MapExplorerEndpoints();
signedIn.MapInsightEndpoints();
signedIn.MapChatEndpoints();
signedIn.MapReportEndpoints();
signedIn.MapSettingsEndpoints();

if (servesSpa)
{
    // Client-side routes are served the app shell. An unmatched /api path is
    // still a missing endpoint, not a page: answering it with index.html would
    // hand a caller HTML where it asked for JSON.
    app.MapFallback(async context =>
    {
        if (context.Request.Path.StartsWithSegments("/api"))
        {
            await Results.Problem(
                    title: "No such endpoint",
                    detail: $"{context.Request.Method} {context.Request.Path} is not a route on this API.",
                    statusCode: StatusCodes.Status404NotFound)
                .ExecuteAsync(context);

            return;
        }

        context.Response.ContentType = "text/html";
        await context.Response.SendFileAsync(spaIndex);
    }).ExcludeFromDescription();
}
else
{
    // No frontend alongside: opening the API's root in a browser should still
    // land somewhere useful — the docs in development, the liveness check
    // anywhere else.
    app.MapGet("/", () => Results.Redirect(app.Environment.IsDevelopment() ? "/swagger" : "/api/health"))
        .ExcludeFromDescription();
}

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await Database.PrepareAsync(db, app.Logger);
}

app.Run();

// The connection's address, or the forwarded client's when forwarded headers
// are trusted (Security:TrustForwardedHeaders).
static string ClientAddress(HttpContext http) =>
    http.Connection.RemoteIpAddress?.ToString() ?? "unknown";

static string Caller(HttpContext http) =>
    http.User.FindFirstValue(ClaimTypes.NameIdentifier) is { } id ? $"account:{id}" : $"address:{ClientAddress(http)}";

/// <summary>Exposed so an integration test project can boot the same pipeline.</summary>
public partial class Program;
