using System.Security.Cryptography;
using System.Text;

namespace Ledger.SyncServer.Authentication;

/// The one place the hash function lives — used both by whoever issues a
/// new key (hashing it before it goes into configuration) and by
/// <see cref="ApiKeyValidator"/> (hashing an incoming request's key to
/// compare). A single shared implementation means the two sides can never
/// quietly drift onto different hash functions, which would silently
/// reject every previously-valid key.
public static class ApiKeyHasher
{
    /// Lowercase hex SHA-256 of <paramref name="apiKey"/>. Never store the
    /// key itself — only this, the same reason a password is never stored
    /// in plaintext.
    public static string Hash(string apiKey)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(apiKey));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
