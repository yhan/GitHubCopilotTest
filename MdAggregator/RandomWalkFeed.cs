public sealed class RandomWalkFeed : IMarketDataFeed
{
    private readonly Venue _venue;
    private readonly string _name;
    private readonly TimeSpan _baseLatency;
    private readonly decimal _spreadTicks;
    private readonly decimal _tickSize;
    private readonly Random _rng = new();

    // Example symbol mapping per venue; in real life, pull from map service
    private readonly Dictionary<string, string> _venueSymbol = new(StringComparer.OrdinalIgnoreCase);

    public string Name => _name;

    public RandomWalkFeed(Venue venue, string name, TimeSpan baseLatency, decimal tickSize = 0.01m, decimal spreadTicks = 2m)
    {
        _venue = venue;
        _name = name;
        _baseLatency = baseLatency;
        _tickSize = tickSize;
        _spreadTicks = spreadTicks;
    }

    public async IAsyncEnumerable<MdEvent> StreamAsync(IEnumerable<string> canonicalSymbols, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var state = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in canonicalSymbols)
        {
            state[s] = 100m + _rng.Next(-50, 50); // start price
            _venueSymbol[s] = ToVenueSymbol(s);
        }

        while (!ct.IsCancellationRequested)
        {
            foreach (var (sym, mid0) in state.ToList())
            {
                var mid = mid0 + _tickSize * (_rng.Next(-2, 3));
                state[sym] = mid;

                var bid = RoundToTick(mid - _tickSize * _spreadTicks / 2);
                var ask = RoundToTick(mid + _tickSize * _spreadTicks / 2);
                var now = DateTimeOffset.UtcNow;

                // bursty emission
                await Task.Delay(_baseLatency + TimeSpan.FromMilliseconds(_rng.Next(0, 10)), ct);

                var l1 = new L1Quote(
                    CanonicalSymbol: sym,
                    VenueSymbol: _venueSymbol[sym],
                    Venue: _venue,
                    EventTime: now.AddMilliseconds(-_rng.Next(0, 5)), // pretend wire delay
                    ReceiveTime: DateTimeOffset.UtcNow,
                    BidPx: bid,
                    BidSz: 100 + _rng.Next(0, 50),
                    AskPx: ask,
                    AskSz: 80 + _rng.Next(0, 50)
                );
                yield return l1;

                // occasional trade inside the spread
                if (_rng.NextDouble() < 0.25)
                {
                    var tradePx = RoundToTick(_rng.NextDouble() < 0.5 ? bid : ask);
                    var t = new Trade(sym, _venueSymbol[sym], _venue, now, DateTimeOffset.UtcNow, tradePx, 10 + _rng.Next(0, 25));
                    yield return t;
                }
            }
        }
    }

    private string ToVenueSymbol(string canonical) =>
        // toy mapping: “AAPL” -> “AAPL.O” on this venue
        canonical + (_venue == Venue.Alpha ? ".A" : ".B");

    private decimal RoundToTick(decimal px) => Math.Round(px / _tickSize, MidpointRounding.AwayFromZero) * _tickSize;
}