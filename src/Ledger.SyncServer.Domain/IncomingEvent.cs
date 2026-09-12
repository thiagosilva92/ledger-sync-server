namespace Ledger.SyncServer.Domain;

/// One event as it arrives on a push, or leaves on a pull: an opaque
/// payload keyed by the id the client assigned it.
///
/// The server never inspects <see cref="Payload"/> — it doesn't know what
/// an "account" or a "transaction" is, and doesn't need to. Everything
/// this server does (deduplicate by <see cref="EventId"/>, hand events
/// back in the order it received them) works the same regardless of what
/// domain the client on the other end is built around. That's deliberate:
/// it's what keeps this repository decoupled from the mobile app's event
/// schema, the same way <c>DriftEventStore</c> on the client side never
/// needs to know what a "leg" or a "transaction" is either.
public sealed record IncomingEvent(string EventId, string Payload);
