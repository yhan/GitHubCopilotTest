
#region Contracts & Fixed‑Point

using System.Runtime.CompilerServices;

public enum Venue : byte { Alpha, Bravo, Charlie }
public enum Side  : byte { Bid, Ask }
public enum TradingStatus : byte { Unknown, Trading, Halted, Auction, Closed }

// Tick scale per symbol (e.g., USD equities: 100 => 0.01)
public readonly struct TickScale
{
    public readonly int Scale; // e.g., 100
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TickScale(int scale) => Scale = scale;
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public decimal ToDecimal(int ticks) => ticks / (decimal)Scale;
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public int ToTicks(decimal px) => (int)Math.Round(px * Scale);
}

public interface IMarketDataFeed
{
    string Name { get; }
    IAsyncEnumerable<BookDelta> StreamAsync(IEnumerable<string> symbols, CancellationToken ct);
}

// Hot‑path structs
public readonly struct LevelUpdate
{
    public readonly Side Side;
    public readonly int PriceTicks;
    public readonly int Size; // non‑negative; 0 => delete
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public LevelUpdate(Side side, int px, int sz){ Side=side; PriceTicks=px; Size=sz; }
}

public sealed class BookDelta // batched message from a venue
{
    public string CanonicalSymbol = string.Empty;
    public Venue Venue;
    public long EventTs;    // Stopwatch timestamp (ticks)
    public long ReceiveTs;  // Stopwatch timestamp
    public LevelUpdate[] Updates = Array.Empty<LevelUpdate>();
    public bool ReplaceBids;
    public bool ReplaceAsks;
    public TradingStatus? Status;
    public long? Seq;
}

public readonly struct AggLevel
{
    public readonly int PriceTicks;
    public readonly int Size;
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public AggLevel(int px, int sz){ PriceTicks=px; Size=sz; }
}

public sealed class AggBook
{
    public string CanonicalSymbol = string.Empty;
    public long EventTs;    // Stopwatch timestamp
    public long PublishTs;  // Stopwatch timestamp
    public AggLevel[] Bids = Array.Empty<AggLevel>(); // desc by price
    public AggLevel[] Asks = Array.Empty<AggLevel>(); // asc by price
    public bool IsCrossedOrLocked;
}

#endregion