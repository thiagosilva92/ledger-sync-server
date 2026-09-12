using Microsoft.EntityFrameworkCore;

namespace Ledger.SyncServer.Infrastructure;

/// The server's one table: every event ever pushed, in the order this
/// server received it. Infrastructure only — no domain knowledge here
/// beyond <see cref="EventRow"/>'s own shape, the same boundary
/// <c>AppDatabase</c> keeps on the client side.
public sealed class SyncDbContext(DbContextOptions<SyncDbContext> options)
    : DbContext(options)
{
    public DbSet<EventRow> Events => Set<EventRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<EventRow>(entity =>
        {
            entity.ToTable("events");
            entity.HasKey(row => row.Sequence);
            entity.Property(row => row.Sequence).ValueGeneratedOnAdd();
            entity.Property(row => row.EventId).IsRequired();
            entity.Property(row => row.Payload).IsRequired();

            // The one invariant the database itself enforces, not just
            // application code: no event id can ever be stored twice.
            // PostgresEventLog already filters duplicates before
            // inserting, but a unique index is what makes "duplicate
            // event id" structurally impossible rather than merely
            // avoided by the current code path.
            entity.HasIndex(row => row.EventId).IsUnique();
        });
    }
}
