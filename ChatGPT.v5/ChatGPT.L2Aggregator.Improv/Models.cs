public enum Venue
{
    Alpha,
    Bravo,
    Charlie
}

public enum Side
{
    Bid,
    Ask
}

public enum TradingStatus
{
    Unknown,
    Trading,
    Halted,
    Auction,
    Closed
}

public interface IMarketDataFeed
{
    string Name { get; }
    IAsyncEnumerable<BookDelta> StreamAsync(IEnumerable<string> symbols, CancellationToken ct);
}

/// Price/size update at a given venue; Size==0 means delete.
/// Prices are expressed as **ticks** (int). Example: USD equities tick = 0.01 ? 42.56 => 4256.
public sealed class LevelUpdate
{
    public Side Side { get; init; }
    public int PriceTicks { get; init; }
    public int Size { get; init; } // shares/contracts; use scaled int for fractional assets
}

/// A batch of venue deltas (or snapshot replace) for one symbol.
public sealed class BookDelta
{
    public string CanonicalSymbol { get; init; } = "";
    public Venue Venue { get; init; }
    public DateTimeOffset EventTime { get; init; }
    public DateTimeOffset ReceiveTime { get; init; }
    public IReadOnlyList<LevelUpdate> Updates { get; init; } = Array.Empty<LevelUpdate>();

    // Optional replace semantics (e.g., after a snapshot):
    public bool ReplaceBids { get; init; }
    public bool ReplaceAsks { get; init; }

    // Optional status/sync/seq (adapters can fill these if available)
    public TradingStatus? Status { get; init; }
    public long? Seq { get; init; }
}

public sealed class AggLevel
{
    public int PriceTicks { get; init; }
    public int Size { get; set; }
}

public sealed class AggBook
{
    public string CanonicalSymbol { get; init; } = "";
    public DateTimeOffset EventTime { get; init; }
    public DateTimeOffset PublishTime { get; init; }
    public IReadOnlyList<AggLevel> Bids { get; init; } = Array.Empty<AggLevel>(); // desc by price
    public IReadOnlyList<AggLevel> Asks { get; init; } = Array.Empty<AggLevel>(); // asc by price
    public bool IsCrossedOrLocked { get; init; }
}