using System.Text.Json;

namespace Ledger.SyncServer;

/// Response body for `GET /events` — mirrors the client's own `PulledBatch`
/// shape (`events`, `remoteSequence`) exactly, since this is precisely
/// what a `SyncTransport.pull()` implementation hands back.
public sealed record PullResponse(IReadOnlyList<JsonElement> Events, long RemoteSequence);
