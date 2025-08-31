using System.Buffers.Binary;
using System.Text;


// ===========================
// Binary encoding for AggBook ? byte[]
// ===========================
public static class AggBookCodec
{
    // Wire format (little?endian):
    // [u8 msgType=1]
    // [u16 symLen][sym bytes ASCII]
    // [i64 publishTs]
    // [u8 bids][u8 asks]
    // bids * ([i32 pxTicks][i32 size]) then asks * (...)
    // [u8 crossed]

    public static int EncodeAggBook(Span<byte> dst, AggBook b)
    {
        int needed = 1 + 2 + b.CanonicalSymbol.Length + 8 + 1 + 1 + (b.Bids.Length + b.Asks.Length) * 8 + 1;
        if (dst.Length < needed) return -needed; // negative => need this many bytes

        int o = 0;
        dst[o++] = 0x01; // msg type
        BinaryPrimitives.WriteUInt16LittleEndian(dst[o..], (ushort)b.CanonicalSymbol.Length); o += 2;
        o += Encoding.ASCII.GetBytes(b.CanonicalSymbol, dst[o..]);
        BinaryPrimitives.WriteInt64LittleEndian(dst[o..], b.PublishTs); o += 8;
        dst[o++] = (byte)b.Bids.Length;
        dst[o++] = (byte)b.Asks.Length;
        for (int i=0;i<b.Bids.Length;i++) { BinaryPrimitives.WriteInt32LittleEndian(dst[o..], b.Bids[i].PriceTicks); o+=4; BinaryPrimitives.WriteInt32LittleEndian(dst[o..], b.Bids[i].Size); o+=4; }
        for (int i=0;i<b.Asks.Length;i++) { BinaryPrimitives.WriteInt32LittleEndian(dst[o..], b.Asks[i].PriceTicks); o+=4; BinaryPrimitives.WriteInt32LittleEndian(dst[o..], b.Asks[i].Size); o+=4; }
        dst[o++] = b.IsCrossedOrLocked ? (byte)1 : (byte)0;
        return o;
    }
}