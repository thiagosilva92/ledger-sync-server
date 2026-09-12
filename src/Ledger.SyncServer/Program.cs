using Ledger.SyncServer;
using Ledger.SyncServer.Authentication;
using Ledger.SyncServer.Domain;
using Ledger.SyncServer.Infrastructure;
using Microsoft.EntityFrameworkCore;

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

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.MapEventsEndpoints();

app.Run();

// Exposes the otherwise-implicit top-level Program class so
// WebApplicationFactory<Program> can target it from the test project.
public partial class Program;
