// High‑Performance L2 Aggregator (Incremental + Sharded)
// .NET 8, single‑file demo with:
//  - int‑tick prices & int sizes (fixed‑point)
//  - incremental aggregation (touch only changed prices)
//  - per‑symbol state, sharded by hash across CPU cores
//  - tiny top‑N maintenance (fixed buffer; O(N) with tiny N) + optional heap variant
//  - bounded channels with DropOldest for backpressure
//  - zero LINQ in hot paths; structs for hot data
//  - array pooling for transient buffers
//  - egress stub (binary frames) using named pipe (optional, toggle at bottom)
//  - source‑generated JSON (optional, toggle at bottom)
//
// Build & run:
//   dotnet new console -n L2AggPerf
//   cd L2AggPerf
//   replace Program.cs with this file
//   dotnet run -c Release
//
// Publish perf‑friendly:
//   dotnet publish -c Release -p:PublishReadyToRun=true -p:TieredPGO=true
//   # For NativeAOT microservice egress: -p:PublishAot=true (requires trimming‑friendly deps)

using System.Diagnostics;
using System;
using System.Collections.Generic;



#region Optional: Minimal Binary Egress via Named Pipe (local IPC)

#endregion

#region Demo App

static decimal ToPx(int ticks, int scale) => ticks / (decimal)scale;

var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

var symbols = new[] { "AAPL", "MSFT" };
const int SCALE = 100; // 0.01 tick

var feeds = new IMarketDataFeed[]
{
    new RandomBookFeed(Venue.Alpha,   "Alpha-ITCH", TimeSpan.FromMilliseconds(2), SCALE),
    new RandomBookFeed(Venue.Bravo,   "Bravo-FAST", TimeSpan.FromMilliseconds(5), SCALE),
};

await using var agg = new L2Aggregator(feeds, depth:10, shards:Math.Max(1, Environment.ProcessorCount/2), minEmitInterval:TimeSpan.FromMilliseconds(10));

// Toggle: pipe egress (optional)
var usePipe = false; // set true to test pipe egress
PipeEgress? pipe = null;

if (usePipe)
{
    pipe = new PipeEgress("md_agg_l2");
    _ = pipe.StartAsync(agg.StreamAsync(symbols, cts.Token), cts.Token);
}

// Console sink for demo (avoid LINQ)
await foreach (var book in agg.StreamAsync(symbols, cts.Token))
{
    var tsMs = (book.PublishTs) * 1.0 * 1000 / Stopwatch.Frequency;
    Console.Write($"{tsMs,10:F3} {book.CanonicalSymbol} ");
    if (book.IsCrossedOrLocked) Console.Write("[LOCK/CRS] ");
    Console.Write("B:");
    for (int i=0;i<book.Bids.Length;i++)
    {
        var l = book.Bids[i];
        Console.Write($" {ToPx(l.PriceTicks, SCALE):F2}x{l.Size}");
        if (i+1<book.Bids.Length) Console.Write(" |");
    }
    Console.Write("  A:");
    for (int i=0;i<book.Asks.Length;i++)
    {
        var l = book.Asks[i];
        Console.Write($" {ToPx(l.PriceTicks, SCALE):F2}x{l.Size}");
        if (i+1<book.Asks.Length) Console.Write(" |");
    }
    Console.WriteLine();
}

if (pipe is not null) await pipe.DisposeAsync();

#endregion


// ===========================
// Shared Memory Ring Buffer Egress (SPSC, fixed-size slots)
// ===========================
// Cross‑process, cross‑platform (Windows/Linux/macOS) using:
//  - MemoryMappedFile for the shared region
//  - Named Semaphores for producer/consumer signaling (no named EventWaitHandle needed)
//  - Single‑Producer / Single‑Consumer per ring (create one ring per client)
//  - Fixed slot size for simple, fast semantics
//
// Files provided in this single block:
//  - ShmRingWriter: producer API (aggregator side)
//  - ShmRingReader: consumer API (client side)
//  - ShmEgress: IEgressPublisher impl that writes AggBook frames into a ring
//  - ShmReaderSample: tiny console reader that prints frames
//
// Notes:
//  * Use one ring per connected client for MPSC fan‑out (like the WebSocket egress design).
//  * Backpressure: writer blocks when no free slot; you can set a timeout and drop if desired.
//  * Slot payload format is: [int length][payload bytes], where length <= slotSize-4.
//  * Payload wire format for AggBook is compact binary (see EncodeAggBook).



// ===========================
// Sample reader process (attach from another app)
// ===========================

// ===========================
// How to wire in Program.cs (example):
// ===========================
// var egress = new ShmEgress(ringName: "md_l2_AAPL", slotSize: 2048, slotCount: 8192);
// _ = egress.StartAsync(agg.StreamAsync(new[]{"AAPL"}, cts.Token), cts.Token);
// In another process:
// await ShmReaderSample.RunAsync("md_l2_AAPL", scale: 100, cts.Token);
