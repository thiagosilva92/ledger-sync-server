using Ledger.SyncServer.Domain;

namespace Ledger.SyncServer.UnitTests;

public class EventDeduplicationTests
{
    [Fact]
    public void Returns_all_events_when_none_are_already_known()
    {
        var incoming = new[]
        {
            new IncomingEvent("evt-1", "{}"),
            new IncomingEvent("evt-2", "{}"),
        };

        var result = EventDeduplication.SelectNew(new HashSet<string>(), incoming);

        Assert.Equal(incoming, result);
    }

    [Fact]
    public void Excludes_events_whose_id_is_already_known()
    {
        var known = new HashSet<string> { "evt-1" };
        var incoming = new[]
        {
            new IncomingEvent("evt-1", "{}"),
            new IncomingEvent("evt-2", "{}"),
        };

        var result = EventDeduplication.SelectNew(known, incoming);

        Assert.Equal([new IncomingEvent("evt-2", "{}")], result);
    }

    [Fact]
    public void Keeps_only_the_first_occurrence_of_a_repeated_id_within_the_same_batch()
    {
        // A caller retrying a push it isn't sure landed can send the same
        // id twice in one call, not just across separate calls — this is
        // the scenario that matters, not just "duplicates never happen".
        var incoming = new[]
        {
            new IncomingEvent("evt-1", "first payload"),
            new IncomingEvent("evt-1", "retried payload, should be dropped"),
        };

        var result = EventDeduplication.SelectNew(new HashSet<string>(), incoming);

        Assert.Equal([new IncomingEvent("evt-1", "first payload")], result);
    }

    [Fact]
    public void Returns_an_empty_list_for_an_empty_batch()
    {
        var result = EventDeduplication.SelectNew(new HashSet<string>(), []);

        Assert.Empty(result);
    }

    [Fact]
    public void Preserves_the_original_order_of_the_events_it_keeps()
    {
        var known = new HashSet<string> { "evt-2" };
        var incoming = new[]
        {
            new IncomingEvent("evt-3", "{}"),
            new IncomingEvent("evt-2", "{}"), // filtered out
            new IncomingEvent("evt-1", "{}"),
        };

        var result = EventDeduplication.SelectNew(known, incoming);

        Assert.Equal(
            [new IncomingEvent("evt-3", "{}"), new IncomingEvent("evt-1", "{}")],
            result);
    }

    [Fact]
    public void Does_not_mutate_the_caller_s_known_ids_set()
    {
        var known = new HashSet<string> { "evt-1" };
        var incoming = new[] { new IncomingEvent("evt-2", "{}") };

        EventDeduplication.SelectNew(known, incoming);

        Assert.Equal(["evt-1"], known);
    }
}
