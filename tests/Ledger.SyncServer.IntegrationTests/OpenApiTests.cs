using System.Net;
using System.Text.Json;
using Ledger.SyncServer;
using Ledger.SyncServer.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace Ledger.SyncServer.IntegrationTests;

/// The OpenAPI document is public — same reasoning as the health
/// endpoints, the caller here is a developer or a tool, not a device with
/// a key — so this deliberately never sets an `X-Api-Key` header.
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Design",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "Disposed via IAsyncLifetime.DisposeAsync — xUnit's async "
        + "fixture lifecycle, which this analyzer doesn't recognize as a "
        + "disposal contract.")]
public sealed class OpenApiTests : IAsyncLifetime
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

        _client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        await _container.DisposeAsync();
    }

    [Fact]
    public async Task The_OpenApi_document_is_served_without_an_api_key_as_valid_json()
    {
        var response = await _client.GetAsync("/openapi/v1.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(content); // throws on invalid JSON
        Assert.True(document.RootElement.TryGetProperty("openapi", out _));
    }

    [Fact]
    public async Task The_OpenApi_document_describes_both_events_endpoints()
    {
        var response = await _client.GetAsync("/openapi/v1.json");
        var content = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(content);

        var paths = document.RootElement.GetProperty("paths");
        var eventsPath = paths.GetProperty("/events");

        Assert.True(eventsPath.TryGetProperty("get", out _));
        Assert.True(eventsPath.TryGetProperty("post", out _));
    }

    [Fact]
    public async Task The_Scalar_UI_page_is_served_without_an_api_key()
    {
        var response = await _client.GetAsync("/scalar/v1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
