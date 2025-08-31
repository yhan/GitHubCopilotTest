namespace TestProjectNet8;

public class AggregatedFeederTests
{
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