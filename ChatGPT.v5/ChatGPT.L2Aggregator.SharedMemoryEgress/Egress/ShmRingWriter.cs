using System.IO.MemoryMappedFiles;

public sealed class ShmRingWriter : IAsyncDisposable
{
    private readonly string _baseName;
    private readonly int _slotSize;
    private readonly int _slotCount;
    private readonly MemoryMappedFile _mmf;
    private readonly MemoryMappedViewAccessor _hdr;
    private readonly MemoryMappedViewAccessor _data;
    private readonly Semaphore _items;
    private readonly Semaphore _free;
    private readonly int _headerSize = 32;

    // Header offsets
    private const int OFF_MAGIC = 0x00;   // int
    private const int OFF_VER   = 0x04;   // int
    private const int OFF_SLOT  = 0x08;   // int
    private const int OFF_COUNT = 0x0C;   // int
    private const int OFF_HEAD  = 0x10;   // int (producer index)
    private const int OFF_TAIL  = 0x14;   // int (consumer index)

    public ShmRingWriter(string baseName, int slotSize = 1024, int slotCount = 4096)
    {
        if (slotSize < 8) throw new ArgumentOutOfRangeException(nameof(slotSize));
        _baseName = baseName;
        _slotSize = slotSize;
        _slotCount = slotCount;

        var totalSize = _headerSize + _slotSize * _slotCount;
        bool created;
        using var initMtx = new Mutex(false, ShmNames.InitMutex(baseName), out created);

        _mmf = MemoryMappedFile.CreateOrOpen(ShmNames.Map(baseName), totalSize);
        _hdr = _mmf.CreateViewAccessor(0, _headerSize, MemoryMappedFileAccess.ReadWrite);
        _data = _mmf.CreateViewAccessor(_headerSize, _slotSize * _slotCount, MemoryMappedFileAccess.ReadWrite);

        // Semaphores: free starts at slotCount, items at 0 when creating fresh
        if (!Semaphore.TryOpenExisting(ShmNames.Free(baseName), out _free))
            _free = new Semaphore(slotCount, slotCount, ShmNames.Free(baseName));
        if (!Semaphore.TryOpenExisting(ShmNames.Items(baseName), out _items))
            _items = new Semaphore(0, slotCount, ShmNames.Items(baseName));

        // Initialize header once
        initMtx.WaitOne();
        try
        {
            int magic; _hdr.Read(OFF_MAGIC, out magic);
            if (magic != 0x53484D52) // 'SHMR'
            {
                _hdr.Write(OFF_MAGIC, 0x53484D52);
                _hdr.Write(OFF_VER, 1);
                _hdr.Write(OFF_SLOT, _slotSize);
                _hdr.Write(OFF_COUNT, _slotCount);
                _hdr.Write(OFF_HEAD, 0);
                _hdr.Write(OFF_TAIL, 0);
            }
        }
        finally { initMtx.ReleaseMutex(); }
    }

    public async ValueTask DisposeAsync()
    {
        _hdr.Dispose();
        _data.Dispose();
        _mmf.Dispose();
        _items.Dispose();
        _free.Dispose();
        await Task.CompletedTask;
    }

    public bool TryWrite(ReadOnlySpan<byte> payload, int timeoutMs = Timeout.Infinite)
    {
        if (payload.Length > _slotSize - 4) return false; // too big
        // Wait for a free slot
        if (!_free.WaitOne(timeoutMs)) return false;

        // Read and advance head (producer index)
        int head; _hdr.Read(OFF_HEAD, out head);
        int next = head + 1; if (next == _slotCount) next = 0;

        // Write into slot 'head'
        long offset = (long)head * _slotSize;
        _data.Write((long)offset + 0, payload.Length);
        _data.WriteArray((long)offset + 4, payload.ToArray(), 0, payload.Length);

        // Publish: update head then signal items
        _hdr.Write(OFF_HEAD, next);
        _items.Release(1);
        return true;
    }
}