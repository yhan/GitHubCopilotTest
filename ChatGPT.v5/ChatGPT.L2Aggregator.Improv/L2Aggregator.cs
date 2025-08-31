using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

public sealed class L2Aggregator : IAsyncDisposable
{
    private readonly IReadOnlyList<IMarketDataFeed> _feeds;
    private readonly int _depth;                 // top-N per side
    private readonly TimeSpan _minEmitInterval;  // conflation / heartbeat
    private readonly Channel<AggBook> _out;

    // symbol -> state (single-threaded in this sample; shard if you parallelize)
    private readonly ConcurrentDictionary<string, SymbolState> _symbols = new(StringComparer.OrdinalIgnoreCase);

    public L2Aggregator(IEnumerable<IMarketDataFeed> feeds, int depth = 10, TimeSpan? minEmitInterval = null)
    {
        _feeds = feeds.ToList();
        _depth = Math.Max(1, depth);
        _minEmitInterval = minEmitInterval ?? TimeSpan.FromMilliseconds(10);
        _out = Channel.CreateUnbounded<AggBook>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
    }

    public async IAsyncEnumerable<AggBook> StreamAsync(IEnumerable<string> symbols, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        _ = StartAsync(symbols, ct);
        while (await _out.Reader.WaitToReadAsync(ct))
        while (_out.Reader.TryRead(out var ev)) yield return ev;
    }

    private async Task StartAsync(IEnumerable<string> symbols, CancellationToken ct)
    {
        var hub = Channel.CreateUnbounded<BookDelta>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });

        // Fan-in from feeds
        foreach (var feed in _feeds)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await foreach (var d in feed.StreamAsync(symbols, ct))
                        await hub.Writer.WriteAsync(d, ct);
                }
                catch (OperationCanceledException) { }
                catch (Exception ex) { Console.Error.WriteLine($"[Feed {feed.Name}] {ex}"); }
            }, ct);
        }

        try
        {
            while (await hub.Reader.WaitToReadAsync(ct))
            {
                while (hub.Reader.TryRead(out var d))
                    OnDelta(d);
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            _out.Writer.TryComplete();
        }
    }

    // ======== Per-symbol incremental state ========
    private sealed class VenueBook
    {
        public readonly Dictionary<int, int> Bids = new(); // priceTick -> size
        public readonly Dictionary<int, int> Asks = new();
        public bool Connected = true;
        public bool InSync = true;
        public TradingStatus Status = TradingStatus.Trading;
        public long LastSeq;
    }

    private sealed class SymbolState
    {
        // Per-venue maps
        public readonly Dictionary<Venue, VenueBook> Venue = new();

        // Aggregate sums per price (across eligible venues)
        public readonly Dictionary<int, int> AggBids = new();
        public readonly Dictionary<int, int> AggAsks = new();

        // Tiny top-N buffers (kept sorted)
        public readonly List<AggLevel> TopBids; // desc by price
        public readonly List<AggLevel> TopAsks; // asc by price

        public readonly int Depth;
        public DateTimeOffset LastEmittedAt;
        public (AggLevel[] bids, AggLevel[] asks)? LastSnapshot; // for change detection

        public SymbolState(int depth)
        {
            Depth = depth;
            TopBids = new(depth);
            TopAsks = new(depth);
        }
    }

    private static bool Eligible(VenueBook v) => v.Connected && v.InSync && v.Status == TradingStatus.Trading;

    private void OnDelta(BookDelta d)
    {
        var sym = _symbols.GetOrAdd(d.CanonicalSymbol, _ => new SymbolState(_depth));
        var vbook = sym.Venue.GetValueOrDefault(d.Venue) ?? (sym.Venue[d.Venue] = new VenueBook());

        // Optional status/seq handling
        if (d.Status is { } st) vbook.Status = st;
        if (d.Seq is long seq)
        {
            if (vbook.LastSeq != 0 && seq != vbook.LastSeq + 1) vbook.InSync = false; // gap -> exclude until snapshot
            vbook.LastSeq = seq;
        }

        // If venue is not eligible, ignore deltas until it becomes eligible again.
        if (!Eligible(vbook)) return;

        // Handle side replacements (snapshot semantics): remove venue's contribution from aggregate, then clear venue side.
        if (d.ReplaceBids) ReplaceSide(sym.AggBids, vbook.Bids, isBid: true, sym);
        if (d.ReplaceAsks) ReplaceSide(sym.AggAsks, vbook.Asks, isBid: false, sym);

        // Apply updates incrementally
        foreach (var u in d.Updates)
        {
            if (u.Side == Side.Bid)
                ApplyIncremental(sym.AggBids, vbook.Bids, u.PriceTicks, u.Size, isBid: true, sym);
            else
                ApplyIncremental(sym.AggAsks, vbook.Asks, u.PriceTicks, u.Size, isBid: false, sym);
        }

        // Decide emission
        var now = DateTimeOffset.UtcNow;
        bool heartbeat = (now - sym.LastEmittedAt) >= _minEmitInterval;
        bool changed = UpdateCrossAndDetectChange(sym, out bool crossedOrLocked);

        if (changed || heartbeat)
        {
            var ev = new AggBook
            {
                CanonicalSymbol = d.CanonicalSymbol,
                EventTime = d.EventTime,
                PublishTime = now,
                Bids = sym.TopBids.Select(x => new AggLevel { PriceTicks = x.PriceTicks, Size = x.Size }).ToArray(),
                Asks = sym.TopAsks.Select(x => new AggLevel { PriceTicks = x.PriceTicks, Size = x.Size }).ToArray(),
                IsCrossedOrLocked = crossedOrLocked
            };
            sym.LastEmittedAt = now;
            _out.Writer.TryWrite(ev);
        }
    }

    // Remove venue contribution then clear venue side (for snapshot replace)
    private static void ReplaceSide(Dictionary<int, int> aggSide, Dictionary<int, int> venueSide, bool isBid, SymbolState sym)
    {
        if (venueSide.Count == 0) return;

        foreach (var (px, old) in venueSide)
        {
            var newSum = UpdateAggMap(aggSide, px, -old);
            TouchTop(sym, isBid, px, newSum);
        }
        venueSide.Clear();
    }

    // Apply a single level change incrementally
    private static void ApplyIncremental(Dictionary<int, int> aggSide,
        Dictionary<int, int> venueSide,
        int px, int newSz, bool isBid, SymbolState sym)
    {
        venueSide.TryGetValue(px, out var old);
        if (old == newSz) return;

        // update venue map
        if (newSz <= 0) venueSide.Remove(px);
        else venueSide[px] = newSz;

        // update aggregate = +diff
        var diff = newSz - old;
        var newSum = UpdateAggMap(aggSide, px, diff);

        // update tiny top-N buffer
        TouchTop(sym, isBid, px, newSum);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int UpdateAggMap(Dictionary<int, int> agg, int px, int delta)
    {
        agg.TryGetValue(px, out var cur);
        var next = cur + delta;
        if (next <= 0) agg.Remove(px);
        else agg[px] = next;
        return next;
    }

    // Maintain tiny top-N buffers (kept sorted). For bids: desc by price; asks: asc by price.
    private static void TouchTop(SymbolState sym, bool isBid, int px, int sum)
    {
        var top = isBid ? sym.TopBids : sym.TopAsks;
        int depth = sym.Depth;

        // Find existing index (N is tiny; linear scan is fastest)
        int idx = -1;
        for (int i = 0; i < top.Count; i++)
            if (top[i].PriceTicks == px) { idx = i; break; }

        if (sum <= 0)
        {
            if (idx >= 0) top.RemoveAt(idx);
            return;
        }

        if (idx >= 0)
        {
            // update size and re-position by price
            top[idx].Size = sum;
            Reposition(top, idx, isBid);
            return;
        }

        // Not in top: insert if capacity or better than boundary
        if (top.Count < depth)
        {
            InsertSorted(top, new AggLevel { PriceTicks = px, Size = sum }, isBid);
        }
        else
        {
            var boundary = top[^1];
            bool better = isBid ? px > boundary.PriceTicks : px < boundary.PriceTicks;
            if (better)
            {
                top.RemoveAt(top.Count - 1);
                InsertSorted(top, new AggLevel { PriceTicks = px, Size = sum }, isBid);
            }
        }
    }

    private static void InsertSorted(List<AggLevel> top, AggLevel lvl, bool isBid)
    {
        // binary search would need comparer; for tiny N, linear is fine
        int i = 0;
        if (isBid)
        {
            while (i < top.Count && top[i].PriceTicks > lvl.PriceTicks) i++;
        }
        else
        {
            while (i < top.Count && top[i].PriceTicks < lvl.PriceTicks) i++;
        }
        top.Insert(i, lvl);
    }

    private static void Reposition(List<AggLevel> top, int idx, bool isBid)
    {
        var lvl = top[idx];
        // move left
        while (idx > 0 && (isBid ? top[idx - 1].PriceTicks < lvl.PriceTicks : top[idx - 1].PriceTicks > lvl.PriceTicks))
        {
            top[idx] = top[idx - 1]; idx--;
        }
        // move right
        while (idx + 1 < top.Count && (isBid ? top[idx + 1].PriceTicks > lvl.PriceTicks : top[idx + 1].PriceTicks < lvl.PriceTicks))
        {
            top[idx] = top[idx + 1]; idx++;
        }
        top[idx] = lvl;
    }

    private static bool UpdateCrossAndDetectChange(SymbolState sym, out bool crossedOrLocked)
    {
        crossedOrLocked = sym.TopBids.Count > 0 && sym.TopAsks.Count > 0
                                                && sym.TopBids[0].PriceTicks >= sym.TopAsks[0].PriceTicks;

        // Detect change versus last snapshot (prices/sizes at top-N)
        var curB = sym.TopBids;
        var curA = sym.TopAsks;

        var last = sym.LastSnapshot;
        if (last is null)
        {
            sym.LastSnapshot = (curB.Select(Clone).ToArray(), curA.Select(Clone).ToArray());
            return true;
        }

        bool changed = !Same(last.Value.bids, curB) || !Same(last.Value.asks, curA);
        if (changed)
            sym.LastSnapshot = (curB.Select(Clone).ToArray(), curA.Select(Clone).ToArray());
        return changed;

        static AggLevel Clone(AggLevel x) => new AggLevel { PriceTicks = x.PriceTicks, Size = x.Size };

        static bool Same(AggLevel[] prev, List<AggLevel> cur)
        {
            if (prev.Length != cur.Count) return false;
            for (int i = 0; i < prev.Length; i++)
                if (prev[i].PriceTicks != cur[i].PriceTicks || prev[i].Size != cur[i].Size) return false;
            return true;
        }
    }

    public ValueTask DisposeAsync()
    {
        _out.Writer.TryComplete();
        return default;
    }
}