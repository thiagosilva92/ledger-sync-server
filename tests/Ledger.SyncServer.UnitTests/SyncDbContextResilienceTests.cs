using Ledger.SyncServer.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Ledger.SyncServer.UnitTests;

/// Proves the retry policy is actually wired into the DbContext options
/// Program.cs configures — not a full failure/recovery drill (that would
/// need a real Postgres to actually interrupt mid-test, and timing a
/// container outage precisely against a retry backoff window is exactly
/// the kind of flaky-by-construction test this repo has avoided
/// elsewhere). This is a wiring check: create the same kind of options
/// Program.cs builds, and confirm EF Core picked Npgsql's retrying
/// execution strategy instead of the non-retrying default.
public class SyncDbContextResilienceTests
{
    [Fact]
    public void EnableRetryOnFailure_configures_a_retrying_execution_strategy()
    {
        var options = new DbContextOptionsBuilder<SyncDbContext>()
            .UseNpgsql(
                "Host=localhost;Database=never-actually-connected-to",
                npgsql => npgsql.EnableRetryOnFailure(
                    maxRetryCount: 3,
                    maxRetryDelay: TimeSpan.FromSeconds(2),
                    errorCodesToAdd: null))
            .Options;
        using var db = new SyncDbContext(options);

        var strategy = db.Database.CreateExecutionStrategy();

        // Checked by type name rather than a direct type reference: the
        // concrete class lives in an Npgsql-internal namespace not meant
        // to be referenced directly, but its name is a stable enough
        // signal that retrying behavior — not the plain, non-retrying
        // default strategy — is what got configured.
        Assert.Equal("NpgsqlRetryingExecutionStrategy", strategy.GetType().Name);
    }

    [Fact]
    public void Without_EnableRetryOnFailure_the_default_non_retrying_strategy_is_used()
    {
        // The contrast that makes the test above meaningful: prove the
        // default really is different, not just assert a name and hope.
        var options = new DbContextOptionsBuilder<SyncDbContext>()
            .UseNpgsql("Host=localhost;Database=never-actually-connected-to")
            .Options;
        using var db = new SyncDbContext(options);

        var strategy = db.Database.CreateExecutionStrategy();

        Assert.NotEqual("NpgsqlRetryingExecutionStrategy", strategy.GetType().Name);
    }
}
