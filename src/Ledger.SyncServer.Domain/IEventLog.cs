namespace Ledger.SyncServer.Domain;

/// The port this server's two endpoints are built on: append new events,
/// read events after a cursor. Everything an implementation needs to
/// guarantee is behavioral — idempotent append, gapless increasing
/// sequence on read — not tied to any particular storage technology.
///
/// <see cref="Ledger.SyncServer.Infrastructure" /> has the only
/// implementation (Postgres via EF Core), the same interface/adapter
/// split the client repo uses for <c>EventStore</c>/<c>DriftEventStore</c>.
public interface IEventLog
{
    /// Appends the events in <paramref name="events"/> that aren't already
    /// on record (by <see cref="IncomingEvent.EventId"/>) and returns how
    /// many were actually new. Safe to call again with the same batch —
    /// a retried call that isn't sure the first one landed inserts nothing
    /// the second time.
    Task<int> AppendAsync(
        IReadOnlyList<IncomingEvent> events,
        CancellationToken cancellationToken = default);

    /// Returns up to <paramref name="limit"/> events with a sequence
    /// greater than <paramref name="afterSequence"/>, ordered by sequence.
    /// An empty result means there is nothing newer — not an error.
    Task<IReadOnlyList<StoredEvent>> ReadAsync(
        long afterSequence,
        int limit,
        CancellationToken cancellationToken = default);
}
