using Microsoft.AspNetCore.Authentication;

namespace Ledger.SyncServer.Authentication;

public sealed class ApiKeyAuthenticationOptions : AuthenticationSchemeOptions
{
    public const string DefaultScheme = "ApiKey";

    public const string HeaderName = "X-Api-Key";
}
