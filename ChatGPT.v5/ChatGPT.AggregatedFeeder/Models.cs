
public enum Venue
{
    Alpha,   // e.g., feed A
    Bravo,   // e.g., feed B
    // extend: Cboe, Nasdaq, Arca, Binance, etc.
}

public abstract record MdEvent(
    string CanonicalSymbol,
    string VenueSymbol,
    Venue Venue,
    DateTimeOffset EventTime,
    DateTimeOffset ReceiveTime);

public sealed record L1Quote(
    string CanonicalSymbol,
    string VenueSymbol,
    Venue Venue,
    DateTimeOffset EventTime,
    DateTimeOffset ReceiveTime,
    decimal BidPx,
    decimal BidSz,
    decimal AskPx,
    decimal AskSz) : MdEvent(CanonicalSymbol, VenueSymbol, Venue, EventTime, ReceiveTime);

public sealed record Trade(
    string CanonicalSymbol,
    string VenueSymbol,
    Venue Venue,
    DateTimeOffset EventTime,
    DateTimeOffset ReceiveTime,
    decimal Price,
    decimal Size) : MdEvent(CanonicalSymbol, VenueSymbol, Venue, EventTime, ReceiveTime);

public sealed record Heartbeat(
    string CanonicalSymbol,
    string VenueSymbol,
    Venue Venue,
    DateTimeOffset EventTime,
    DateTimeOffset ReceiveTime) : MdEvent(CanonicalSymbol, VenueSymbol, Venue, EventTime, ReceiveTime);

// Aggregated outputs
public abstract record AggEvent(string CanonicalSymbol, DateTimeOffset EventTime, DateTimeOffset PublishTime);

public sealed record AggNbbo(
    string CanonicalSymbol,
    DateTimeOffset EventTime,
    DateTimeOffset PublishTime,
    decimal BestBidPx,
    decimal BestBidSz,
    Venue BestBidVenue,
    decimal BestAskPx,
    decimal BestAskSz,
    Venue BestAskVenue,
    bool IsCrossedOrLocked) : AggEvent(CanonicalSymbol, EventTime, PublishTime);

public sealed record AggTrade(
    string CanonicalSymbol,
    DateTimeOffset EventTime,
    DateTimeOffset PublishTime,
    decimal Price,
    decimal Size,
    Venue Venue) : AggEvent(CanonicalSymbol, EventTime, PublishTime);

public interface IMarketDataFeed
{
    string Name { get; }
    // Returns a unified async stream of events for the requested canonical symbols.
    IAsyncEnumerable<MdEvent> StreamAsync(IEnumerable<string> canonicalSymbols, CancellationToken ct);
}