using System.IO.Pipes;
using System.Text;

public sealed class PipeEgress : IAsyncDisposable
{
    private readonly string _pipeName;
    private NamedPipeServerStream? _server;
    public PipeEgress(string pipeName){ _pipeName=pipeName; }

    public async Task StartAsync(IAsyncEnumerable<AggBook> source, CancellationToken ct)
    {
        _server = new NamedPipeServerStream(_pipeName, PipeDirection.Out, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 65536, 65536);
        await _server.WaitForConnectionAsync(ct);
        var stream = _server;
        var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen:true);

        await foreach (var b in source.WithCancellation(ct))
        {
            // Framing: [symbolLen][symbol][ts][nb][na][pairs...]
            var symBytes = Encoding.ASCII.GetBytes(b.CanonicalSymbol);
            writer.Write((ushort)symBytes.Length); writer.Write(symBytes);
            writer.Write(b.PublishTs);
            writer.Write((byte)b.Bids.Length);
            writer.Write((byte)b.Asks.Length);
            for (int i=0;i<b.Bids.Length;i++){ writer.Write(b.Bids[i].PriceTicks); writer.Write(b.Bids[i].Size); }
            for (int i=0;i<b.Asks.Length;i++){ writer.Write(b.Asks[i].PriceTicks); writer.Write(b.Asks[i].Size); }
            writer.Write(b.IsCrossedOrLocked);
            writer.Flush();
        }
    }

    public ValueTask DisposeAsync(){ try{ _server?.Dispose(); } catch{} return default; }
}