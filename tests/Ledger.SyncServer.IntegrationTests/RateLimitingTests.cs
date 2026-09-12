using System.Net;
using Ledger.SyncServer;
using Ledger.SyncServer.Authentication;
using Ledger.SyncServer.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace Ledger.SyncServer.IntegrationTests;

/// Configures a deliberately tiny limit (2 requests / 10 seconds) via the
/// same configuration overlay every other test uses for the connection
/// string and API keys — proportional testing: proving a 100-requests-a-
/// minute production limit gets enforced doesn't require firing 101 real
/// requests, only that *some* configured limit does.
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Design",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "Disposed via IAsyncLifetime.DisposeAsync — xUnit's async "
        + "fixture lifecycle, which this analyzer doesn't recognize as a "
        + "disposal contract.")]
public sealed class RateLimitingTests : IAsyncLifetime
{
    private const string PrimaryApiKey = "rate-limit-primary-key";
    private const string SecondaryApiKey = "rate-limit-secondary-key";

    private readonly PostgreSqlContainer _container =
        new PostgreSqlBuilder("postgres:18-alpine").Build();

    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:SyncDatabase"] = _container.GetConnectionString(),
                    ["ApiKeys:Hashes:0"] = ApiKeyHasher.Hash(PrimaryApiKey),
                    ["ApiKeys:Hashes:1"] = ApiKeyHasher.Hash(SecondaryApiKey),
                    ["RateLimiting:PermitLimit"] = "2",
                    ["RateLimiting:WindowSeconds"] = "10",
                });
            });
        });

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SyncDbContext>();
            await db.Database.MigrateAsync();
        }

        _client = _factory.CreateClient();
        _client.DefaultRequestHeaders.Add(ApiKeyAuthenticationOptions.HeaderName, PrimaryApiKey);
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        await _container.DisposeAsync();
    }

    [Fact]
    public async Task Requests_within_the_configured_limit_all_succeed()
    {
        var first = await _client.GetAsync("/events?after=0");
        var second = await _client.GetAsync("/events?after=0");

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
    }

    [Fact]
    public async Task A_request_beyond_the_limit_is_rejected_with_429()
    {
        await _client.GetAsync("/events?after=0"); // 1st, within limit
        await _client.GetAsync("/events?after=0"); // 2nd, within limit

        var third = await _client.GetAsync("/events?after=0"); // exceeds the 2-request limit

        Assert.Equal(HttpStatusCode.TooManyRequests, third.StatusCode);
    }

    [Fact]
    public async Task Different_devices_have_independent_limits()
    {
        // Exhaust the primary key's limit.
        await _client.GetAsync("/events?after=0");
        await _client.GetAsync("/events?after=0");
        var primaryExhausted = await _client.GetAsync("/events?after=0");
        Assert.Equal(HttpStatusCode.TooManyRequests, primaryExhausted.StatusCode);

        // A different, still-valid device key has its own untouched budget
        // — proving the partition is per-key, not one limit shared by
        // every authenticated caller.
        using var secondDeviceClient = _factory.CreateClient();
        secondDeviceClient.DefaultRequestHeaders.Add(
            ApiKeyAuthenticationOptions.HeaderName,
            SecondaryApiKey);

        var response = await secondDeviceClient.GetAsync("/events?after=0");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
