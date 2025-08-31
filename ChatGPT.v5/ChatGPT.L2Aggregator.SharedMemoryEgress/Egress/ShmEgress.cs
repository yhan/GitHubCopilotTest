
//  Sample reader process (attach from another app)
public sealed class ShmEgress : IEgressPublisher
{
    private readonly string _ringName;
    private readonly int _slotSize;
    private readonly int _slotCount;
    private ShmRingWriter? _writer;

    public ShmEgress(string ringName, int slotSize = 2048, int slotCount = 8192)
    { _ringName = ringName; _slotSize = slotSize; _slotCount = slotCount; }

    public async Task StartAsync(IAsyncEnumerable<AggBook> source, CancellationToken ct)
    {
        _writer = new ShmRingWriter(_ringName, _slotSize, _slotCount);
        var buf = new byte[_slotSize-4];

        await foreach (var b in source.WithCancellation(ct))
        {
            int n = AggBookCodec.EncodeAggBook(buf, b);
            if (n < 0) continue; // too large for slot; drop
            _writer.TryWrite(buf.AsSpan(0, n), timeoutMs: 100); // backpressure: wait a bit, drop if busy
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_writer is not null) await _writer.DisposeAsync();
    }

}