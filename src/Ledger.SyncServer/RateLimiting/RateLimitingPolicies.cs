namespace Ledger.SyncServer.RateLimiting;

/// The one rate-limiting policy this server defines. A named constant
/// rather than a string literal shared between `Program.cs` (where the
/// policy is registered) and `EventsEndpoints.cs` (where it's applied) —
/// a typo in either place would otherwise silently apply no rate limit
/// at all instead of failing to compile.
public static class RateLimitingPolicies
{
    public const string PerApiKey = "per-api-key";
}
