namespace TestProjectNet8;

public class AggregatedFeederTests
{
    [Test]
    public void AggregatesSizesAcrossVenues_ForSamePrice()
    {
        var venue1 = new[] { new FeedItem(100m, 5m), new FeedItem(101m, 2m) };
        var venue2 = new[] { new FeedItem(100m, 3m), new FeedItem(102m, 4m) };

        var feeder = new AggregatedFeeder();
        var result = feeder.Aggregate(new[] { venue1, venue2 }).ToList();

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
        var feeder = new AggregatedFeeder();
        var result = feeder.Aggregate(new List<IEnumerable<FeedItem>>()).ToList();
        Assert.That(result, Is.Empty);
    }
}