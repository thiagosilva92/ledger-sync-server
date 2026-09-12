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

var builder = WebApplication.CreateBuilder(args);

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

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapEventsEndpoints();
app.MapHealthCheckEndpoints();

app.Run();

// Exposes the otherwise-implicit top-level Program class so
// WebApplicationFactory<Program> can target it from the test project.
public partial class Program;
