using System.Collections.Concurrent;

namespace TestProjectNet8;

public class AggregatedFeeder
{
    private readonly ConcurrentDictionary<decimal, decimal> _map = new();

    // Feed a single item from a network/thread-pool thread.
    public void Feed(FeedItem? item)
    {
        if (item is null) return;
        _map.AddOrUpdate(item.Price, item.Size, (_, existing) => existing + item.Size);
    }

    // Feed multiple items for a venue (called by that venue's network thread).
    public void FeedMany(IEnumerable<FeedItem>? items)
    {
        if (items is null) return;
        foreach (var item in items) Feed(item);
    }

    // Get a snapshot of aggregated feed items ordered by price.
    public IReadOnlyList<FeedItem> Snapshot()
    {
        return _map
            .OrderBy(kv => kv.Key)
            .Select(kv => new FeedItem(kv.Key, kv.Value))
            .ToList();
    }

    // Clear current aggregation.
    public void Clear() => _map.Clear();

    // Legacy / convenience: aggregate static collections (no threading).
    public static IEnumerable<FeedItem> Aggregate(IEnumerable<IEnumerable<FeedItem>>? venueFeeds)
    {
        if (venueFeeds == null) yield break;

        var map = new Dictionary<decimal, decimal>();

        foreach (var feed in venueFeeds)
        {
            if (feed == null) continue;
            foreach (var item in feed)
            {
                if (item == null) continue;
                if (map.ContainsKey(item.Price)) map[item.Price] += item.Size;
                else map[item.Price] = item.Size;
            }
        }

        foreach (var kv in map.OrderBy(kv => kv.Key))
            yield return new FeedItem(kv.Key, kv.Value);
    }
}