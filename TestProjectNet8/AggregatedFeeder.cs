namespace TestProjectNet8;

public class AggregatedFeeder : IDisposable
{
    private readonly SortedDictionary<decimal, decimal> _map = new();

    private readonly System.Threading.ReaderWriterLockSlim _lock =
        new(System.Threading.LockRecursionPolicy.NoRecursion);

    private bool _disposed;

    public void Feed(FeedItem? item)
    {
        if (item is null) return;
        _lock.EnterWriteLock();
        try
        {
            if (_map.ContainsKey(item.Price)) _map[item.Price] += item.Size;
            else _map[item.Price] = item.Size;
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    public void FeedMany(IEnumerable<FeedItem>? items)
    {
        if (items is null) return;
        _lock.EnterWriteLock();
        try
        {
            foreach (var item in items)
            {
                if (item is null) continue;
                if (_map.ContainsKey(item.Price)) _map[item.Price] += item.Size;
                else _map[item.Price] = item.Size;
            }
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    public IReadOnlyList<FeedItem> Snapshot()
    {
        _lock.EnterReadLock();
        try
        {
            var list = new List<FeedItem>(_map.Count);
            foreach (var kv in _map) list.Add(new FeedItem(kv.Key, kv.Value));
            return list;
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    public void Clear()
    {
        _lock.EnterWriteLock();
        try
        {
            _map.Clear();
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _lock.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}