
var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

var symbols = new[] { "AAPL", "MSFT" };

// Two venues with slightly different latency profiles
var feeds = new IMarketDataFeed[]
{
    new RandomWalkFeed(Venue.Alpha, "Alpha-WS", baseLatency: TimeSpan.FromMilliseconds(3), tickSize: 0.01m, spreadTicks: 2m),
    new RandomWalkFeed(Venue.Bravo, "Bravo-FIX", baseLatency: TimeSpan.FromMilliseconds(7), tickSize: 0.01m, spreadTicks: 3m),
};

await using var agg = new AggregatedFeeder(
    feeds,
    venueStaleAfter: TimeSpan.FromMilliseconds(250),   // drop venues that are quiet for 250ms
    minNbboEmitInterval: TimeSpan.FromMilliseconds(5)  // conflation guard
);

Console.WriteLine("Starting aggregated feeder. Press Ctrl+C to stop.\n");

var start = DateTimeOffset.UtcNow;
await foreach (var ev in agg.StreamAsync(symbols, cts.Token))
{
    switch (ev)
    {
        case AggNbbo nbbo:
            var spread = nbbo.BestAskPx - nbbo.BestBidPx;
            Console.WriteLine(
                $"{nbbo.PublishTime:HH:mm:ss.fff} NBBO {nbbo.CanonicalSymbol} " +
                $"Bid {nbbo.BestBidPx:F2}x{nbbo.BestBidSz}({nbbo.BestBidVenue}) | " +
                $"Ask {nbbo.BestAskPx:F2}x{nbbo.BestAskSz}({nbbo.BestAskVenue}) | " +
                $"Spread {spread:F2}" + (nbbo.IsCrossedOrLocked ? "  <-- LOCKED/CROSSED" : ""));
            break;

        case AggTrade t:
            Console.WriteLine($"{t.PublishTime:HH:mm:ss.fff} TRADE {t.CanonicalSymbol} {t.Price:F2} x {t.Size} ({t.Venue})");
            break;
    }
}



async Task EgressPub()
{
    var symbols = new[] { "AAPL", "MSFT" };
    var source = agg.StreamAsync(symbols, cts.Token);

// Start the hub (tees source into per-client buffers)
    var policy = new EgressPolicy(
        BoundedCapacity: 8192,
        BackpressureMode: BackpressureMode.DropOldest,
        ConflateInterval: TimeSpan.FromMilliseconds(10) // e.g. 100 Hz per client max
    );
    EgressHub.Start(source, policy, cts.Token);

// Choose any backends you want:
    await using var egress = new CompositeEgressPublisher(new IEgressPublisher[]
    {
        new WebSocketEgress("http://localhost:5000/ws/", policy),
        new KafkaEgress(topic: "md.agg.nbbo", policy),
        new NamedPipeEgress("md_agg_pipe", policy)
    });

// Fire them up (non-blocking; StartAsync returns when cancelled)
    _ = egress.StartAsync(source, cts.Token);

// You can still also print locally for debugging
    await foreach (var ev in source.WithCancellation(cts.Token))
    {
        if (ev is AggNbbo nbbo)
            Console.WriteLine($"NBBO {nbbo.CanonicalSymbol} {nbbo.BestBidPx:F2}@{nbbo.BestAskPx:F2}");
    }
}
