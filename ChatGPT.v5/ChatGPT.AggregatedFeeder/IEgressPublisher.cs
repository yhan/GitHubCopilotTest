public interface IEgressPublisher : IAsyncDisposable
{
    /// <summary>Start serving clients. Non-blocking; long-running loop inside.</summary>
    Task StartAsync(IAsyncEnumerable<AggEvent> source, CancellationToken ct);
}