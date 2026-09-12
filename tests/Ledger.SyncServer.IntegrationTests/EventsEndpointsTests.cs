using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
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

/// End-to-end tests for the two endpoints — real HTTP, through the actual
/// ASP.NET Core pipeline (`WebApplicationFactory<Program>`), against a
/// real Postgres (Testcontainers), migrated with the same committed
/// migration production uses. This is the one place in the test suite
/// that proves the wire contract itself: request/response JSON shapes,
/// status codes, routing — everything `PostgresEventLogTests` can't see
/// because it calls `PostgresEventLog` directly, skipping HTTP entirely.
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Design",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "Disposed via IAsyncLifetime.DisposeAsync — xUnit's async "
        + "fixture lifecycle, which this analyzer doesn't recognize as a "
        + "disposal contract.")]
public sealed class EventsEndpointsTests : IAsyncLifetime
{
    /// The plaintext key every test in this class authenticates with —
    /// only its hash (see <c>InitializeAsync</c>) is ever configured on
    /// the server, the same as a real deployment would only ever be
    /// handed a hash.
    private const string TestApiKey = "test-device-key";

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
                    ["ApiKeys:Hashes:0"] = ApiKeyHasher.Hash(TestApiKey),
                });
            });
        });

        // Applies the real, committed migration through the host's own
        // service provider — proves the host is wired to a database that
        // actually works, not just that it starts.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SyncDbContext>();
            await db.Database.MigrateAsync();
        }

        _client = _factory.CreateClient();
        _client.DefaultRequestHeaders.Add(ApiKeyAuthenticationOptions.HeaderName, TestApiKey);
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        await _container.DisposeAsync();
    }

    [Fact]
    public async Task Pushing_an_event_then_pulling_returns_it_unchanged()
    {
        var pushed = new
        {
            eventId = "evt-1",
            aggregateId = "acc-1",
            eventType = "test.thing",
            timestamp = "1000-00000-node",
            payload = new { label = "groceries" },
        };

        var pushResponse = await _client.PostAsJsonAsync("/events", new[] { pushed });
        pushResponse.EnsureSuccessStatusCode();
        var pushResult = await pushResponse.Content.ReadFromJsonAsync<PushResponse>();
        Assert.Equal(1, pushResult?.InsertedCount);

        var pullResult = await _client.GetFromJsonAsync<PullResponse>("/events?after=0");

        Assert.NotNull(pullResult);
        var singleEvent = Assert.Single(pullResult.Events);
        Assert.Equal("evt-1", singleEvent.GetProperty("eventId").GetString());
        Assert.Equal("acc-1", singleEvent.GetProperty("aggregateId").GetString());
        Assert.Equal("groceries", singleEvent.GetProperty("payload").GetProperty("label").GetString());
        Assert.True(pullResult.RemoteSequence > 0);
    }

    [Fact]
    public async Task Pushing_the_same_batch_twice_is_a_no_op_the_second_time()
    {
        var pushed = new[] { new { eventId = "evt-1", payload = "x" } };

        var first = await _client.PostAsJsonAsync("/events", pushed);
        var second = await _client.PostAsJsonAsync("/events", pushed);

        var firstResult = await first.Content.ReadFromJsonAsync<PushResponse>();
        var secondResult = await second.Content.ReadFromJsonAsync<PushResponse>();

        Assert.Equal(1, firstResult?.InsertedCount);
        Assert.Equal(0, secondResult?.InsertedCount);
    }

    [Fact]
    public async Task Pushing_an_event_without_an_eventId_is_rejected()
    {
        var invalid = new[] { new { aggregateId = "acc-1" } }; // no eventId

        var response = await _client.PostAsJsonAsync("/events", invalid);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Pulling_with_nothing_stored_returns_an_empty_page_and_the_same_cursor()
    {
        var result = await _client.GetFromJsonAsync<PullResponse>("/events?after=0");

        Assert.NotNull(result);
        Assert.Empty(result.Events);
        Assert.Equal(0, result.RemoteSequence);
    }

    [Fact]
    public async Task Pulling_respects_the_limit_query_parameter()
    {
        var pushed = Enumerable.Range(1, 5)
            .Select(i => new { eventId = $"evt-{i}", payload = i })
            .ToArray();
        await _client.PostAsJsonAsync("/events", pushed);

        var firstPage = await _client.GetFromJsonAsync<PullResponse>("/events?after=0&limit=2");

        Assert.NotNull(firstPage);
        Assert.Equal(2, firstPage.Events.Count);

        var secondPage = await _client.GetFromJsonAsync<PullResponse>(
            $"/events?after={firstPage.RemoteSequence}&limit=2");

        Assert.NotNull(secondPage);
        Assert.Equal(2, secondPage.Events.Count);
        Assert.NotEqual(firstPage.Events[0].GetRawText(), secondPage.Events[0].GetRawText());
    }

    [Fact]
    public async Task A_request_with_no_api_key_is_rejected_with_401()
    {
        using var anonymousClient = _factory.CreateClient();

        var response = await anonymousClient.GetAsync("/events?after=0");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_request_with_a_wrong_api_key_is_rejected_with_401()
    {
        using var wrongKeyClient = _factory.CreateClient();
        wrongKeyClient.DefaultRequestHeaders.Add(ApiKeyAuthenticationOptions.HeaderName, "wrong-key");

        var response = await wrongKeyClient.GetAsync("/events?after=0");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
