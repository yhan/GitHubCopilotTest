using System.Buffers;
using System.Diagnostics;
using System.Runtime.CompilerServices;
//  Simulator Feeds (no LINQ, pooled buffers)
public sealed class RandomBookFeed : IMarketDataFeed
{
    private readonly Venue _venue;
    private readonly string _name;
    private readonly TimeSpan _latency;
    private readonly int _scale; // tick scale (e.g., 100)
    private readonly Random _rng = new();

    public string Name => _name;
    public RandomBookFeed(Venue venue, string name, TimeSpan latency, int tickScale)
    { _venue=venue; _name=name; _latency=latency; _scale=tickScale; }

    public async IAsyncEnumerable<BookDelta> StreamAsync(IEnumerable<string> symbols, [EnumeratorCancellation] CancellationToken ct)
    {
        // init mids per symbol in ticks
        var mids = new Dictionary<string,int>(StringComparer.Ordinal);
        foreach (var s in symbols) mids[s] = 10000 + _rng.Next(-500, 500);

        long seq = 0;
        while (!ct.IsCancellationRequested)
        {
            foreach (var s in symbols)
            {
                int mid = mids[s] += _rng.Next(-2, 3);

                // rent a buffer for updates (amortize allocs)
                var buf = ArrayPool<LevelUpdate>.Shared.Rent(8);
                int n = 0;
                for (int i=0;i<3;i++)
                {
                    int bidPx = mid - (2+i);
                    int askPx = mid + (2+i);
                    buf[n++] = new LevelUpdate(Side.Bid, bidPx, Math.Max(0, 40 + _rng.Next(-10, 30)));
                    buf[n++] = new LevelUpdate(Side.Ask, askPx, Math.Max(0, 40 + _rng.Next(-10, 30)));
                }
                if (_rng.NextDouble() < 0.15) buf[n++] = new LevelUpdate(Side.Bid, mid - _rng.Next(2,6), 0);
                if (_rng.NextDouble() < 0.15) buf[n++] = new LevelUpdate(Side.Ask, mid + _rng.Next(2,6), 0);

                await Task.Delay(_latency + TimeSpan.FromMilliseconds(_rng.Next(0, 8)), ct);

                var now = Stopwatch.GetTimestamp();
                var del = new BookDelta
                {
                    CanonicalSymbol = s,
                    Venue = _venue,
                    EventTs = now,
                    ReceiveTs = now,
                    Updates = buf.AsSpan(0, n).ToArray(),
                    Seq = ++seq
                };
                ArrayPool<LevelUpdate>.Shared.Return(buf, clearArray: false);
                yield return del;

                // occasional snapshot replace
                if (_rng.NextDouble() < 0.03)
                {
                    var snapBuf = ArrayPool<LevelUpdate>.Shared.Rent(10);
                    int m = 0;
                    for (int i=0;i<5;i++)
                    {
                        snapBuf[m++] = new LevelUpdate(Side.Bid, mid - (2+i), 50 + _rng.Next(0,40));
                        snapBuf[m++] = new LevelUpdate(Side.Ask, mid + (2+i), 50 + _rng.Next(0,40));
                    }
                    await Task.Delay(_latency, ct);
                    var del2 = new BookDelta
                    {
                        CanonicalSymbol = s,
                        Venue = _venue,
                        EventTs = Stopwatch.GetTimestamp(),
                        ReceiveTs = Stopwatch.GetTimestamp(),
                        Updates = snapBuf.AsSpan(0, m).ToArray(),
                        ReplaceBids = true,
                        ReplaceAsks = true,
                        Seq = ++seq
                    };
                    ArrayPool<LevelUpdate>.Shared.Return(snapBuf, clearArray:false);
                    yield return del2;
                }
            }
        }
    }
}