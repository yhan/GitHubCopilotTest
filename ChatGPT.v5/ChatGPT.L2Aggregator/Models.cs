
public enum Venue { Alpha, Bravo }
public enum Side { Bid, Ask }

public abstract record MdEvent(
    string CanonicalSymbol,
    string VenueSymbol,
    Venue Venue,
    DateTimeOffset EventTime,
    DateTimeOffset ReceiveTime);

/// <summary>
/// A batch of price-level upserts. Size==0 removes the level.
/// Prices are already tick-normalized per venue.
/// </summary>
public sealed record BookDelta(
    string CanonicalSymbol,
    string VenueSymbol,
    Venue Venue,
    DateTimeOffset EventTime,
    DateTimeOffset ReceiveTime,
    IReadOnlyList<LevelUpdate> Updates,
    bool ReplaceBids = false,
    bool ReplaceAsks = false) : MdEvent(CanonicalSymbol, VenueSymbol, Venue, EventTime, ReceiveTime);

public sealed record LevelUpdate(Side Side, decimal Price, decimal Size);

/// Aggregated output: top-N by side with sizes summed across venues at the same price.
public sealed record AggBook(
    string CanonicalSymbol,
    DateTimeOffset EventTime,
    DateTimeOffset PublishTime,
    IReadOnlyList<AggLevel> Bids, // sorted desc by price
    IReadOnlyList<AggLevel> Asks, // sorted asc by price
    bool IsCrossedOrLocked);

public sealed record AggLevel(decimal Price, decimal Size);

public interface IMarketDataFeed
{
    string Name { get; }
    IAsyncEnumerable<MdEvent> StreamAsync(IEnumerable<string> canonicalSymbols, CancellationToken ct);
}

