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

        var newVenue = new Dictionary<decimal, decimal>();
        if (items != null)
        {
            foreach (var item in items)
            {
                if (item is null) continue;
                if (item.Size <= 0) continue; // ignore zero/negative sizes in a full snapshot
                if (newVenue.ContainsKey(item.Price)) newVenue[item.Price] += item.Size;
                else newVenue[item.Price] = item.Size;
            }
        }

        _lock.EnterWriteLock();
        try
        {
            // subtract old venue contributions
            if (_venues.TryGetValue(venueId, out var oldVenue) && oldVenue != null)
            {
                foreach (var kv in oldVenue)
                {
                    if (_aggregate.TryGetValue(kv.Key, out var agg))
                    {
                        var updated = agg - kv.Value;
                        if (updated <= 0) _aggregate.Remove(kv.Key);
                        else _aggregate[kv.Key] = updated;
                    }
                }
            }

            // add new venue contributions
            if (newVenue.Count > 0)
            {
                foreach (var kv in newVenue)
                {
                    if (_aggregate.ContainsKey(kv.Key)) _aggregate[kv.Key] += kv.Value;
                    else _aggregate[kv.Key] = kv.Value;
                }

                _venues[venueId] = newVenue;
            }
            else
            {
                // no items -> remove venue entry
                _venues.Remove(venueId);
            }

            RebuildCachedSnapshot();
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    // Apply a small delta (can be positive or negative). Not tied to a venue.
    public void FeedDelta(FeedItem? delta)
    {
        if (delta is null) return;
        if (delta.Size == 0m) return;

        _lock.EnterWriteLock();
        try
        {
            if (_aggregate.TryGetValue(delta.Price, out var existing))
            {
                var updated = existing + delta.Size;
                if (updated <= 0m) _aggregate.Remove(delta.Price);
                else _aggregate[delta.Price] = updated;
            }
            else
            {
                if (delta.Size > 0m) _aggregate[delta.Price] = delta.Size;
                // negative delta for missing price is ignored
            }

            RebuildCachedSnapshot();
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    // Remove a specific venue entirely.
    public void ClearVenue(string venueId)
    {
        if (venueId is null) throw new ArgumentNullException(nameof(venueId));

        _lock.EnterWriteLock();
        try
        {
            if (_venues.TryGetValue(venueId, out var oldVenue) && oldVenue != null)
            {
                foreach (var kv in oldVenue)
                {
                    if (_aggregate.TryGetValue(kv.Key, out var agg))
                    {
                        var updated = agg - kv.Value;
                        if (updated <= 0m) _aggregate.Remove(kv.Key);
                        else _aggregate[kv.Key] = updated;
                    }
                }

                _venues.Remove(venueId);
                RebuildCachedSnapshot();
            }
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    // Clear everything.
    public void ClearAll()
    {
        _lock.EnterWriteLock();
        try
        {
            _venues.Clear();
            _aggregate.Clear();
            _cachedSnapshot = Array.Empty<FeedItem>();
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    // Fast read: returns cached, already ordered snapshot with no iteration/allocation.
    public IReadOnlyList<FeedItem> Snapshot() => _cachedSnapshot;

    private void RebuildCachedSnapshot()
    {
        // _aggregate is already sorted by price
        var list = new List<FeedItem>(_aggregate.Count);
        foreach (var kv in _aggregate) list.Add(new FeedItem(kv.Key, kv.Value));
        _cachedSnapshot = list.AsReadOnly();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _lock.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}