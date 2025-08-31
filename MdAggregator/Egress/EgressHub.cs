
//EgressHub.cs (the “tee” from the aggregator to all client queues)
using System.Collections.Concurrent;
using System.Threading.Channels;

// A singleton-style hub wiring the single aggregated source into N per-client buffers.
public static class EgressHub
{
    private static readonly ConcurrentDictionary<Guid, Channel<AggEvent>> _clients = new();
    private static volatile bool _started;
    private static IAsyncEnumerable<AggEvent>? _source;
    public static EgressPolicy Policy { get; private set; } = new();

    public static void Start(IAsyncEnumerable<AggEvent> source, EgressPolicy policy, CancellationToken ct)
    {
        if (_started) return;
        _started = true;
        _source = source;
        Policy = policy;

        _ = Task.Run(async () =>
        {
            try
            {
                await foreach (var ev in source.WithCancellation(ct))
                {
                    foreach (var kv in _clients)
                    {
                        var ch = kv.Value;
                        ch.Writer.TryWrite(ev); // respect per-client backpressure mode
                    }
                }
            }
            catch (OperationCanceledException) { }
            finally
            {
                foreach (var ch in _clients.Values) ch.Writer.TryComplete();
            }
        }, ct);
    }

    /// <summary>Register a client; returns a handle that provides a Reader.</summary>
    public static Channel<AggEvent> RegisterClient(ChannelWriter<AggEvent> writer, EgressPolicy _)
    {
        // We actually ignore the passed writer and create a full channel (writer+reader) here,
        // returning the channel so transport can read from it.
        var chan = Channel.CreateBounded<AggEvent>(new BoundedChannelOptions(Policy.BoundedCapacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = Policy.BackpressureMode == BackpressureMode.DropOldest
                ? BoundedChannelFullMode.DropOldest
                : BoundedChannelFullMode.DropWrite
        });

        var id = Guid.NewGuid();
        _clients[id] = chan;

        // Cleanup when closed
        Task notUsed = System.Threading.Tasks.Task.Run(async () =>
        {
            await chan.Reader.Completion;
            _clients.TryRemove(id, out var _);
        });

        return chan;
    }
}
