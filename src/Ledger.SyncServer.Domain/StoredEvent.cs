namespace Ledger.SyncServer.Domain;

/// An <see cref="IncomingEvent"/> that has already been accepted onto the
/// log, with the sequence number this server assigned it on arrival.
///
/// This sequence is this server's own — never the client's local
/// <c>EventStore</c> sequence, and never derived from an HLC timestamp.
/// It exists purely so <c>GET /events?after={sequence}</c> has something
/// gapless and strictly increasing to page through, mirroring exactly why
/// the mobile client's own sync design (see <c>SyncService</c>'s doc
/// comment in the ledger repo) uses a sequence cursor instead of an HLC
/// one: two events can tie on wall-clock time, but never on the order
/// this server actually received them in.
public sealed record StoredEvent(long Sequence, string EventId, string Payload);
