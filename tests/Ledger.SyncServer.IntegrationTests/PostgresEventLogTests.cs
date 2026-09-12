using Ledger.SyncServer.Domain;
using Ledger.SyncServer.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace Ledger.SyncServer.IntegrationTests;

/// Exercises <see cref="PostgresEventLog"/> against a real PostgreSQL
/// instance — via Testcontainers, not a mock and not SQLite standing in
/// for Postgres. Same reasoning as `DriftEventStore`'s tests running
/// against real SQLite on the client: the actual query behavior (unique
/// constraints, ordering, pagination) is exactly what would differ from a
/// fake, so a fake wouldn't be testing the thing that matters.
///
/// Every test gets its own container (`IAsyncLifetime` runs per test
/// method, not once for the class) — a few seconds of overhead per test,
/// bought back as complete isolation: no test can see another test's data,
/// so there's no shared-database cleanup logic to get wrong.
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Design",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "Disposed via IAsyncLifetime.DisposeAsync — xUnit's async "
        + "fixture lifecycle, which this analyzer doesn't recognize as a "
        + "disposal contract.")]
public sealed class PostgresEventLogTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container =
        new PostgreSqlBuilder("postgres:18-alpine").Build();

    private SyncDbContext _db = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        var options = new DbContextOptionsBuilder<SyncDbContext>()
            .UseNpgsql(_container.GetConnectionString())
            .Options;
        _db = new SyncDbContext(options);

        // Applies the real, committed EF Core migration — not
        // EnsureCreatedAsync(), which bypasses migrations entirely and
        // would prove nothing about whether InitialCreate itself is
        // correct.
        await _db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _container.DisposeAsync();
    }

    [Fact]
    public async Task AppendAsync_assigns_increasing_sequence_numbers_in_submission_order()
    {
        var log = new PostgresEventLog(_db);

        var insertedCount = await log.AppendAsync(
        [
            new IncomingEvent("evt-1", "payload-1"),
            new IncomingEvent("evt-2", "payload-2"),
        ]);

        Assert.Equal(2, insertedCount);

        var stored = await log.ReadAsync(afterSequence: 0, limit: 10);
        Assert.Equal(["evt-1", "evt-2"], stored.Select(e => e.EventId));
        Assert.True(stored[0].Sequence < stored[1].Sequence);
    }

    [Fact]
    public async Task AppendAsync_is_idempotent_to_a_retried_batch()
    {
        var log = new PostgresEventLog(_db);
        IncomingEvent[] batch = [new IncomingEvent("evt-1", "payload")];

        var firstCallCount = await log.AppendAsync(batch);
        var retryCount = await log.AppendAsync(batch); // the caller isn't sure the first call landed

        Assert.Equal(1, firstCallCount);
        Assert.Equal(0, retryCount);
        Assert.Single(await log.ReadAsync(afterSequence: 0, limit: 10));
    }

    [Fact]
    public async Task AppendAsync_inserts_only_the_events_not_already_known_from_a_mixed_batch()
    {
        var log = new PostgresEventLog(_db);
        await log.AppendAsync([new IncomingEvent("evt-1", "payload-1")]);

        var secondCallCount = await log.AppendAsync(
        [
            new IncomingEvent("evt-1", "payload-1"), // already stored
            new IncomingEvent("evt-2", "payload-2"), // new
        ]);

        Assert.Equal(1, secondCallCount);
        var stored = await log.ReadAsync(afterSequence: 0, limit: 10);
        Assert.Equal(["evt-1", "evt-2"], stored.Select(e => e.EventId));
    }

    [Fact]
    public async Task ReadAsync_pages_results_after_a_cursor_respecting_the_limit()
    {
        var log = new PostgresEventLog(_db);
        await log.AppendAsync(Enumerable.Range(1, 5)
            .Select(i => new IncomingEvent($"evt-{i}", $"payload-{i}"))
            .ToList());

        var firstPage = await log.ReadAsync(afterSequence: 0, limit: 2);
        Assert.Equal(["evt-1", "evt-2"], firstPage.Select(e => e.EventId));

        var secondPage = await log.ReadAsync(afterSequence: firstPage[^1].Sequence, limit: 2);
        Assert.Equal(["evt-3", "evt-4"], secondPage.Select(e => e.EventId));
    }

    [Fact]
    public async Task ReadAsync_returns_an_empty_list_when_nothing_is_newer_than_the_cursor()
    {
        var log = new PostgresEventLog(_db);
        await log.AppendAsync([new IncomingEvent("evt-1", "payload")]);

        var page = await log.ReadAsync(afterSequence: 999, limit: 10);

        Assert.Empty(page);
    }

    [Fact]
    public async Task A_fresh_EventLog_instance_over_the_same_database_sees_previously_stored_events()
    {
        // The whole point of persisting to Postgres instead of holding
        // events in memory: this has to survive a real app restart, not
        // just outlive one PostgresEventLog object. Modeled here as a
        // second instance over the same DbContext/database — the same
        // pattern used for DriftDeviceIdentityStore's equivalent test on
        // the client.
        var firstInstance = new PostgresEventLog(_db);
        await firstInstance.AppendAsync([new IncomingEvent("evt-1", "payload")]);

        var secondInstance = new PostgresEventLog(_db);
        var stored = await secondInstance.ReadAsync(afterSequence: 0, limit: 10);

        Assert.Single(stored);
        Assert.Equal("evt-1", stored[0].EventId);
    }
}
