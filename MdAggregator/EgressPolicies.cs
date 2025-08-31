using System.Threading.Channels;

public enum BackpressureMode
{
    DropOldest,   // keep freshest (good for market data)
    DropNewest,   // preserve history until full
}

public sealed record EgressPolicy(
    int BoundedCapacity = 4096,
    BackpressureMode BackpressureMode = BackpressureMode.DropOldest,
    TimeSpan? ConflateInterval = null // if set, emit at most once per interval per client
);
