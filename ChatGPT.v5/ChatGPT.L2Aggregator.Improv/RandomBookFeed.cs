public sealed class RandomBookFeed : IMarketDataFeed
{
    private readonly Venue _venue;
    private readonly string _name;
    private readonly TimeSpan _latency;
    private readonly int _tickScale;
    private readonly Random _rng = new();

    public string Name => _name;

    public RandomBookFeed(Venue venue, string name, TimeSpan latency, int tickScale = 100)
    {
        _venue = venue; _name = name; _latency = latency; _tickScale = tickScale;
    }

    public async IAsyncEnumerable<BookDelta> StreamAsync(IEnumerable<string> symbols, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var mids = symbols.ToDictionary(s => s, _ => 10_000 + _rng.Next(-500, 500)); // ticks

        int seq = 0;
        while (!ct.IsCancellationRequested)
        {
            foreach (var s in symbols)
            {
                // random-walk mid
                mids[s] += _rng.Next(-3, 4);

                var upds = new List<LevelUpdate>(6);
                for (int i = 0; i < 3; i++)
                {
                    int bidPx = mids[s] - (3 + i);
                    int askPx = mids[s] + (3 + i);
                    upds.Add(new LevelUpdate { Side = Side.Bid, PriceTicks = bidPx, Size = Math.Max(0, 40 + _rng.Next(-10, 30)) });
                    upds.Add(new LevelUpdate { Side = Side.Ask, PriceTicks = askPx, Size = Math.Max(0, 40 + _rng.Next(-10, 30)) });
                }

                // Occasionally delete a level
                if (_rng.NextDouble() < 0.15) upds.Add(new LevelUpdate { Side = Side.Bid, PriceTicks = mids[s] - _rng.Next(2, 6), Size = 0 });
                if (_rng.NextDouble() < 0.15) upds.Add(new LevelUpdate { Side = Side.Ask, PriceTicks = mids[s] + _rng.Next(2, 6), Size = 0 });

                await Task.Delay(_latency + TimeSpan.FromMilliseconds(_rng.Next(0, 8)), ct);

                yield return new BookDelta
                {
                    CanonicalSymbol = s,
                    Venue = _venue,
                    EventTime = DateTimeOffset.UtcNow,
                    ReceiveTime = DateTimeOffset.UtcNow,
                    Updates = upds,
                    Seq = ++seq
                };

                // Occasionally send a snapshot replace (simulate venue snapshot)
                if (_rng.NextDouble() < 0.03)
                {
                    var snap = new List<LevelUpdate>(10);
                    for (int i = 0; i < 5; i++)
                    {
                        snap.Add(new LevelUpdate { Side = Side.Bid, PriceTicks = mids[s] - (2 + i), Size = 50 + _rng.Next(0, 40) });
                        snap.Add(new LevelUpdate { Side = Side.Ask, PriceTicks = mids[s] + (2 + i), Size = 50 + _rng.Next(0, 40) });
                    }
                    await Task.Delay(_latency, ct);
                    yield return new BookDelta
                    {
                        CanonicalSymbol = s,
                        Venue = _venue,
                        EventTime = DateTimeOffset.UtcNow,
                        ReceiveTime = DateTimeOffset.UtcNow,
                        Updates = snap,
                        ReplaceBids = true,
                        ReplaceAsks = true,
                        Seq = ++seq
                    };
                }
            }
        }
    }
}