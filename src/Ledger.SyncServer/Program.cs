using System.Security.Claims;
using System.Threading.RateLimiting;
using Ledger.SyncServer;
using Ledger.SyncServer.Authentication;
using Ledger.SyncServer.Domain;
using Ledger.SyncServer.Infrastructure;
using Ledger.SyncServer.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Enrichers.Span;
using Serilog.Formatting.Compact;

var builder = WebApplication.CreateBuilder(args);

// Structured JSON to stdout — the shape a real deployment's log
// collector actually wants, not the human-formatted text ASP.NET Core
// defaults to. Enrich.WithSpan() is what makes each log line carry the
// TraceId/SpanId of whatever request it happened during, so a log line
// and the distributed trace it came from can be pivoted between —
// without it, logs and traces are two disconnected systems that happen
// to describe the same request.
builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .Enrich.FromLogContext()
    .Enrich.WithSpan()
    .WriteTo.Console(new RenderedCompactJsonFormatter()));

// Tracing instrumentation is always on (near-zero cost when nothing's
// listening) — only the OTLP *export* is conditional on
// Observability:OtlpEndpoint being configured. Without this split, every
// plain `dotnet test`/`dotnet run` with no collector nearby would spend
// its life quietly retrying a connection to nobody.
var otlpEndpoint = builder.Configuration["Observability:OtlpEndpoint"];
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("ledger-sync-server"))
    .WithTracing(tracing =>
    {
        tracing
            .AddAspNetCoreInstrumentation()
            // Npgsql has emitted its own ActivitySource-based traces
            // since v6 — this just has to be told to listen, not
            // reimplemented. One line gets every SQL command this
            // server issues into the same trace as the HTTP request
            // that caused it.
            .AddSource("Npgsql");

        if (!string.IsNullOrEmpty(otlpEndpoint))
        {
            tracing.AddOtlpExporter(otlp => otlp.Endpoint = new Uri(otlpEndpoint));
        }
    });

builder.Services.Configure<ApiKeySettings>(
    builder.Configuration.GetSection(ApiKeySettings.SectionName));
builder.Services.AddSingleton<IApiKeyValidator, ApiKeyValidator>();
builder.Services
    .AddAuthentication(ApiKeyAuthenticationOptions.DefaultScheme)
    .AddScheme<ApiKeyAuthenticationOptions, ApiKeyAuthenticationHandler>(
        ApiKeyAuthenticationOptions.DefaultScheme,
        _ => { });
builder.Services.AddAuthorization();

// The connection string is read lazily, inside this callback, rather than
// straight off `builder.Configuration` up front — this runs when the DI
// container first resolves DbContextOptions, by which point every
// configuration source is in place, including WebApplicationFactory's
// test overlay (added during Build(), after CreateBuilder() returns).
// Reading it eagerly here would see the pre-Build() configuration and
// throw in every test that never gets the chance to override it.
builder.Services.AddDbContext<SyncDbContext>(options =>
{
    var connectionString = builder.Configuration.GetConnectionString("SyncDatabase")
        ?? throw new InvalidOperationException(
            "Missing required configuration \"ConnectionStrings:SyncDatabase\".");
    options.UseNpgsql(connectionString);
});
builder.Services.AddScoped<IEventLog, PostgresEventLog>();

builder.Services.AddHealthChecks()
    .AddDbContextCheck<SyncDbContext>(name: "postgres", tags: ["ready"]);

builder.Services.Configure<RateLimitingSettings>(
    builder.Configuration.GetSection(RateLimitingSettings.SectionName));
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(RateLimitingPolicies.PerApiKey, context =>
    {
        var settings = context.RequestServices
            .GetRequiredService<IOptions<RateLimitingSettings>>().Value;
        // Partitioned by the authenticated device's key hash (set as a
        // claim by ApiKeyAuthenticationHandler), not by IP — several
        // devices behind the same NAT/carrier IP shouldn't share one
        // budget, and a single device switching networks shouldn't reset
        // one either.
        var partitionKey = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? "unauthenticated";
        return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = settings.PermitLimit,
            Window = TimeSpan.FromSeconds(settings.WindowSeconds),
            QueueLimit = 0,
        });
    });
});

var app = builder.Build();

// Migrations run as a separate, one-shot step (the docker-compose
// "migrator" service passes this flag, then exits), never as a side
// effect of a normal replica starting up. With more than one replica —
// the whole point of the horizontal-scaling setup this ships with — two
// processes both calling Database.MigrateAsync() against a fresh
// database at the same time is a real race, not a hypothetical one:
// EF Core's migration history table gives no cross-process locking
// guarantee against two migrators applying the same migration
// concurrently. One designated migration step before any replica starts
// serving traffic sidesteps the race entirely instead of hoping it
// doesn't happen.
if (args.Contains("--migrate-only"))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<SyncDbContext>();
    await db.Database.MigrateAsync();
    return;
}

// One structured log line per request (method, path, status, elapsed) —
// replaces the multi-line default ASP.NET Core request logging, and
// (via Enrich.WithSpan() above) carries the same TraceId Jaeger shows
// for that request.
app.UseSerilogRequestLogging();

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapEventsEndpoints();
app.MapHealthCheckEndpoints();

app.Run();

// Exposes the otherwise-implicit top-level Program class so
// WebApplicationFactory<Program> can target it from the test project.
public partial class Program;
