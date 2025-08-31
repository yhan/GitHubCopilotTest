public sealed class CompositeEgressPublisher : IEgressPublisher
{
    private readonly IReadOnlyList<IEgressPublisher> _backends;

    public CompositeEgressPublisher(IEnumerable<IEgressPublisher> backends) =>
        _backends = backends.ToList();

    public async Task StartAsync(IAsyncEnumerable<AggEvent> source, CancellationToken ct)
    {
        // Start all backends in parallel
        var tasks = _backends.Select(b => b.StartAsync(source, ct)).ToArray();
        await Task.WhenAll(tasks);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var b in _backends)
            await b.DisposeAsync();
    }
}