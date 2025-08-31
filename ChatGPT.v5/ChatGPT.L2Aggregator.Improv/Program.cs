// dotnet new console -n L2AggInc
// cd L2AggInc
// Replace Program.cs with this file
// dotnet run

using System.Diagnostics;

#region Contracts & Models (incremental, int ticks)

#endregion

#region Demo app

static decimal ToPx(int ticks, int scale) => ticks / (decimal)scale;

var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

var symbols = new[] { "AAPL", "MSFT" };
const int TICK_SCALE = 100; // 0.01 USD ticks

var feeds = new IMarketDataFeed[]
{
    new RandomBookFeed(Venue.Alpha,   "Alpha-ITCH", TimeSpan.FromMilliseconds(3), TICK_SCALE),
    new RandomBookFeed(Venue.Bravo,   "Bravo-FAST", TimeSpan.FromMilliseconds(7), TICK_SCALE),
    // new RandomBookFeed(Venue.Charlie, "Charlie-ITCH", TimeSpan.FromMilliseconds(5), TICK_SCALE),
};

await using var agg = new L2Aggregator(feeds, depth: 10, minEmitInterval: TimeSpan.FromMilliseconds(15));

Console.WriteLine("Incremental L2 aggregator (sizes summed per price across venues). Ctrl+C to stop.\n");

await foreach (var book in agg.StreamAsync(symbols, cts.Token))
{
    Console.WriteLine($"{book.PublishTime:HH:mm:ss.fff} {book.CanonicalSymbol} {(book.IsCrossedOrLocked ? "[LOCK/CRS]" : "")}");
    Console.Write("  BIDS: ");
    Console.WriteLine(string.Join(" | ", book.Bids.Select(l => $"{ToPx(l.PriceTicks, TICK_SCALE):F2} x {l.Size}")));
    Console.Write("  ASKS: ");
    Console.WriteLine(string.Join(" | ", book.Asks.Select(l => $"{ToPx(l.PriceTicks, TICK_SCALE):F2} x {l.Size}")));
}

#endregion
