using System.Buffers.Binary;
using System.Text;

public static class ShmReaderSample
{
    public static async Task RunAsync(string ringName, int scale, CancellationToken ct)
    {
        await using var reader = new ShmRingReader(ringName);
        var tmp = new byte[4096];
        while (!ct.IsCancellationRequested)
        {
            if (!reader.ReadNext(out var payload, timeoutMs: 1000)) continue;
            // very small, parse minimally
            var span = payload.AsSpan(); int o=0;
            byte type = span[o++]; if (type != 0x01) continue;
            ushort symLen = BinaryPrimitives.ReadUInt16LittleEndian(span[o..]); o+=2;
            string sym = Encoding.ASCII.GetString(span.Slice(o, symLen)); o+=symLen;
            long ts = BinaryPrimitives.ReadInt64LittleEndian(span[o..]); o+=8;
            byte nb = span[o++]; byte na = span[o++];
            Console.Write($"{sym} B:");
            for (int i=0;i<nb;i++)
            {
                int px = BinaryPrimitives.ReadInt32LittleEndian(span[o..]); o+=4;
                int sz = BinaryPrimitives.ReadInt32LittleEndian(span[o..]); o+=4;
                Console.Write($" {(px/(decimal)scale):F2}x{sz}");
            }
            Console.Write(" A:");
            for (int i=0;i<na;i++)
            {
                int px = BinaryPrimitives.ReadInt32LittleEndian(span[o..]); o+=4;
                int sz = BinaryPrimitives.ReadInt32LittleEndian(span[o..]); o+=4;
                Console.Write($" {(px/(decimal)scale):F2}x{sz}");
            }
            bool crossed = span[o++]!=0;
            if (crossed) Console.Write(" [LOCK/CRS]");
            Console.WriteLine();
        }
    }
}