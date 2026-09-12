namespace Ledger.SyncServer.RateLimiting;

/// Binds to the `RateLimiting` configuration section. Defaults are sane
/// production values; tests override both to small numbers so a test can
/// actually exceed the limit in a handful of requests instead of firing
/// hundreds of them to prove the same thing.
public sealed class RateLimitingSettings
{
    public const string SectionName = "RateLimiting";

    public int PermitLimit { get; set; } = 100;

    public int WindowSeconds { get; set; } = 60;
}
