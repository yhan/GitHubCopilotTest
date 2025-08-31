// KafkaEgress.cs(lightweight stub you can wire to Confluent.Kafka)
using System.Text.Json;

public sealed class KafkaEgress : IEgressPublisher
{
    private readonly string _topic;
    private readonly EgressPolicy _policy;

    public KafkaEgress(string topic, EgressPolicy policy)
    {
        _topic = topic;
        _policy = policy;
    }

    public async Task StartAsync(IAsyncEnumerable<AggEvent> source, CancellationToken ct)
    {
        await foreach (var ev in source.WithCancellation(ct))
        {
            // Serialize and send
            var bytes = System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(ev));
            // TODO: producer.Produce(_topic, new Message<Null, byte[]> { Value = bytes });
            // For demo:
            _ = bytes;
            _ = _topic;
        }
    }

    public ValueTask DisposeAsync() => default;
}