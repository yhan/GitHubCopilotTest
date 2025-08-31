using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

//  Aggregator (Incremental + Sharded)

public sealed class L2Aggregator : IAsyncDisposable
{
    private readonly IReadOnlyList<IMarketDataFeed> _feeds;
    private readonly int _depth;
    private readonly TimeSpan _minEmitInterval;
    private readonly Channel<AggBook> _out;
    private readonly int _shards;

    // Router -> per?shard channels
    private readonly Channel<BookDelta>[] _ingest;

    // symbol -> shard index (stable)
    private readonly ConcurrentDictionary<string,int> _symbolShard = new(StringComparer.Ordinal);

    // Stopwatch frequency helpers
    private static readonly double s_ticksToMs = 1000.0 / Stopwatch.Frequency;

    public L2Aggregator(IEnumerable<IMarketDataFeed> feeds, int depth = 10, int shards = 0, TimeSpan? minEmitInterval = null)
    {
        _feeds = feeds is IReadOnlyList<IMarketDataFeed> list ? list : new List<IMarketDataFeed>(feeds);
        _depth = Math.Clamp(depth, 1, 64);
        _minEmitInterval = minEmitInterval ?? TimeSpan.FromMilliseconds(10);
        _out = Channel.CreateBounded<AggBook>(new BoundedChannelOptions(8192){ SingleReader=true, SingleWriter=false, FullMode=BoundedChannelFullMode.DropOldest });

        _shards = shards > 0 ? shards : Math.Max(1, Environment.ProcessorCount);
        _ingest = new Channel<BookDelta>[_shards];
        for (int i=0;i<_shards;i++)
            _ingest[i] = Channel.CreateBounded<BookDelta>(new BoundedChannelOptions(16384){ SingleReader=true, SingleWriter=false, FullMode=BoundedChannelFullMode.DropOldest });
    }

    public async IAsyncEnumerable<AggBook> StreamAsync(IEnumerable<string> symbols, [EnumeratorCancellation] CancellationToken ct)
    {
        _ = StartAsync(symbols, ct);
        while (await _out.Reader.WaitToReadAsync(ct))
        while (_out.Reader.TryRead(out var ev)) yield return ev;
    }

    private async Task StartAsync(IEnumerable<string> symbols, CancellationToken ct)
    {
        // Start shard workers
        for (int i=0;i<_shards;i++)
        {
            var idx = i;
            _ = Task.Run(() => ShardWorker(idx, ct), ct);
        }

        // Start feed readers -> route to shard by symbol
        foreach (var feed in _feeds)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await foreach (var d in feed.StreamAsync(symbols, ct))
                    {
                        var shard = GetShard(d.CanonicalSymbol);
                        var ch = _ingest[shard];
                        // best?effort write with drop on pressure
                        if (!ch.Writer.TryWrite(d))
                            await ch.Writer.WriteAsync(d, ct);
                    }
                }
                catch (OperationCanceledException) { }
                catch (Exception ex) { Console.Error.WriteLine($"[Feed {feed.Name}] {ex}"); }
            }, ct);
        }
    }

    private int GetShard(string sym) => _symbolShard.GetOrAdd(sym, s => (int)((uint)s.GetHashCode() % (uint)_shards));

    // ===== Shard worker state types =====
    private sealed class VenueBook
    {
        public readonly Dictionary<int,int> Bids = new();
        public readonly Dictionary<int,int> Asks = new();
        public bool Connected = true;
        public bool InSync = true;
        public TradingStatus Status = TradingStatus.Trading;
        public long LastSeq;
    }

    private sealed class SymbolState
    {
        public readonly string Symbol;
        public readonly Dictionary<Venue, VenueBook> Venue = new();
        public readonly Dictionary<int,int> AggBids = new();
        public readonly Dictionary<int,int> AggAsks = new();

        // Top?N buffers kept sorted (bids desc, asks asc)
        public AggLevel[] TopBids;
        public AggLevel[] TopAsks;
        public int CountBids;
        public int CountAsks;

        // Last emitted snapshot for change detection
        public AggLevel[] LastBids = Array.Empty<AggLevel>();
        public AggLevel[] LastAsks = Array.Empty<AggLevel>();
        public long LastEmitTs;

        public SymbolState(string s, int depth)
        {
            Symbol = s;
            TopBids = new AggLevel[depth];
            TopAsks = new AggLevel[depth];
        }
    }

    private async Task ShardWorker(int shard, CancellationToken ct)
    {
        var ch = _ingest[shard];
        var symbols = new Dictionary<string, SymbolState>(StringComparer.Ordinal);
        var minEmitTicks = (long)(_minEmitInterval.TotalSeconds * Stopwatch.Frequency);

        while (await ch.Reader.WaitToReadAsync(ct))
        {
            while (ch.Reader.TryRead(out var d))
            {
                if (!symbols.TryGetValue(d.CanonicalSymbol, out var sst))
                {
                    sst = new SymbolState(d.CanonicalSymbol, _depth);
                    symbols.Add(d.CanonicalSymbol, sst);
                }

                var vbook = sst.Venue.TryGetValue(d.Venue, out var vb) ? vb : (sst.Venue[d.Venue] = new VenueBook());

                if (d.Status.HasValue) vb.Status = d.Status.Value;
                if (d.Seq.HasValue)
                {
                    long seq = d.Seq.Value;
                    if (vb.LastSeq != 0 && seq != vb.LastSeq + 1) vb.InSync = false;
                    vb.LastSeq = seq;
                }

                if (!Eligible(vb)) continue; // drop until eligible

                if (d.ReplaceBids) ReplaceSide(sst.AggBids, vb.Bids, isBid:true, ref sst.TopBids, ref sst.CountBids);
                if (d.ReplaceAsks) ReplaceSide(sst.AggAsks, vb.Asks, isBid:false, ref sst.TopAsks, ref sst.CountAsks);

                var updates = d.Updates;
                for (int i=0;i<updates.Length;i++)
                {
                    ref readonly var u = ref updates[i];
                    if (u.Side == Side.Bid)
                        ApplyIncremental(sst.AggBids, vb.Bids, u.PriceTicks, u.Size, isBid:true, ref sst.TopBids, ref sst.CountBids);
                    else
                        ApplyIncremental(sst.AggAsks, vb.Asks, u.PriceTicks, u.Size, isBid:false, ref sst.TopAsks, ref sst.CountAsks);
                }

                var now = Stopwatch.GetTimestamp();
                bool heartbeat = (now - sst.LastEmitTs) >= minEmitTicks;
                bool changed = DetectChange(ref sst.TopBids, sst.CountBids, sst.LastBids) || DetectChange(ref sst.TopAsks, sst.CountAsks, sst.LastAsks);

                if (changed || heartbeat)
                {
                    // snapshot current top?N arrays (no LINQ, minimal alloc using ArrayPool)
                    var bidsSnap = ArrayPool<AggLevel>.Shared.Rent(sst.CountBids);
                    var asksSnap = ArrayPool<AggLevel>.Shared.Rent(sst.CountAsks);
                    Array.Copy(sst.TopBids, bidsSnap, sst.CountBids);
                    Array.Copy(sst.TopAsks, asksSnap, sst.CountAsks);

                    sst.LastBids = bidsSnap.AsSpan(0, sst.CountBids).ToArray();
                    sst.LastAsks = asksSnap.AsSpan(0, sst.CountAsks).ToArray();
                    ArrayPool<AggLevel>.Shared.Return(bidsSnap, clearArray:false);
                    ArrayPool<AggLevel>.Shared.Return(asksSnap, clearArray:false);

                    var outBids = sst.LastBids; var outAsks = sst.LastAsks;
                    var ev = new AggBook
                    {
                        CanonicalSymbol = sst.Symbol,
                        EventTs = d.EventTs,
                        PublishTs = now,
                        Bids = outBids,
                        Asks = outAsks,
                        IsCrossedOrLocked = sst.CountBids>0 && sst.CountAsks>0 && sst.TopBids[0].PriceTicks >= sst.TopAsks[0].PriceTicks
                    };

                    sst.LastEmitTs = now;
                    // bounded out channel; drop oldest if backpressured
                    _out.Writer.TryWrite(ev);
                }
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool Eligible(VenueBook v) => v.Connected && v.InSync && v.Status == TradingStatus.Trading;

    // Remove venue contribution then clear venue side
    private static void ReplaceSide(Dictionary<int,int> aggSide, Dictionary<int,int> venueSide, bool isBid, ref AggLevel[] top, ref int count)
    {
        if (venueSide.Count == 0) return;
        foreach (var kv in venueSide)
        {
            int newSum = UpdateAggMap(aggSide, kv.Key, -kv.Value);
            TouchTop(ref top, ref count, isBid, kv.Key, newSum);
        }
        venueSide.Clear();
    }

    // Apply single level change incrementally
    private static void ApplyIncremental(Dictionary<int,int> aggSide, Dictionary<int,int> venueSide,
        int px, int newSz, bool isBid,
        ref AggLevel[] top, ref int count)
    {
        venueSide.TryGetValue(px, out var old);
        if (old == newSz) return;
        if (newSz <= 0) venueSide.Remove(px); else venueSide[px] = newSz;

        int diff = newSz - old;
        int newSum = UpdateAggMap(aggSide, px, diff);
        TouchTop(ref top, ref count, isBid, px, newSum);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int UpdateAggMap(Dictionary<int,int> agg, int px, int delta)
    {
        agg.TryGetValue(px, out var cur);
        int next = cur + delta;
        if (next <= 0) agg.Remove(px); else agg[px] = next;
        return next;
    }

    // Tiny top?N maintenance (kept sorted: bids desc, asks asc). No heap to keep GC zero & constant factors tiny.
    private static void TouchTop(ref AggLevel[] top, ref int count, bool isBid, int px, int sum)
    {
        // find existing
        int idx = -1;
        for (int i=0;i<count;i++) if (top[i].PriceTicks == px) { idx=i; break; }

        if (sum <= 0)
        {
            if (idx >= 0)
            {
                // remove idx
                for (int j=idx+1;j<count;j++) top[j-1] = top[j];
                count--;
            }
            return;
        }

        var lvl = new AggLevel(px, sum);
        if (idx >= 0)
        {
            // update & reposition
            top[idx] = lvl;
            Reposition(ref top, ref count, isBid, idx);
            return;
        }

        // Not present
        if (count < top.Length)
        {
            // append then bubble to place
            top[count++] = lvl;
            Reposition(ref top, ref count, isBid, count-1);
        }
        else
        {
            // compare with boundary (worst element)
            var boundary = top[count-1];
            bool better = isBid ? px > boundary.PriceTicks : px < boundary.PriceTicks;
            if (!better) return;
            top[count-1] = lvl;
            Reposition(ref top, ref count, isBid, count-1);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Reposition(ref AggLevel[] top, ref int count, bool isBid, int idx)
    {
        var cur = top[idx];
        // move left while better than left neighbor
        while (idx > 0)
        {
            var left = top[idx-1];
            bool better = isBid ? cur.PriceTicks > left.PriceTicks : cur.PriceTicks < left.PriceTicks;
            if (!better) break;
            top[idx] = left; idx--; top[idx] = cur;
        }
        // move right while worse than right neighbor
        while (idx+1 < count)
        {
            var right = top[idx+1];
            bool worse = isBid ? cur.PriceTicks < right.PriceTicks : cur.PriceTicks > right.PriceTicks;
            if (!worse) break;
            top[idx] = right; idx++; top[idx] = cur;
        }
    }

    private static bool DetectChange(ref AggLevel[] top, int count, AggLevel[] last)
    {
        if (last.Length != count) return true;
        for (int i=0;i<count;i++)
        {
            var a = top[i]; var b = last[i];
            if (a.PriceTicks != b.PriceTicks || a.Size != b.Size) return true;
        }
        return false;
    }

    public ValueTask DisposeAsync(){ _out.Writer.TryComplete(); return default; }
}