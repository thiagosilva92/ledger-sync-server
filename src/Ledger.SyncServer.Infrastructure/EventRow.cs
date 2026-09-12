namespace Ledger.SyncServer.Infrastructure;

/// The `events` table's row shape. Deliberately separate from
/// <c>Domain.StoredEvent</c> — this type is EF Core's mapping concern
/// (mutable, parameterless-constructible, no meaning outside this
/// project), not the domain's own value type. <see cref="PostgresEventLog"/>
/// is what translates between the two.
public sealed class EventRow
{
    public long Sequence { get; set; }

    public required string EventId { get; set; }

    public required string Payload { get; set; }
}
