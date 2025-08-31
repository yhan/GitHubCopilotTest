using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Channels;

public sealed class AggregatedFeeder : IAsyncDisposable
{
    private readonly IReadOnlyList<IMarketDataFeed> _feeds;
    private readonly TimeSpan _venueStaleAfter;
    private readonly TimeSpan _minNbboEmitInterval;
    private readonly Channel<AggEvent> _out = Channel.CreateUnbounded<AggEvent>(new UnboundedChannelOptions
    {
        SingleReader = true,
        SingleWriter = false
    });

    private readonly ConcurrentDictionary<string, SymbolState> _symbols = new();

    private sealed class VenueQuoteState
    {
        public L1Quote? LastQuote;
        public DateTimeOffset LastSeen;
    }

    private sealed class SymbolState
    {
        public ConcurrentDictionary<Venue, VenueQuoteState> VenueQuotes { get; } = new();
        public DateTimeOffset LastNbboEmittedAt = DateTimeOffset.MinValue;
        public AggNbbo? LastNbbo;
    }

    public AggregatedFeeder(IEnumerable<IMarketDataFeed> feeds,
        TimeSpan venueStaleAfter,
        TimeSpan minNbboEmitInterval)
    {
        _feeds = feeds.ToList();
        _venueStaleAfter = venueStaleAfter;
        _minNbboEmitInterval = minNbboEmitInterval;
    }

    public async IAsyncEnumerable<AggEvent> StreamAsync(IEnumerable<string> canonicalSymbols, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        _ = StartAsync(canonicalSymbols, ct); // fire-and-stream
        while (await _out.Reader.WaitToReadAsync(ct))
        {
            while (_out.Reader.TryRead(out var ev))
                yield return ev;
        }
    }

    private async Task StartAsync(IEnumerable<string> symbols, CancellationToken ct)
    {
        var hub = Channel.CreateUnbounded<MdEvent>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });

        // Fan-in from all feeds
        foreach (var feed in _feeds)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await foreach (var ev in feed.StreamAsync(symbols, ct))
                    {
                        if (!hub.Writer.TryWrite(ev))
                            await hub.Writer.WriteAsync(ev, ct);
                    }
                }
                catch (OperationCanceledException) { /* normal */ }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[Feed {feed.Name}] ERROR: {ex}");
                }
            }, ct);
        }

        // Main loop: aggregate
        var sw = Stopwatch.StartNew();
        try
        {
            while (await hub.Reader.WaitToReadAsync(ct))
            {
                while (hub.Reader.TryRead(out var ev))
                {
                    switch (ev)
                    {
                        case L1Quote q: OnQuote(q, sw, ct); break;
                        case Trade t: OnTrade(t, sw, ct); break;
                        case Heartbeat hb: OnHeartbeat(hb); break;
                    }
                }
            }
        }
        catch (OperationCanceledException) { /* normal */ }
        finally
        {
            _out.Writer.TryComplete();
        }
    }

    private void OnHeartbeat(Heartbeat hb)
    {
        // Could update liveness metrics; noop for now
    }

    private void OnTrade(Trade t, Stopwatch sw, CancellationToken ct)
    {
        var publish = new AggTrade(
            t.CanonicalSymbol,
            t.EventTime,
            DateTimeOffset.UtcNow,
            t.Price,
            t.Size,
            t.Venue);

        _out.Writer.TryWrite(publish);
    }

    private void OnQuote(L1Quote q, Stopwatch sw, CancellationToken ct)
    {
        var sym = _symbols.GetOrAdd(q.CanonicalSymbol, _ => new SymbolState());
        var venueState = sym.VenueQuotes.GetOrAdd(q.Venue, _ => new VenueQuoteState());
        venueState.LastQuote = q;
        venueState.LastSeen = q.ReceiveTime;

        var now = DateTimeOffset.UtcNow;

        // Build active venue set (filter stale)
        var active = new List<L1Quote>(capacity: sym.VenueQuotes.Count);
        foreach (var kv in sym.VenueQuotes)
        {
            var vstate = kv.Value;
            if (vstate.LastQuote is null) continue;
            if (now - vstate.LastSeen > _venueStaleAfter) continue;
            active.Add(vstate.LastQuote);
        }

        if (active.Count == 0) return;

        // NBBO: best bid = max, best ask = min; tie-breaker = latest EventTime then ReceiveTime
        L1Quote? bestBid = null, bestAsk = null;
        foreach (var x in active)
        {
            if (bestBid is null ||
                x.BidPx > bestBid.BidPx ||
                (x.BidPx == bestBid.BidPx && x.EventTime > bestBid.EventTime) ||
                (x.BidPx == bestBid.BidPx && x.EventTime == bestBid.EventTime && x.ReceiveTime > bestBid.ReceiveTime))
                bestBid = x;

            if (bestAsk is null ||
                x.AskPx < bestAsk.AskPx ||
                (x.AskPx == bestAsk.AskPx && x.EventTime > bestAsk.EventTime) ||
                (x.AskPx == bestAsk.AskPx && x.EventTime == bestAsk.EventTime && x.ReceiveTime > bestAsk.ReceiveTime))
                bestAsk = x;
        }
        Debug.Assert(bestBid is not null && bestAsk is not null);

        var crossedOrLocked = bestBid!.BidPx >= bestAsk!.AskPx; // true if crossed (>) or locked (==)

        // Basic de-noise: only emit if price/size changed or min interval elapsed
        var last = sym.LastNbbo;
        var shouldEmit = last is null
                         || last.BestBidPx != bestBid.BidPx
                         || last.BestAskPx != bestAsk.AskPx
                         || last.BestBidSz != bestBid.BidSz
                         || last.BestAskSz != bestAsk.AskSz
                         || (DateTimeOffset.UtcNow - sym.LastNbboEmittedAt) >= _minNbboEmitInterval;

        if (!shouldEmit) return;

        var agg = new AggNbbo(
            q.CanonicalSymbol,
            // Event time = max of the two sides we’re using
            (bestBid.EventTime > bestAsk.EventTime) ? bestBid.EventTime : bestAsk.EventTime,
            DateTimeOffset.UtcNow,
            bestBid.BidPx, bestBid.BidSz, bestBid.Venue,
            bestAsk.AskPx, bestAsk.AskSz, bestAsk.Venue,
            crossedOrLocked);

        sym.LastNbbo = agg;
        sym.LastNbboEmittedAt = agg.PublishTime;

        _out.Writer.TryWrite(agg);
    }

    public ValueTask DisposeAsync()
    {
        _out.Writer.TryComplete();
        return default;
    }
}