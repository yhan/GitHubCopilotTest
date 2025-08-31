namespace TestProjectNet8;

public class AggregatedFeederTests
{
    [Test]
    public void AggregatesSizesAcrossVenues_ForSamePrice_StaticAggregate()
    {
        var venue1 = new[] { new FeedItem(100m, 5m), new FeedItem(101m, 2m) };
        var venue2 = new[] { new FeedItem(100m, 3m), new FeedItem(102m, 4m) };

        var result = AggregatedFeeder.Aggregate(new[] { venue1, venue2 }).ToList();

        var expected = new List<FeedItem>
        {
            new FeedItem(100m, 8m), // 5 + 3
            new FeedItem(101m, 2m),
            new FeedItem(102m, 4m)
        };

        Assert.That(result, Is.EqualTo(expected));
    }

    [Test]
    public void Aggregate_EmptyFeeds_ReturnsEmpty()
    {
        var result = AggregatedFeeder.Aggregate(new List<IEnumerable<FeedItem>>()).ToList();
        Assert.That(result, Is.Empty);
    }

    [Test]
    public void FeedMany_and_Snapshot_AreThreadSafe()
    {
        var feeder = new AggregatedFeeder();

        var venue1 = new[] { new FeedItem(100m, 5m), new FeedItem(101m, 2m) };
        var venue2 = new[] { new FeedItem(100m, 3m), new FeedItem(102m, 4m) };

        // Simulate network threads feeding concurrently
        var t1 = Task.Run(() => feeder.FeedMany(venue1));
        var t2 = Task.Run(() => feeder.FeedMany(venue2));
        Task.WaitAll(t1, t2);

        var snapshot = feeder.Snapshot().ToList();

        var expected = new List<FeedItem>
        {
            new FeedItem(100m, 8m),
            new FeedItem(101m, 2m),
            new FeedItem(102m, 4m)
        };

        Assert.That(snapshot, Is.EqualTo(expected));
    }
}