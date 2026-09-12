using Ledger.SyncServer.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ledger.SyncServer.Infrastructure;

/// <see cref="IEventLog"/> backed by Postgres via EF Core — the only
/// implementation this server has, the same relationship
/// <c>DriftEventStore</c> has to <c>EventStore</c> on the client.
///
/// Not safe against two concurrent <see cref="AppendAsync"/> calls that
/// both include the same brand-new event id: both would read "not known
/// yet" before either has inserted, and the second insert would fail the
/// unique index on <c>EventId</c> rather than silently deduplicating.
/// Documented rather than engineered around, the same call made for
/// <c>DriftDeviceIdentityStore</c> on the client — a genuine race, but not
/// one this server's actual call pattern (one push per request, handled
/// sequentially against one connection) can hit in practice.
public sealed class PostgresEventLog(SyncDbContext db) : IEventLog
{
    public async Task<int> AppendAsync(
        IReadOnlyList<IncomingEvent> events,
        CancellationToken cancellationToken = default)
    {
        if (events.Count == 0)
        {
            return 0;
        }

        var incomingIds = events.Select(e => e.EventId).ToList();
        var knownIds = await db.Events
            .Where(row => incomingIds.Contains(row.EventId))
            .Select(row => row.EventId)
            .ToListAsync(cancellationToken);

        var toInsert = EventDeduplication.SelectNew(new HashSet<string>(knownIds), events);
        if (toInsert.Count == 0)
        {
            return 0;
        }

        db.Events.AddRange(toInsert.Select(e => new EventRow
        {
            EventId = e.EventId,
            Payload = e.Payload,
        }));
        await db.SaveChangesAsync(cancellationToken);

        return toInsert.Count;
    }

    public async Task<IReadOnlyList<StoredEvent>> ReadAsync(
        long afterSequence,
        int limit,
        CancellationToken cancellationToken = default)
    {
        return await db.Events
            .Where(row => row.Sequence > afterSequence)
            .OrderBy(row => row.Sequence)
            .Take(limit)
            .Select(row => new StoredEvent(row.Sequence, row.EventId, row.Payload))
            .ToListAsync(cancellationToken);
    }
}
