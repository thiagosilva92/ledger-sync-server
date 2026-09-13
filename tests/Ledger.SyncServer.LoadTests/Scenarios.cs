using System.Net.Http.Json;
using NBomber.Contracts;
using NBomber.CSharp;

namespace Ledger.SyncServer.LoadTests;

internal static class Scenarios
{
    /// Push-then-pull, round-robining across all five demo devices — the
    /// realistic shape of what this server actually serves (see the main
    /// README's "What this server actually needs to do"). Meant to run
    /// against an elevated `RateLimiting__PermitLimit` (see
    /// docker-compose.yml and the README's "Load testing" section): the
    /// point here is measuring real push/pull latency and throughput
    /// through the YARP gateway across both replicas, not re-proving the
    /// rate limiter (that's <see cref="RateLimitEnforcement"/>'s job).
    public static ScenarioProps Throughput(HttpClient client, Uri baseUrl)
    {
        var deviceIndex = -1;

        return Scenario
            .Create(
                "throughput_push_pull",
                async context =>
                {
                    var key = DeviceKeys.Keys[
                        Interlocked.Increment(ref deviceIndex) % DeviceKeys.Keys.Count
                    ];

                    var pushStep = await Step.Run(
                        "push",
                        context,
                        async () =>
                        {
                            using var request = new HttpRequestMessage(
                                HttpMethod.Post,
                                new Uri(baseUrl, "/events")
                            )
                            {
                                Content = JsonContent.Create(EventPayloads.Batch(5)),
                            };
                            request.Headers.Add("X-Api-Key", key);
                            using var response = await client.SendAsync(request);
                            return response.IsSuccessStatusCode
                                ? Response.Ok(statusCode: ((int)response.StatusCode).ToString(System.Globalization.CultureInfo.InvariantCulture))
                                : Response.Fail(statusCode: ((int)response.StatusCode).ToString(System.Globalization.CultureInfo.InvariantCulture));
                        }
                    );

                    var pullStep = await Step.Run(
                        "pull",
                        context,
                        async () =>
                        {
                            using var request = new HttpRequestMessage(
                                HttpMethod.Get,
                                new Uri(baseUrl, "/events?after=0&limit=50")
                            );
                            request.Headers.Add("X-Api-Key", key);
                            using var response = await client.SendAsync(request);
                            return response.IsSuccessStatusCode
                                ? Response.Ok(statusCode: ((int)response.StatusCode).ToString(System.Globalization.CultureInfo.InvariantCulture))
                                : Response.Fail(statusCode: ((int)response.StatusCode).ToString(System.Globalization.CultureInfo.InvariantCulture));
                        }
                    );

                    return pushStep.IsError ? pushStep : pullStep;
                }
            )
            .WithoutWarmUp()
            .WithLoadSimulations(
                Simulation.RampingInject(
                    rate: 30,
                    interval: TimeSpan.FromSeconds(1),
                    during: TimeSpan.FromSeconds(10)
                ),
                Simulation.Inject(
                    rate: 30,
                    interval: TimeSpan.FromSeconds(1),
                    during: TimeSpan.FromSeconds(30)
                )
            );
    }

    /// One device, pushed well past its configured budget, run against
    /// the *default* `RateLimiting__PermitLimit` (100/60s — plain
    /// `docker compose up`, no override). This isn't measuring speed; it
    /// exists to answer one question under genuine concurrent HTTP load,
    /// not a hand-timed two-request unit test: does the fixed-window
    /// limiter partitioned by API key (see docs/adr/0004) actually hold
    /// at ~135/60s of realistic incoming traffic, or does concurrency
    /// let some requests slip past the count? Every response is reported
    /// as Ok (a 429 here is the server working correctly, not a load-test
    /// failure) but tagged with its real status code, so the final report
    /// shows the true 200-vs-429 split.
    public static ScenarioProps RateLimitEnforcement(HttpClient client, Uri baseUrl)
    {
        const string key = "demo-local-only-key";

        return Scenario
            .Create(
                "rate_limit_enforcement",
                async context =>
                {
                    using var request = new HttpRequestMessage(
                        HttpMethod.Get,
                        new Uri(baseUrl, "/events?after=0&limit=10")
                    );
                    request.Headers.Add("X-Api-Key", key);
                    using var response = await client.SendAsync(request);

                    // Both 200 and 429 are Ok: the point is measuring
                    // which one happens, not treating 429 as a failure.
                    return response.StatusCode is System.Net.HttpStatusCode.OK
                        or System.Net.HttpStatusCode.TooManyRequests
                        ? Response.Ok(statusCode: ((int)response.StatusCode).ToString(System.Globalization.CultureInfo.InvariantCulture))
                        : Response.Fail(statusCode: ((int)response.StatusCode).ToString(System.Globalization.CultureInfo.InvariantCulture));
                }
            )
            .WithoutWarmUp()
            .WithLoadSimulations(
                // ~135 requests over 60s against a 100/60s budget —
                // deliberately over, so the split is unambiguous.
                Simulation.Inject(
                    rate: 2,
                    interval: TimeSpan.FromSeconds(1),
                    during: TimeSpan.FromSeconds(60)
                ),
                Simulation.Inject(
                    rate: 3,
                    interval: TimeSpan.FromSeconds(1),
                    during: TimeSpan.FromSeconds(5)
                )
            );
    }
}
