using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Ledger.SyncServer.Authentication;

/// A minimal <see cref="AuthenticationHandler{TOptions}"/> for one scheme:
/// present a valid key in the <see cref="ApiKeyAuthenticationOptions.HeaderName"/>
/// header, or the request never reaches an endpoint marked
/// <c>RequireAuthorization()</c>. No roles, no claims beyond "this request
/// presented *a* valid key" — every device trusted with a key can push
/// and pull equally, which matches what this server actually needs: it
/// isn't multi-tenant, so there's nothing for a role to distinguish yet.
public sealed class ApiKeyAuthenticationHandler(
    IOptionsMonitor<ApiKeyAuthenticationOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IApiKeyValidator validator)
    : AuthenticationHandler<ApiKeyAuthenticationOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(ApiKeyAuthenticationOptions.HeaderName, out var headerValues))
        {
            return Task.FromResult(AuthenticateResult.Fail(
                $"Missing \"{ApiKeyAuthenticationOptions.HeaderName}\" header."));
        }

        var apiKey = headerValues.ToString();
        if (!validator.IsValid(apiKey))
        {
            return Task.FromResult(AuthenticateResult.Fail("Invalid API key."));
        }

        var identity = new ClaimsIdentity(ApiKeyAuthenticationOptions.DefaultScheme);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, ApiKeyAuthenticationOptions.DefaultScheme);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
