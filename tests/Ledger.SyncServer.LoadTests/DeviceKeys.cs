using Ledger.SyncServer.Authentication;

namespace Ledger.SyncServer.LoadTests;

/// The plaintext keys behind `docker-compose.yml`'s `ApiKeys__Hashes__0..4`
/// — five fixed demo devices, matching the fact that the compose file
/// bakes in five committed demo hashes (fine for local-only demo keys, the
/// same reasoning `ApiKeys__Hashes__0`'s own comment gives). Reuses
/// <see cref="ApiKeyHasher"/> — the server's real hashing — as a
/// build-time cross-check: if these plaintext keys ever stop hashing to
/// what `docker-compose.yml` has hard-coded, <see cref="VerifyAgainst"/>
/// throws immediately rather than every request in the load test silently
/// getting 401s.
internal static class DeviceKeys
{
    public static readonly IReadOnlyList<string> Keys =
    [
        "demo-local-only-key",
        "loadtest-device-1-key",
        "loadtest-device-2-key",
        "loadtest-device-3-key",
        "loadtest-device-4-key",
    ];

    private static readonly IReadOnlyList<string> ExpectedHashes =
    [
        "59b69b7c26135a56dda94467422ea4910356e0271b520f85f2c77ede5e55f0dc",
        "b71f9be61299cb8c723c65bb105c645346f1253fec66cac03373ab3261ffc3c7",
        "e95298781ad2286160aa2e37881469017e740dc1c32a09b36c2eb92ac7d1a504",
        "94c0c01f8b12add1a2fded7d377c94d6ed1886cd2147caf86639be43419d9e9d",
        "43a0b82c3caf1fe5a865fb5292d127b19959deadc7d1c39ea3be8ec64f67044c",
    ];

    /// Throws if any key's hash doesn't match `docker-compose.yml`'s
    /// hard-coded `ApiKeys__Hashes__N` — a load test that silently
    /// measures "how fast does the server reject requests" instead of
    /// "how fast does the server serve requests" would be worse than no
    /// load test at all.
    public static void VerifyAgainst(string composeFilePath)
    {
        for (var i = 0; i < Keys.Count; i++)
        {
            var actual = ApiKeyHasher.Hash(Keys[i]);
            if (actual != ExpectedHashes[i])
            {
                throw new InvalidOperationException(
                    $"Device key #{i} hashes to {actual}, but {composeFilePath} "
                        + $"has {ExpectedHashes[i]} baked in for ApiKeys__Hashes__{i}. "
                        + "Update whichever one is stale before running the load test.");
            }
        }
    }
}
