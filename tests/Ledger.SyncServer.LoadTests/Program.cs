using Ledger.SyncServer.LoadTests;
using NBomber.CSharp;

var scenarioName = args.Length > 0 ? args[0] : null;
if (scenarioName is not ("throughput" or "ratelimit"))
{
    Console.WriteLine(
        """
        Usage: dotnet run -- <throughput|ratelimit>

        Requires ledger-sync-server's docker-compose stack already
        running (`docker compose up -d` from the repo root) — see the
        main README's "Load testing" section for exactly how to run
        each scenario, including the environment variable override
        'throughput' needs.
        """
    );
    return 1;
}

DeviceKeys.VerifyAgainst("docker-compose.yml");

var baseUrl = new Uri(
    Environment.GetEnvironmentVariable("LOADTEST_BASE_URL") ?? "http://localhost:8080"
);
using var client = new HttpClient();

// scenarioName is already checked above to be one of these two.
var scenario =
    scenarioName == "throughput"
        ? Scenarios.Throughput(client, baseUrl)
        : Scenarios.RateLimitEnforcement(client, baseUrl);

NBomberRunner.RegisterScenarios(scenario).Run();

return 0;
