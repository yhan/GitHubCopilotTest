using System.IO.MemoryMappedFiles;

public sealed class ShmRingReader : IAsyncDisposable
{
    private readonly string _baseName;
    private readonly MemoryMappedFile _mmf;
    private readonly MemoryMappedViewAccessor _hdr;
    private readonly MemoryMappedViewAccessor _data;
    private readonly Semaphore _items;
    private readonly Semaphore _free;
    private readonly int _headerSize = 32;
    private readonly int _slotSize;
    private readonly int _slotCount;

    // Header offsets (must match writer)
    private const int OFF_MAGIC = 0x00;
    private const int OFF_VER   = 0x04;
    private const int OFF_SLOT  = 0x08;
    private const int OFF_COUNT = 0x0C;
    private const int OFF_HEAD  = 0x10;
    private const int OFF_TAIL  = 0x14;

    public ShmRingReader(string baseName)
    {
        _baseName = baseName;
        _mmf = MemoryMappedFile.OpenExisting(ShmNames.Map(baseName));
        _hdr = _mmf.CreateViewAccessor(0, _headerSize, MemoryMappedFileAccess.ReadWrite);
        int slotSize, slotCount; _hdr.Read(OFF_SLOT, out slotSize); _hdr.Read(OFF_COUNT, out slotCount);
        _slotSize = slotSize; _slotCount = slotCount;
        _data = _mmf.CreateViewAccessor(_headerSize, _slotSize * _slotCount, MemoryMappedFileAccess.ReadWrite);

        _items = Semaphore.OpenExisting(ShmNames.Items(baseName));
        _free  = Semaphore.OpenExisting(ShmNames.Free(baseName));
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

    public bool ReadNext(out byte[] payload, int timeoutMs = Timeout.Infinite)
    {
        payload = Array.Empty<byte>();
        if (!_items.WaitOne(timeoutMs)) return false;

        int tail; _hdr.Read(OFF_TAIL, out tail);
        long offset = (long)tail * _slotSize;
        int len; _data.Read((long)offset + 0, out len);
        if (len < 0 || len > _slotSize - 4) { // corrupt; skip slot
            len = 0;
        }
        var buf = new byte[len];
        if (len > 0)
            _data.ReadArray((long)offset + 4, buf, 0, len);

        // advance tail
        int next = tail + 1; if (next == _slotCount) next = 0;
        _hdr.Write(OFF_TAIL, next);
        _free.Release(1);
        payload = buf;
        return true;
    }
}