namespace TestProjectNet8;

public class AggregatedFeeder
{
    public IEnumerable<FeedItem> Aggregate(IEnumerable<IEnumerable<FeedItem>> venueFeeds)
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
        {
            yield return new FeedItem(kv.Key, kv.Value);
        }
    }
}