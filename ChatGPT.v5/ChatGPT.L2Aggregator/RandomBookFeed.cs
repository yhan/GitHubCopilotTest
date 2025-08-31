public sealed class RandomBookFeed : IMarketDataFeed
{
    private readonly Venue _venue;
    private readonly string _name;
    private readonly TimeSpan _latency;
    private readonly decimal _tick;
    private readonly Random _rng = new();
    private readonly Dictionary<string, string> _venueSymbol = new(StringComparer.OrdinalIgnoreCase);

    public string Name => _name;

    public RandomBookFeed(Venue venue, string name, TimeSpan latency, decimal tick = 0.01m)
    {
        _venue = venue;
        _name = name;
        _latency = latency;
        _tick = tick;
    }

    public async IAsyncEnumerable<MdEvent> StreamAsync(IEnumerable<string> canonicalSymbols, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var state = new Dictionary<string, MidAndBook>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in canonicalSymbols)
        {
            state[s] = new MidAndBook { Mid = 100m + _rng.Next(-50, 50) };
            _venueSymbol[s] = ToVenueSymbol(s);
        }

        while (!ct.IsCancellationRequested)
        {
            foreach (var (sym, sb) in state.ToList())
            {
                // random-walk mid
                sb.Mid += _tick * _rng.Next(-2, 3);
                var mid = sb.Mid;

                // generate a small batch of level updates around mid
                var updates = new List<LevelUpdate>(capacity: 6);

                // create 3 bid levels, 3 ask levels with random sizes
                for (int i = 0; i < 3; i++)
                {
                    var bidPx = RoundToTick(mid - _tick * (2 + i));
                    var askPx = RoundToTick(mid + _tick * (2 + i));
                    updates.Add(new LevelUpdate(Side.Bid, bidPx, Math.Max(0, 50 + _rng.Next(-10, 40))));
                    updates.Add(new LevelUpdate(Side.Ask, askPx, Math.Max(0, 50 + _rng.Next(-10, 40))));
                }

                // occasionally remove a level (size=0)
                if (_rng.NextDouble() < 0.2)
                {
                    var px = RoundToTick(mid - _tick * _rng.Next(2, 5));
                    updates.Add(new LevelUpdate(Side.Bid, px, 0));
                }
                if (_rng.NextDouble() < 0.2)
                {
                    var px = RoundToTick(mid + _tick * _rng.Next(2, 5));
                    updates.Add(new LevelUpdate(Side.Ask, px, 0));
                }

                await Task.Delay(_latency + TimeSpan.FromMilliseconds(_rng.Next(0, 8)), ct);

                var now = DateTimeOffset.UtcNow;
                yield return new BookDelta(sym, _venueSymbol[sym], _venue, now, DateTimeOffset.UtcNow, updates, true, true);
            }
        }
    }

    private sealed class MidAndBook { public decimal Mid; }
    private string ToVenueSymbol(string canonical) => canonical + (_venue == Venue.Alpha ? ".A" : ".B");
    private decimal RoundToTick(decimal px) => Math.Round(px / _tick, MidpointRounding.AwayFromZero) * _tick;
}