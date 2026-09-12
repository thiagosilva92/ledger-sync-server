namespace Ledger.SyncServer.Domain;

/// Pure decision logic for what a push actually needs to write: given the
/// event ids already on record, which of an incoming batch are genuinely
/// new.
///
/// This is the one piece of real logic <c>POST /events</c> has — everything
/// else is I/O (read known ids, insert new rows). Keeping it here, as a
/// static method over plain records with no database or HTTP in sight,
/// means it's unit-testable the same way <c>LedgerTransaction</c>'s
/// invariants are testable without an <c>EventStore</c>: no container, no
/// server, just an assertion about what the function returns for a given
/// input.
public static class EventDeduplication
{
    /// Returns, in order, the events from <paramref name="incoming"/> that
    /// are not in <paramref name="knownEventIds"/> — and, within
    /// <paramref name="incoming"/> itself, only the first occurrence of any
    /// repeated id.
    ///
    /// The in-batch de-dup matters for the same reason
    /// <c>DriftEventStore.merge</c> tolerates re-merging the same events on
    /// the client: a caller retrying a push it's not sure landed may send
    /// the same id twice in one call, not just across separate calls.
    public static IReadOnlyList<IncomingEvent> SelectNew(
        IReadOnlySet<string> knownEventIds,
        IEnumerable<IncomingEvent> incoming)
    {
        var seen = new HashSet<string>(knownEventIds);
        var result = new List<IncomingEvent>();

        foreach (var candidate in incoming)
        {
            if (seen.Add(candidate.EventId))
            {
                result.Add(candidate);
            }
        }

        return result;
    }
}
