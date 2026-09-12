namespace Ledger.SyncServer.Authentication;

/// Binds to the `ApiKeys` configuration section — one hash per device
/// trusted to call this server. No key is ever stored, only its
/// <see cref="ApiKeyHasher.Hash"/>.
///
/// Deliberately just a flat list, not a database table: this server has
/// no self-service "register a new device" endpoint (opening one would
/// be its own chicken-and-egg authentication problem), so keys are
/// provisioned out-of-band by whoever operates the server — see the
/// README for how to generate one. A real multi-household deployment
/// with device revocation audit trails would earn a database-backed
/// store; a single household's self-hosted sync server doesn't need one
/// yet.
public sealed class ApiKeySettings
{
    public const string SectionName = "ApiKeys";

    public IReadOnlyList<string> Hashes { get; set; } = [];
}
