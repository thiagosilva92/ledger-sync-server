using System.Security.Cryptography;
using Microsoft.Extensions.Options;

namespace Ledger.SyncServer.Authentication;

public interface IApiKeyValidator
{
    bool IsValid(string? apiKey);
}

/// Checks a request's API key against every hash in
/// <see cref="ApiKeySettings"/>. Pure enough to unit test without any
/// ASP.NET Core pipeline in sight — the only dependency is the
/// configuration it was handed.
public sealed class ApiKeyValidator(IOptions<ApiKeySettings> settings) : IApiKeyValidator
{
    public bool IsValid(string? apiKey)
    {
        if (string.IsNullOrEmpty(apiKey))
        {
            return false;
        }

        var candidateHash = Convert.FromHexString(ApiKeyHasher.Hash(apiKey));

        // Constant-time comparison, and checked against *every* configured
        // hash rather than stopping at the first mismatch with an
        // early-exit string comparison — a key is either valid or it
        // isn't; nothing about how long that decision took should leak
        // which configured hash it came closest to.
        var isValid = false;
        foreach (var knownHash in settings.Value.Hashes)
        {
            isValid |= CryptographicOperations.FixedTimeEquals(
                candidateHash,
                Convert.FromHexString(knownHash));
        }

        return isValid;
    }
}
