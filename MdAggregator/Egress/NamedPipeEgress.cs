// fast local IPC, Windows & Linux in .NET
using System.IO.Pipes;
using System.Text.Json;
using System.Threading.Channels;

public sealed class NamedPipeEgress : IEgressPublisher
{
    private readonly string _pipeName;
    private readonly EgressPolicy _policy;

    public NamedPipeEgress(string pipeName, EgressPolicy policy)
    {
        _pipeName = pipeName;
        _policy = policy;
    }

    public async Task StartAsync(IAsyncEnumerable<AggEvent> source, CancellationToken ct)
    {
        Console.WriteLine($"[Pipe] Serving on '{_pipeName}'");
        _ = Task.Run(() => AcceptLoop(ct), ct);

        try { await Task.Delay(Timeout.Infinite, ct); }
        catch (OperationCanceledException) { }
    }

    private async Task AcceptLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var server = new NamedPipeServerStream(_pipeName, PipeDirection.Out, NamedPipeServerStream.MaxAllowedServerInstances,
                                                   PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            await server.WaitForConnectionAsync(ct);

            var writer = EgressHub.RegisterClient(CreateClientChannel(_policy).Writer, _policy);

            _ = Task.Run(async () =>
            {
                var sw = new StreamWriter(server) { AutoFlush = true };
                var reader = writer.Reader; // ChannelReader via small helper
                try
                {
                    while (await reader.WaitToReadAsync(ct))
                    {
                        while (reader.TryRead(out var ev))
                        {
                            var json = JsonSerializer.Serialize(ev);
                            await sw.WriteLineAsync(json.AsMemory(), ct);
                        }
                    }
                }
                catch (OperationCanceledException) { }
                catch (IOException) { }
                finally
                {
                    try { server.Dispose(); } catch { }
                }
            }, ct);
        }
    }

    private static Channel<AggEvent> CreateClientChannel(EgressPolicy policy)
    {
        var opts = new BoundedChannelOptions(policy.BoundedCapacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = policy.BackpressureMode == BackpressureMode.DropOldest
                ? BoundedChannelFullMode.DropOldest
                : BoundedChannelFullMode.DropWrite
        };
        return Channel.CreateBounded<AggEvent>(opts);
    }

    public ValueTask DisposeAsync() => default;
}

// Small helper to expose ChannelReader from a writer registration
internal static class ChannelWriterExtensions
{
    public static (ChannelWriter<AggEvent> Writer, ChannelReader<AggEvent> Reader) Reader(this ChannelWriter<AggEvent> writer)
        => throw new NotSupportedException("This is conceptual; we return the paired reader in EgressHub.RegisterClient.");
}
