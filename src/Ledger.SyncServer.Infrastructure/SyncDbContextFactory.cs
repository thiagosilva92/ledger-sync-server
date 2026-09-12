using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Ledger.SyncServer.Infrastructure;

/// Lets `dotnet ef migrations add` generate migrations from this class
/// library directly, without needing the API host project to exist yet
/// (it doesn't — that's the next checkpoint). The connection string here
/// is never used to actually connect; EF Core only needs it to pick the
/// Npgsql provider and build the model for diffing.
public sealed class SyncDbContextFactory : IDesignTimeDbContextFactory<SyncDbContext>
{
    public SyncDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<SyncDbContext>()
            .UseNpgsql("Host=localhost;Database=design_time_only")
            .Options;
        return new SyncDbContext(options);
    }
}
