using System.Net;
using Ledger.SyncServer;
using Ledger.SyncServer.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace Ledger.SyncServer.IntegrationTests;

/// Proves the two health endpoints actually answer the different
/// questions they're meant to — not just that they exist and return 200.
/// The one test that matters most here deliberately stops the real
/// Postgres container mid-test: readiness has to notice, the way an
/// actual outage would need it to.
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Design",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "Disposed via IAsyncLifetime.DisposeAsync — xUnit's async "
        + "fixture lifecycle, which this analyzer doesn't recognize as a "
        + "disposal contract.")]
public sealed class HealthCheckEndpointsTests : IAsyncLifetime
{
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
                });
            });
        });

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SyncDbContext>();
            await db.Database.MigrateAsync();
        }

        // Deliberately no X-Api-Key header: health endpoints are for
        // infrastructure probes, which have no key to present.
        _client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        await _container.DisposeAsync();
    }

    [Fact]
    public async Task Health_live_succeeds_without_an_api_key()
    {
        var response = await _client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Health_ready_succeeds_when_postgres_is_reachable()
    {
        var response = await _client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Health_live_stays_healthy_even_when_postgres_is_unreachable()
    {
        // The whole point of splitting live from ready: a database outage
        // is not a reason to restart this process — it can't fix the
        // database by restarting, and doing so would just drop whatever
        // requests were in flight for no benefit.
        await _container.StopAsync();

        var response = await _client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Health_ready_reports_unhealthy_when_postgres_is_unreachable()
    {
        await _container.StopAsync();

        var response = await _client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }
}
