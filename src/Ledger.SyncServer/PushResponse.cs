namespace Ledger.SyncServer;

/// Response body for `POST /events`.
public sealed record PushResponse(int InsertedCount);
