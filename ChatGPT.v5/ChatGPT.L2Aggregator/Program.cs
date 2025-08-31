

var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

var symbols = new[] { "AAPL", "MSFT" };

var feeds = new IMarketDataFeed[]
{
    new RandomBookFeed(Venue.Alpha, "Alpha-ITCH", TimeSpan.FromMilliseconds(3), tick: 0.01m),
    new RandomBookFeed(Venue.Bravo, "Bravo-FAST", TimeSpan.FromMilliseconds(7), tick: 0.01m),
};

await using var agg = new L2Aggregator(
    feeds,
    venueStaleAfter: TimeSpan.FromMilliseconds(500),  // drop quiet venues
    minEmitInterval: TimeSpan.FromMilliseconds(10),   // conflation
    depth: 5                                          // top-N per side
);

Console.WriteLine("Starting L2 aggregated book (sizes summed by price). Ctrl+C to stop.\n");

await foreach (var book in agg.StreamAsync(symbols, cts.Token))
{
    // Pretty print
    Console.WriteLine($"{book.PublishTime:HH:mm:ss.fff} {book.CanonicalSymbol}  {(book.IsCrossedOrLocked ? "[LOCKED/CROSSED]" : "")}");
    Console.Write("  BIDS: ");
    Console.WriteLine(string.Join(" | ", book.Bids.Select(l => $"{l.Price:F2} x {l.Size}")));
    Console.Write("  ASKS: ");
    Console.WriteLine(string.Join(" | ", book.Asks.Select(l => $"{l.Price:F2} x {l.Size}")));
}


