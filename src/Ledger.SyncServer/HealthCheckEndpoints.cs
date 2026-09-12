using Microsoft.AspNetCore.Diagnostics.HealthChecks;

namespace Ledger.SyncServer;

/// The two endpoints an orchestrator or load balancer actually needs —
/// deliberately separate from the "ready" tag registered in
/// `Program.cs`'s `AddHealthChecks().AddDbContextCheck<SyncDbContext>`.
///
/// Liveness and readiness answer different questions, and conflating them
/// is a common mistake: liveness asks "should this process be restarted?"
/// (a hung/deadlocked process, not a slow dependency), readiness asks
/// "should traffic be sent here right now?" A database outage should
/// take this instance out of a load balancer's rotation — readiness — not
/// cause an orchestrator to kill and restart a perfectly healthy process
/// that can't do anything about the database being down.
///
/// Neither endpoint requires an API key: the caller here is
/// infrastructure (a load balancer's own health prober), not a device
/// syncing events, and it has no way to present one.
public static class HealthCheckEndpoints
{
    public static IEndpointRouteBuilder MapHealthCheckEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapHealthChecks("/health/live", new HealthCheckOptions
        {
            // No registered check carries no tag, so an empty predicate
            // set means literally nothing runs — this only proves the
            // process is up enough to answer HTTP requests at all.
            Predicate = _ => false,
        });

        app.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains("ready"),
        });

        return app;
    }
}
