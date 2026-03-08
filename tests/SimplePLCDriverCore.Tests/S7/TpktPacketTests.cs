using System.Buffers.Binary;
using SimplePLCDriverCore.Common.Buffers;
using SimplePLCDriverCore.Protocols.S7;

namespace SimplePLCDriverCore.Tests.S7;

public class TpktPacketTests
{
    [Fact]
    public void Write_ProducesCorrectFrame()
    {
        var payload = new byte[] { 0x01, 0x02, 0x03 };
        using var writer = new PacketWriter(32);
        TpktPacket.Write(writer, payload);

        var frame = writer.ToArray();
        Assert.Equal(7, frame.Length); // 4 header + 3 payload
        Assert.Equal(3, frame[0]); // version
        Assert.Equal(0, frame[1]); // reserved
        Assert.Equal(7, BinaryPrimitives.ReadUInt16BigEndian(frame.AsSpan(2))); // total length
        Assert.Equal(0x01, frame[4]);
        Assert.Equal(0x02, frame[5]);
        Assert.Equal(0x03, frame[6]);
    }

    [Fact]
    public void Write_EmptyPayload_ProducesHeaderOnly()
    {
        using var writer = new PacketWriter(16);
        TpktPacket.Write(writer, ReadOnlySpan<byte>.Empty);

        var frame = writer.ToArray();
        Assert.Equal(4, frame.Length);
        Assert.Equal(3, frame[0]);
        Assert.Equal(4, BinaryPrimitives.ReadUInt16BigEndian(frame.AsSpan(2)));
    }

    [Fact]
    public void GetLengthFromHeader_ReturnsCorrectLength()
    {
        var header = new byte[] { 0x03, 0x00, 0x00, 0x1A }; // length = 26
        Assert.Equal(26, TpktPacket.GetLengthFromHeader(header));
    }

    [Fact]
    public void GetLengthFromHeader_TooShort_Throws()
    {
        Assert.Throws<InvalidOperationException>(
            () => TpktPacket.GetLengthFromHeader(new byte[] { 0x03, 0x00 }));
    }

    [Fact]
    public void GetPayload_ReturnsDataAfterHeader()
    {
        var frame = new byte[] { 0x03, 0x00, 0x00, 0x07, 0xAA, 0xBB, 0xCC };
        var payload = TpktPacket.GetPayload(frame);
        Assert.Equal(3, payload.Length);
        Assert.Equal(0xAA, payload[0]);
        Assert.Equal(0xBB, payload[1]);
        Assert.Equal(0xCC, payload[2]);
    }

    [Fact]
    public void GetPayload_TooShort_Throws()
    {
        Assert.Throws<InvalidOperationException>(
            () => TpktPacket.GetPayload(new byte[] { 0x03, 0x00 }));
    }

    [Fact]
    public void RoundTrip_WriteAndParse()
    {
        var payload = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };
        using var writer = new PacketWriter(32);
        TpktPacket.Write(writer, payload);

        var frame = writer.ToArray();
        var length = TpktPacket.GetLengthFromHeader(frame);
        Assert.Equal(frame.Length, length);

        var parsed = TpktPacket.GetPayload(frame);
        Assert.Equal(payload, parsed.ToArray());
    }
}
