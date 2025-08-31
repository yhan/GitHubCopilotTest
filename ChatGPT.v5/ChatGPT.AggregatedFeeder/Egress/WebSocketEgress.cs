// WebSocketEgress.cs (self-contained, no external packages)

using System.Net;
using System.Net.WebSockets;
using System.Text.Json;
using System.Threading.Channels;

public sealed class WebSocketEgress : IEgressPublisher
{
    private readonly HttpListener _http = new();
    private readonly EgressPolicy _policy;

    public WebSocketEgress(string httpPrefix, EgressPolicy policy)
    {
        // e.g. "http://localhost:5000/ws/"
        _http.Prefixes.Add(httpPrefix);
        _policy = policy;
    }

    public async Task StartAsync(IAsyncEnumerable<AggEvent> source, CancellationToken ct)
    {
        _http.Start();
        Console.WriteLine($"[WS] Listening on {string.Join(", ", _http.Prefixes)}");

        // Broadcast loop: each client has its own bounded buffer
        _ = Task.Run(() => AcceptLoop(ct), ct);

        // Keep the method alive until cancelled
        try { await Task.Delay(Timeout.Infinite, ct); }
        catch (OperationCanceledException) { /* normal */ }
    }

    private async Task AcceptLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var ctx = await _http.GetContextAsync();
            if (!ctx.Request.IsWebSocketRequest)
            {
                ctx.Response.StatusCode = 400; ctx.Response.Close();
                continue;
            }

            var wsCtx = await ctx.AcceptWebSocketAsync(null);
            var ws = wsCtx.WebSocket;

            // Each connection gets a bounded channel
            var chan = CreateClientChannel(_policy);

            // Pump aggregator events into this client’s channel
            var pumpCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _ = Task.Run(() => ClientSendLoop(ws, chan.Reader, pumpCts.Token), pumpCts.Token);

            // Optional: read subscription updates from client (simple keepalive here)
            _ = Task.Run(() => ClientReceiveLoop(ws, pumpCts), pumpCts.Token);

            // Attach to the shared bus: we tee from a global source below
            // NOTE: We can’t access the aggregator here; so expose a static registrar:
            EgressHub.RegisterClient(chan.Writer, _policy);
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

    private static async Task ClientSendLoop(WebSocket ws, ChannelReader<AggEvent> reader, CancellationToken ct)
    {
        try
        {
            AggEvent? last = null;
            var lastSentAt = DateTimeOffset.MinValue;
            var conflate = EgressHub.Policy.ConflateInterval;

            while (await reader.WaitToReadAsync(ct))
            {
                while (reader.TryRead(out var ev))
                {
                    // Simple conflation: if set, only send at most once per interval
                    if (conflate is not null)
                    {
                        last = ev;
                        var now = DateTimeOffset.UtcNow;
                        if (now - lastSentAt < conflate.Value) continue;
                        ev = last;
                        last = null;
                        lastSentAt = now;
                    }

                    if (ws.State != WebSocketState.Open) return;

                    var json = JsonSerializer.Serialize(ev);
                    var buf = System.Text.Encoding.UTF8.GetBytes(json);
                    await ws.SendAsync(buf, WebSocketMessageType.Text, true, ct);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException) { }
        finally
        {
            try { await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None); } catch { }
        }
    }

    private static async Task ClientReceiveLoop(WebSocket ws, CancellationTokenSource pumpCts)
    {
        var buf = new byte[1024];
        try
        {
            while (ws.State == WebSocketState.Open)
            {
                var res = await ws.ReceiveAsync(buf, pumpCts.Token);
                if (res.MessageType == WebSocketMessageType.Close) break;
                // If you want: parse a subscription message to set per-client symbols.
            }
        }
        catch { /* ignore */ }
        finally
        {
            pumpCts.Cancel();
        }
    }

    public ValueTask DisposeAsync()
    {
        _http.Close();
        return default;
    }
}
