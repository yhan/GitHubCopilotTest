using System.Net;
using System.Net.WebSockets;
using System.Text.Json;

public class WebSocketEgress
{
    private readonly HttpListener _http;
    private readonly AggregatedFeeder _aggregator;

    public WebSocketEgress(string prefix, AggregatedFeeder aggregator)
    {
        _http = new HttpListener();
        _http.Prefixes.Add(prefix); // e.g. "http://localhost:5000/ws/"
        _aggregator = aggregator;
    }

    public async Task StartAsync(IEnumerable<string> symbols, CancellationToken ct)
    {
        _http.Start();
        Console.WriteLine("WebSocket server listening...");

        while (!ct.IsCancellationRequested)
        {
            var ctx = await _http.GetContextAsync();
            if (ctx.Request.IsWebSocketRequest)
            {
                var ws = (await ctx.AcceptWebSocketAsync(null)).WebSocket;
                _ = HandleClient(ws, symbols, ct);
            }
            else
            {
                ctx.Response.StatusCode = 400;
                ctx.Response.Close();
            }
        }
    }

    private async Task HandleClient(WebSocket ws, IEnumerable<string> symbols, CancellationToken ct)
    {
        Console.WriteLine("Client connected");
        await foreach (var ev in _aggregator.StreamAsync(symbols, ct))
        {
            if (ws.State != WebSocketState.Open) break;
            var json = JsonSerializer.Serialize(ev);
            var buf = System.Text.Encoding.UTF8.GetBytes(json);
            await ws.SendAsync(buf, WebSocketMessageType.Text, true, ct);
        }

        Console.WriteLine("Client disconnected");
    }
}