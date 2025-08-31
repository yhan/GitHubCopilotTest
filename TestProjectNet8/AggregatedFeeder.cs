
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace TestProjectNet8;

public class AggregatedFeeder : IDisposable
{
    private readonly Dictionary<string, Dictionary<decimal, decimal>> _venues = new();
    private readonly SortedDictionary<decimal, decimal> _aggregate = new();
    private readonly ReaderWriterLockSlim _lock = new(LockRecursionPolicy.NoRecursion);
    private volatile IReadOnlyList<FeedItem> _cachedSnapshot = Array.Empty<FeedItem>();
    private bool _disposed;

    // Replace entire venue snapshot (call from that venue's network thread).
    public void FeedVenue(string venueId, IEnumerable<FeedItem>? items)
    {
        if (venueId is null) throw new ArgumentNullException(nameof(venueId));

        // Build new venue map from incoming snapshot (already a snapshot, so just aggregate duplicates)
        var newVenue = new Dictionary<decimal, decimal>();
        if (items != null)
        {
            foreach (var item in items)
            {
                if (item is null) continue;
                if (item.Size <= 0m) continue; // ignore zero/negative sizes in a full snapshot
