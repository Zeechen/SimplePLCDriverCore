using SimplePLCDriverCore.Common.Buffers;

namespace SimplePLCDriverCore.Tests.Buffers;

public class PacketReaderTests
{
    [Fact]
    public void ReadUInt8_ReadsCorrectByte()
    {
        var reader = new PacketReader(new byte[] { 0xAB });
        Assert.Equal(0xAB, reader.ReadUInt8());
        Assert.Equal(1, reader.Position);
    }

    [Fact]
    public void ReadInt8_ReadsSignedByte()
    {
        var reader = new PacketReader(new byte[] { 0xD6 }); // -42
        Assert.Equal(-42, reader.ReadInt8());
    }

    [Fact]
    public void ReadUInt16LE_ReadsLittleEndian()
    {
        var reader = new PacketReader(new byte[] { 0x02, 0x01 });
        Assert.Equal((ushort)0x0102, reader.ReadUInt16LE());
    }

    [Fact]
    public void ReadUInt16BE_ReadsBigEndian()
    {
        var reader = new PacketReader(new byte[] { 0x01, 0x02 });
        Assert.Equal((ushort)0x0102, reader.ReadUInt16BE());
    }

    [Fact]
    public void ReadUInt32LE_ReadsLittleEndian()
    {
        var reader = new PacketReader(new byte[] { 0x04, 0x03, 0x02, 0x01 });
        Assert.Equal(0x01020304U, reader.ReadUInt32LE());
    }

    [Fact]
    public void ReadUInt32BE_ReadsBigEndian()
    {
        var reader = new PacketReader(new byte[] { 0x01, 0x02, 0x03, 0x04 });
        Assert.Equal(0x01020304U, reader.ReadUInt32BE());
    }

    [Fact]
    public void ReadInt32LE_ReadsNegativeValue()
    {
        var reader = new PacketReader(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF });
        Assert.Equal(-1, reader.ReadInt32LE());
    }

    [Fact]
    public void ReadUInt64LE_ReadsCorrectly()
    {
        var reader = new PacketReader(new byte[] { 0x08, 0x07, 0x06, 0x05, 0x04, 0x03, 0x02, 0x01 });
        Assert.Equal(0x0102030405060708UL, reader.ReadUInt64LE());
    }

    [Fact]
    public void ReadSingleLE_ReadsFloat()
    {
        var bytes = BitConverter.GetBytes(3.14f);
        var reader = new PacketReader(bytes);
        Assert.Equal(3.14f, reader.ReadSingleLE());
    }

    [Fact]
    public void ReadDoubleLE_ReadsDouble()
    {
        var bytes = BitConverter.GetBytes(3.14159265358979);
        var reader = new PacketReader(bytes);
        Assert.Equal(3.14159265358979, reader.ReadDoubleLE());
    }

    [Fact]
    public void ReadSingleBE_ReadsFloat()
    {
        // 3.14f in big-endian: 0x4048F5C3
        var bytes = new byte[] { 0x40, 0x48, 0xF5, 0xC3 };
        var reader = new PacketReader(bytes);
        Assert.Equal(3.14f, reader.ReadSingleBE(), 2);
    }

    [Fact]
    public void ReadBytes_ReadsExactCount()
    {
        var reader = new PacketReader(new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05 });
        var result = reader.ReadBytesToArray(3);

        Assert.Equal(new byte[] { 0x01, 0x02, 0x03 }, result);
        Assert.Equal(3, reader.Position);
    }

    [Fact]
    public void ReadAscii_ReadsString()
    {
        var reader = new PacketReader(new byte[] { 0x48, 0x65, 0x6C, 0x6C, 0x6F }); // "Hello"
        Assert.Equal("Hello", reader.ReadAscii(5));
    }

    [Fact]
    public void ReadAsciiTrimmed_TrimsNullTerminators()
    {
        var reader = new PacketReader(new byte[] { 0x48, 0x69, 0x00, 0x00 }); // "Hi\0\0"
        Assert.Equal("Hi", reader.ReadAsciiTrimmed(4));
    }

    [Fact]
    public void Skip_AdvancesPosition()
    {
        var reader = new PacketReader(new byte[] { 0x01, 0x02, 0x03, 0x04 });
        reader.Skip(2);
        Assert.Equal(2, reader.Position);
        Assert.Equal(0x03, reader.ReadUInt8());
    }

    [Fact]
    public void Remaining_ReturnsCorrectCount()
    {
        var reader = new PacketReader(new byte[] { 0x01, 0x02, 0x03, 0x04 });
        Assert.Equal(4, reader.Remaining);
        reader.Skip(1);
        Assert.Equal(3, reader.Remaining);
    }

    [Fact]
    public void PeekUInt8_DoesNotAdvancePosition()
    {
        var reader = new PacketReader(new byte[] { 0xAB });
        Assert.Equal(0xAB, reader.PeekUInt8());
        Assert.Equal(0, reader.Position);
    }

    [Fact]
    public void PeekUInt16LE_DoesNotAdvancePosition()
    {
        var reader = new PacketReader(new byte[] { 0x02, 0x01 });
        Assert.Equal((ushort)0x0102, reader.PeekUInt16LE());
        Assert.Equal(0, reader.Position);
    }

    [Fact]
    public void ReadUInt16LEAt_ReadsAtSpecificOffset()
    {
        var reader = new PacketReader(new byte[] { 0xFF, 0xFF, 0x02, 0x01 });
        Assert.Equal((ushort)0x0102, reader.ReadUInt16LEAt(2));
        Assert.Equal(0, reader.Position); // position unchanged
    }

    [Fact]
    public void HasRemaining_ReturnsTrueWhenDataAvailable()
    {
        var reader = new PacketReader(new byte[] { 0x01 });
        Assert.True(reader.HasRemaining);
        reader.ReadUInt8();
        Assert.False(reader.HasRemaining);
    }

    [Fact]
    public void WriterAndReader_RoundTrip()
    {
        // Write various types
        using var writer = new PacketWriter();
        writer.WriteUInt8(0x42);
        writer.WriteUInt16LE(0x1234);
        writer.WriteUInt32LE(0xDEADBEEF);
        writer.WriteSingleLE(2.718f);
        writer.WriteAscii("CIP");

        // Read them back
        var reader = new PacketReader(writer.ToArray());
        Assert.Equal(0x42, reader.ReadUInt8());
        Assert.Equal((ushort)0x1234, reader.ReadUInt16LE());
        Assert.Equal(0xDEADBEEFU, reader.ReadUInt32LE());
        Assert.Equal(2.718f, reader.ReadSingleLE());
        Assert.Equal("CIP", reader.ReadAscii(3));
        Assert.False(reader.HasRemaining);
    }

    [Fact]
    public void Position_CanBeSetManually()
    {
        var reader = new PacketReader(new byte[] { 0x01, 0x02, 0x03, 0x04 });
        reader.Position = 2;
        Assert.Equal(0x03, reader.ReadUInt8());
    }

    [Fact]
    public void ReadInt16BE_ReadsBigEndian()
    {
        var reader = new PacketReader(new byte[] { 0xFF, 0xFF });
        Assert.Equal((short)-1, reader.ReadInt16BE());
    }

    [Fact]
    public void ReadInt32BE_ReadsBigEndian()
    {
        var reader = new PacketReader(new byte[] { 0x00, 0x00, 0x00, 0x2A });
        Assert.Equal(42, reader.ReadInt32BE());
    }

    [Fact]
    public void ReadUInt64BE_ReadsBigEndian()
    {
        var reader = new PacketReader(new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08 });
        Assert.Equal(0x0102030405060708UL, reader.ReadUInt64BE());
    }

    [Fact]
    public void ReadDoubleBE_ReadsDouble()
    {
        var bytes = BitConverter.GetBytes(3.14);
        if (BitConverter.IsLittleEndian) Array.Reverse(bytes);
        var reader = new PacketReader(bytes);
        Assert.Equal(3.14, reader.ReadDoubleBE(), 10);
    }

    [Fact]
    public void ReadInt16LE_ReadsSignedLittleEndian()
    {
        var bytes = BitConverter.GetBytes((short)-100);
        var reader = new PacketReader(bytes);
        Assert.Equal((short)-100, reader.ReadInt16LE());
    }

    [Fact]
    public void ReadInt64LE_ReadsCorrectly()
    {
        var bytes = BitConverter.GetBytes((long)-42);
        var reader = new PacketReader(bytes);
        Assert.Equal((long)-42, reader.ReadInt64LE());
    }

    [Fact]
    public void ReadBytes_ReadsSpan()
    {
        var reader = new PacketReader(new byte[] { 0x01, 0x02, 0x03, 0x04 });
        var span = reader.ReadBytes(2);
        Assert.Equal(2, span.Length);
        Assert.Equal(0x01, span[0]);
        Assert.Equal(0x02, span[1]);
        Assert.Equal(2, reader.Position);
    }

    [Fact]
    public void PeekUInt32LE_DoesNotAdvancePosition()
    {
        var reader = new PacketReader(new byte[] { 0x04, 0x03, 0x02, 0x01 });
        Assert.Equal(0x01020304U, reader.PeekUInt32LE());
        Assert.Equal(0, reader.Position);
    }

    [Fact]
    public void ReadUInt8At_ReadsAtOffset()
    {
        var reader = new PacketReader(new byte[] { 0xAA, 0xBB, 0xCC });
        Assert.Equal(0xBB, reader.ReadUInt8At(1));
        Assert.Equal(0, reader.Position);
    }

    [Fact]
    public void ReadUInt32LEAt_ReadsAtOffset()
    {
        var reader = new PacketReader(new byte[] { 0xFF, 0xFF, 0x04, 0x03, 0x02, 0x01 });
        Assert.Equal(0x01020304U, reader.ReadUInt32LEAt(2));
        Assert.Equal(0, reader.Position);
    }

    [Fact]
    public void Length_ReturnsBufferLength()
    {
        var reader = new PacketReader(new byte[] { 0x01, 0x02, 0x03 });
        Assert.Equal(3, reader.Length);
    }

    [Fact]
    public void RemainingSpan_ReturnsCorrectSlice()
    {
        var reader = new PacketReader(new byte[] { 0x01, 0x02, 0x03, 0x04 });
        reader.Skip(2);
        var remaining = reader.RemainingSpan;
        Assert.Equal(2, remaining.Length);
        Assert.Equal(0x03, remaining[0]);
    }

    [Fact]
    public void Slice_ReturnsCorrectSlice()
    {
        var reader = new PacketReader(new byte[] { 0x01, 0x02, 0x03, 0x04 });
        reader.Skip(1);
        var slice = reader.Slice(2);
        Assert.Equal(2, slice.Length);
        Assert.Equal(0x02, slice[0]);
        Assert.Equal(0x03, slice[1]);
    }

    [Fact]
    public void Constructor_FromReadOnlyMemory()
    {
        ReadOnlyMemory<byte> mem = new byte[] { 0x42 };
        var reader = new PacketReader(mem);
        Assert.Equal(0x42, reader.ReadUInt8());
    }
}
