using SimplePLCDriverCore.Common.Buffers;

namespace SimplePLCDriverCore.Tests.Buffers;

public class PacketWriterTests
{
    [Fact]
    public void WriteUInt8_WritesCorrectByte()
    {
        using var writer = new PacketWriter();
        writer.WriteUInt8(0xAB);

        var result = writer.ToArray();
        Assert.Single(result);
        Assert.Equal(0xAB, result[0]);
    }

    [Fact]
    public void WriteInt8_WritesSignedByte()
    {
        using var writer = new PacketWriter();
        writer.WriteInt8(-42);

        var result = writer.ToArray();
        Assert.Single(result);
        Assert.Equal((byte)(-42 & 0xFF), result[0]);
    }

    [Fact]
    public void WriteUInt16LE_WritesLittleEndian()
    {
        using var writer = new PacketWriter();
        writer.WriteUInt16LE(0x0102);

        var result = writer.ToArray();
        Assert.Equal(2, result.Length);
        Assert.Equal(0x02, result[0]); // low byte first
        Assert.Equal(0x01, result[1]); // high byte second
    }

    [Fact]
    public void WriteUInt16BE_WritesBigEndian()
    {
        using var writer = new PacketWriter();
        writer.WriteUInt16BE(0x0102);

        var result = writer.ToArray();
        Assert.Equal(2, result.Length);
        Assert.Equal(0x01, result[0]); // high byte first
        Assert.Equal(0x02, result[1]); // low byte second
    }

    [Fact]
    public void WriteUInt32LE_WritesLittleEndian()
    {
        using var writer = new PacketWriter();
        writer.WriteUInt32LE(0x01020304);

        var result = writer.ToArray();
        Assert.Equal(4, result.Length);
        Assert.Equal(0x04, result[0]);
        Assert.Equal(0x03, result[1]);
        Assert.Equal(0x02, result[2]);
        Assert.Equal(0x01, result[3]);
    }

    [Fact]
    public void WriteUInt32BE_WritesBigEndian()
    {
        using var writer = new PacketWriter();
        writer.WriteUInt32BE(0x01020304);

        var result = writer.ToArray();
        Assert.Equal(4, result.Length);
        Assert.Equal(0x01, result[0]);
        Assert.Equal(0x02, result[1]);
        Assert.Equal(0x03, result[2]);
        Assert.Equal(0x04, result[3]);
    }

    [Fact]
    public void WriteInt32LE_WritesNegativeValue()
    {
        using var writer = new PacketWriter();
        writer.WriteInt32LE(-1);

        var result = writer.ToArray();
        Assert.Equal(4, result.Length);
        Assert.All(result, b => Assert.Equal(0xFF, b)); // -1 = 0xFFFFFFFF
    }

    [Fact]
    public void WriteUInt64LE_WritesCorrectly()
    {
        using var writer = new PacketWriter();
        writer.WriteUInt64LE(0x0102030405060708UL);

        var result = writer.ToArray();
        Assert.Equal(8, result.Length);
        Assert.Equal(0x08, result[0]);
        Assert.Equal(0x07, result[1]);
        Assert.Equal(0x06, result[2]);
        Assert.Equal(0x05, result[3]);
        Assert.Equal(0x04, result[4]);
        Assert.Equal(0x03, result[5]);
        Assert.Equal(0x02, result[6]);
        Assert.Equal(0x01, result[7]);
    }

    [Fact]
    public void WriteSingleLE_WritesFloat()
    {
        using var writer = new PacketWriter();
        writer.WriteSingleLE(3.14f);

        var result = writer.ToArray();
        Assert.Equal(4, result.Length);
        Assert.Equal(3.14f, BitConverter.ToSingle(result, 0));
    }

    [Fact]
    public void WriteDoubleLE_WritesDouble()
    {
        using var writer = new PacketWriter();
        writer.WriteDoubleLE(3.14159265358979);

        var result = writer.ToArray();
        Assert.Equal(8, result.Length);
        Assert.Equal(3.14159265358979, BitConverter.ToDouble(result, 0));
    }

    [Fact]
    public void WriteBytes_WritesRawBytes()
    {
        using var writer = new PacketWriter();
        writer.WriteBytes(new byte[] { 0x01, 0x02, 0x03 });

        var result = writer.ToArray();
        Assert.Equal(new byte[] { 0x01, 0x02, 0x03 }, result);
    }

    [Fact]
    public void WriteZeros_WritesZeroBytes()
    {
        using var writer = new PacketWriter();
        writer.WriteUInt8(0xFF);
        writer.WriteZeros(3);
        writer.WriteUInt8(0xFF);

        var result = writer.ToArray();
        Assert.Equal(new byte[] { 0xFF, 0x00, 0x00, 0x00, 0xFF }, result);
    }

    [Fact]
    public void WriteAscii_WritesStringBytes()
    {
        using var writer = new PacketWriter();
        writer.WriteAscii("AB");

        var result = writer.ToArray();
        Assert.Equal(new byte[] { 0x41, 0x42 }, result);
    }

    [Fact]
    public void WriteAsciiPadded_PadsOddLengthString()
    {
        using var writer = new PacketWriter();
        writer.WriteAsciiPadded("ABC"); // odd length, needs padding

        var result = writer.ToArray();
        Assert.Equal(4, result.Length);
        Assert.Equal(new byte[] { 0x41, 0x42, 0x43, 0x00 }, result);
    }

    [Fact]
    public void WriteAsciiPadded_DoesNotPadEvenLengthString()
    {
        using var writer = new PacketWriter();
        writer.WriteAsciiPadded("ABCD"); // even length, no padding

        var result = writer.ToArray();
        Assert.Equal(4, result.Length);
    }

    [Fact]
    public void Length_TracksWrittenBytes()
    {
        using var writer = new PacketWriter();
        Assert.Equal(0, writer.Length);

        writer.WriteUInt16LE(0);
        Assert.Equal(2, writer.Length);

        writer.WriteUInt32LE(0);
        Assert.Equal(6, writer.Length);
    }

    [Fact]
    public void PatchUInt16LE_PatchesAtOffset()
    {
        using var writer = new PacketWriter();
        writer.WriteUInt16LE(0);     // placeholder at offset 0
        writer.WriteUInt32LE(0xDEAD);
        writer.PatchUInt16LE(0, 42); // patch the placeholder

        var result = writer.ToArray();
        Assert.Equal(42, BitConverter.ToUInt16(result, 0));
    }

    [Fact]
    public void Reset_ClearsPosition()
    {
        using var writer = new PacketWriter();
        writer.WriteUInt32LE(0x12345678);
        Assert.Equal(4, writer.Length);

        writer.Reset();
        Assert.Equal(0, writer.Length);
    }

    [Fact]
    public void AutoGrows_WhenBufferFull()
    {
        using var writer = new PacketWriter(4); // start with tiny buffer

        // Write more than 4 bytes to trigger growth
        for (int i = 0; i < 100; i++)
            writer.WriteUInt32LE((uint)i);

        Assert.Equal(400, writer.Length);
    }

    [Fact]
    public void MultipleTypes_WrittenSequentially()
    {
        using var writer = new PacketWriter();
        writer.WriteUInt8(0x01);
        writer.WriteUInt16LE(0x0203);
        writer.WriteUInt32LE(0x04050607);

        var result = writer.ToArray();
        Assert.Equal(7, result.Length);
        Assert.Equal(0x01, result[0]);
        Assert.Equal(0x03, result[1]); // LE: low byte of 0x0203
        Assert.Equal(0x02, result[2]);
        Assert.Equal(0x07, result[3]); // LE: low byte of 0x04050607
        Assert.Equal(0x06, result[4]);
        Assert.Equal(0x05, result[5]);
        Assert.Equal(0x04, result[6]);
    }

    [Fact]
    public void WriteInt16LE_WritesSignedValue()
    {
        using var writer = new PacketWriter();
        writer.WriteInt16LE(-100);
        var result = writer.ToArray();
        Assert.Equal(-100, BitConverter.ToInt16(result, 0));
    }

    [Fact]
    public void WriteInt16BE_WritesBigEndian()
    {
        using var writer = new PacketWriter();
        writer.WriteInt16BE(0x0102);
        var result = writer.ToArray();
        Assert.Equal(0x01, result[0]);
        Assert.Equal(0x02, result[1]);
    }

    [Fact]
    public void WriteInt32BE_WritesBigEndian()
    {
        using var writer = new PacketWriter();
        writer.WriteInt32BE(0x01020304);
        var result = writer.ToArray();
        Assert.Equal(0x01, result[0]);
        Assert.Equal(0x02, result[1]);
        Assert.Equal(0x03, result[2]);
        Assert.Equal(0x04, result[3]);
    }

    [Fact]
    public void WriteUInt64BE_WritesBigEndian()
    {
        using var writer = new PacketWriter();
        writer.WriteUInt64BE(0x0102030405060708UL);
        var result = writer.ToArray();
        Assert.Equal(0x01, result[0]);
        Assert.Equal(0x08, result[7]);
    }

    [Fact]
    public void WriteInt64LE_WritesSignedValue()
    {
        using var writer = new PacketWriter();
        writer.WriteInt64LE(-42);
        var result = writer.ToArray();
        Assert.Equal(-42L, BitConverter.ToInt64(result, 0));
    }

    [Fact]
    public void WriteSingleBE_WritesBigEndianFloat()
    {
        using var writer = new PacketWriter();
        writer.WriteSingleBE(3.14f);
        var result = writer.ToArray();
        // Read back in BE
        if (BitConverter.IsLittleEndian) Array.Reverse(result);
        Assert.Equal(3.14f, BitConverter.ToSingle(result, 0));
    }

    [Fact]
    public void WriteDoubleBE_WritesBigEndianDouble()
    {
        using var writer = new PacketWriter();
        writer.WriteDoubleBE(3.14);
        var result = writer.ToArray();
        if (BitConverter.IsLittleEndian) Array.Reverse(result);
        Assert.Equal(3.14, BitConverter.ToDouble(result, 0));
    }

    [Fact]
    public void Position_SetterWorks()
    {
        using var writer = new PacketWriter();
        writer.WriteUInt32LE(0);
        writer.Position = 0;
        writer.WriteUInt16LE(0x1234);
        writer.Position = 4; // restore
        var result = writer.ToArray();
        Assert.Equal(0x1234, BitConverter.ToUInt16(result, 0));
    }

    [Fact]
    public void Position_SetterThrowsOnInvalid()
    {
        using var writer = new PacketWriter();
        Assert.Throws<ArgumentOutOfRangeException>(() => writer.Position = -1);
    }

    [Fact]
    public void GetWrittenMemory_ReturnsCorrectData()
    {
        using var writer = new PacketWriter();
        writer.WriteUInt8(0x42);
        writer.WriteUInt8(0x43);
        var mem = writer.GetWrittenMemory();
        Assert.Equal(2, mem.Length);
        Assert.Equal(0x42, mem.Span[0]);
    }

    [Fact]
    public void GetWrittenSpan_ReturnsCorrectData()
    {
        using var writer = new PacketWriter();
        writer.WriteUInt8(0xAB);
        var span = writer.GetWrittenSpan();
        Assert.Equal(1, span.Length);
        Assert.Equal(0xAB, span[0]);
    }

    [Fact]
    public void PatchUInt32LE_PatchesAtOffset()
    {
        using var writer = new PacketWriter();
        writer.WriteUInt32LE(0);
        writer.WriteUInt32LE(0xDEAD);
        writer.PatchUInt32LE(0, 0x12345678);
        var result = writer.ToArray();
        Assert.Equal(0x12345678U, BitConverter.ToUInt32(result, 0));
    }

    [Fact]
    public void Dispose_CanBeCalledMultipleTimes()
    {
        var writer = new PacketWriter();
        writer.WriteUInt8(1);
        writer.Dispose();
        writer.Dispose(); // should not throw
    }

    [Fact]
    public void WriteBigEndian_RoundTrip()
    {
        using var writer = new PacketWriter();
        writer.WriteUInt16BE(0x1234);
        writer.WriteUInt32BE(0xDEADBEEF);
        writer.WriteInt16BE(-100);
        writer.WriteInt32BE(-42);

        var data = writer.ToArray();
        var reader = new PacketReader(data);
        Assert.Equal((ushort)0x1234, reader.ReadUInt16BE());
        Assert.Equal(0xDEADBEEFU, reader.ReadUInt32BE());
        Assert.Equal((short)-100, reader.ReadInt16BE());
        Assert.Equal(-42, reader.ReadInt32BE());
    }
}
