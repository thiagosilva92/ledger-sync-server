using System.Text.Json;
using Ledger.SyncServer.Domain;
using Ledger.SyncServer.RateLimiting;

namespace Ledger.SyncServer;

/// The two endpoints this entire server exists to provide — the server
/// side of `SyncTransport`. Extracted out of `Program.cs` into its own
/// extension method, the way current Minimal API guidance recommends
/// once there's more than a line or two of routing: `Program.cs` stays a
/// short list of "what's wired up", not a growing pile of route handlers.
public static class EventsEndpoints
{
    /// `GET /events` returns at most this many events per call when the
    /// caller doesn't specify `limit` — matches `SyncTransport.pull`'s own
    /// default on the client.
    private const int DefaultLimit = 200;

    /// Hard ceiling on `limit`, regardless of what a caller asks for — a
    /// client-supplied limit is untrusted input; without a ceiling, a
    /// single call could ask this server to materialize an unbounded
    /// number of rows.
    private const int MaxLimit = 1000;

    public static IEndpointRouteBuilder MapEventsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/events", PushAsync)
            .RequireAuthorization()
            .RequireRateLimiting(RateLimitingPolicies.PerApiKey)
            .WithName("PushEvents")
            .WithSummary("Push a batch of events, de-duplicated by eventId.")
            .WithDescription(
                "Idempotent to a retried call: an event id already on "
                    + "record is silently skipped, not duplicated or "
                    + "rejected. Each element is stored and later returned "
                    + "verbatim — this server only reads its \"eventId\" "
                    + "field, nothing else about its shape is validated.")
            .WithTags("Events");
        app.MapGet("/events", PullAsync)
            .RequireAuthorization()
            .RequireRateLimiting(RateLimitingPolicies.PerApiKey)
            .WithName("PullEvents")
            .WithSummary("Pull events after a sequence cursor, paginated.")
            .WithDescription(
                "\"after\" is this server's own sequence number, opaque to "
                    + "the caller — always the \"remoteSequence\" from a "
                    + "previous call's response, starting from 0. An empty "
                    + "\"events\" array means there's nothing newer, not an "
                    + "error.")
            .WithTags("Events");
        return app;
    }

    /// Accepts events as raw JSON elements, not a typed DTO — this server
    /// never needs more than one field (`eventId`) out of what's sent; the
    /// rest is stored verbatim as an opaque payload and handed back
    /// unchanged on a pull. See the README for why that's a deliberate
    /// boundary, not a missing model.
    private static async Task<IResult> PushAsync(
        List<JsonElement> events,
        IEventLog eventLog,
        CancellationToken cancellationToken)
    {
        var incoming = new List<IncomingEvent>(events.Count);
        foreach (var element in events)
        {
            if (!element.TryGetProperty("eventId", out var idProperty)
                || idProperty.GetString() is not { Length: > 0 } eventId)
            {
                return Results.BadRequest("every event must have a non-empty \"eventId\"");
            }

            incoming.Add(new IncomingEvent(eventId, element.GetRawText()));
        }

        var insertedCount = await eventLog.AppendAsync(incoming, cancellationToken);
        return Results.Ok(new PushResponse(insertedCount));
    }

    private static async Task<IResult> PullAsync(
        long after,
        int? limit,
        IEventLog eventLog,
        CancellationToken cancellationToken)
    {
        var effectiveLimit = limit is > 0 and <= MaxLimit ? limit.Value : DefaultLimit;
        var stored = await eventLog.ReadAsync(after, effectiveLimit, cancellationToken);

        // The remote cursor a caller resumes from next time: the last
        // event's own sequence if this page returned anything, otherwise
        // unchanged — mirrors PulledBatch's contract on the client
        // (nothing new means "here's the same cursor back", not an error).
        var remoteSequence = stored.Count > 0 ? stored[^1].Sequence : after;
        var events = stored.Select(e => ParseStoredPayload(e.Payload)).ToList();

        return Results.Ok(new PullResponse(events, remoteSequence));
    }

    /// Parses a stored payload back into a <see cref="JsonElement"/> that
    /// outlives the <see cref="JsonDocument"/> it came from —
    /// <see cref="JsonElement.Clone"/> is what makes that safe once
    /// <paramref name="payload"/>'s backing document is disposed.
    private static JsonElement ParseStoredPayload(string payload)
    {
        using var document = JsonDocument.Parse(payload);
        return document.RootElement.Clone();
    }
}
