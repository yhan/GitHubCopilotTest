using System.Collections.Concurrent;
using System.Threading.Channels;

public sealed class L2Aggregator : IAsyncDisposable
{
    private readonly IReadOnlyList<IMarketDataFeed> _feeds;
    private readonly TimeSpan _venueStaleAfter;
    private readonly TimeSpan _minEmitInterval;
    private readonly int _depth;

    private readonly Channel<AggBook> _out = Channel.CreateUnbounded<AggBook>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });

    // Per-symbol state
    private sealed class VenueSideBook
    {
        // price -> size ; we use decimal for price/size
        public readonly Dictionary<decimal, decimal> Levels = new();
        public DateTimeOffset LastSeen;
    }
    private sealed class VenueBook
    {
        public readonly VenueSideBook Bids = new();
        public readonly VenueSideBook Asks = new();
    }
    private sealed class SymbolState
    {
        // per venue book
        public readonly ConcurrentDictionary<Venue, VenueBook> VenueBooks = new();
        public DateTimeOffset LastEmittedAt = DateTimeOffset.MinValue;
        public AggBook? Last;
    }

    private readonly ConcurrentDictionary<string, SymbolState> _symbols = new(StringComparer.OrdinalIgnoreCase);

    public L2Aggregator(IEnumerable<IMarketDataFeed> feeds,
        TimeSpan venueStaleAfter,
        TimeSpan minEmitInterval,
        int depth)
    {
        _feeds = feeds.ToList();
        _venueStaleAfter = venueStaleAfter;
        _minEmitInterval = minEmitInterval;
        _depth = depth;
    }

    public async IAsyncEnumerable<AggBook> StreamAsync(IEnumerable<string> canonicalSymbols, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
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
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[Feed {feed.Name}] ERROR: {ex}");
                }
            }, ct);
        }

        try
        {
            while (await hub.Reader.WaitToReadAsync(ct))
            {
                while (hub.Reader.TryRead(out var ev))
                {
                    if (ev is BookDelta d)
                        OnDelta(d);
                }
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            _out.Writer.TryComplete();
        }
    }

    private void OnDelta(BookDelta d)
    {
        var sym = _symbols.GetOrAdd(d.CanonicalSymbol, _ => new SymbolState());
        var vbook = sym.VenueBooks.GetOrAdd(d.Venue, _ => new VenueBook());

        if (d.ReplaceBids) vbook.Bids.Levels.Clear();
        if (d.ReplaceAsks) vbook.Asks.Levels.Clear();

        // Apply updates into per-venue book
        foreach (var u in d.Updates)
        {
            var sideBook = u.Side == Side.Bid ? vbook.Bids : vbook.Asks;
            sideBook.LastSeen = d.ReceiveTime;

            if (u.Size <= 0)
            {
                sideBook.Levels.Remove(u.Price);
            }
            else
            {
                sideBook.Levels[u.Price] = u.Size;
            }
        }

        // Build aggregate using only non-stale venues
        var now = DateTimeOffset.UtcNow;
        var activeVenueBooks = sym.VenueBooks.Values.Where(v =>
            (now - v.Bids.LastSeen) <= _venueStaleAfter ||
            (now - v.Asks.LastSeen) <= _venueStaleAfter).ToArray();

        if (activeVenueBooks.Length == 0) return;

        // Sum by price across venues
        var bidSums = new Dictionary<decimal, decimal>();
        var askSums = new Dictionary<decimal, decimal>();

        foreach (VenueBook vb in activeVenueBooks)
        {
            foreach (var (px, sz) in vb.Bids.Levels)
                if (sz > 0)
                    bidSums[px] = bidSums.GetValueOrDefault(px) + sz;
            foreach (var (px, sz) in vb.Asks.Levels)
                if (sz > 0)
                    askSums[px] = askSums.GetValueOrDefault(px) + sz;
        }

        if (bidSums.Count == 0 && askSums.Count == 0) return;

        // Sort & take top-N per side
        static List<AggLevel> TakeTop(IDictionary<decimal, decimal> src, bool bids, int depth)
        {
            IEnumerable<KeyValuePair<decimal, decimal>> ordered = bids
                ? src.OrderByDescending(kv => kv.Key) // best bid highest price first
                : src.OrderBy(kv => kv.Key);          // best ask lowest price first;

            var list = new List<AggLevel>(depth);
            foreach (var (price, size) in ordered)
            {
                list.Add(new AggLevel(price, size));
                if (list.Count >= depth) break;
            }
            return list;
        }

        var bids = TakeTop(bidSums, bids: true, _depth);
        var asks = TakeTop(askSums, bids: false, _depth);

        if (bids.Count == 0 && asks.Count == 0) return;

        var crossedOrLocked = (bids.Count > 0 && asks.Count > 0) && (bids[0].Price >= asks[0].Price);

        // Emit on change or min interval elapsed
        var last = sym.Last;
        var publishTime = DateTimeOffset.UtcNow;
        var shouldEmit = last is null
                         || publishTime - sym.LastEmittedAt >= _minEmitInterval
                         || !LevelsEqual(last.Bids, bids)
                         || !LevelsEqual(last.Asks, asks);

        if (!shouldEmit) return;

        var ev = new AggBook(
            d.CanonicalSymbol,
            d.EventTime,              // event time of last delta batch
            publishTime,
            bids,
            asks,
            crossedOrLocked);

        sym.Last = ev;
        sym.LastEmittedAt = publishTime;

        _out.Writer.TryWrite(ev);
    }

    private static bool LevelsEqual(IReadOnlyList<AggLevel> a, IReadOnlyList<AggLevel> b)
    {
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
        {
            if (a[i].Price != b[i].Price || a[i].Size != b[i].Size)
                return false;
        }
        return true;
    }

    public ValueTask DisposeAsync()
    {
        _out.Writer.TryComplete();
        return default;
    }
}