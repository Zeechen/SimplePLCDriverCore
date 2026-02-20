using System.Buffers.Binary;
using SimplePLCDriverCore.Common.Buffers;
using SimplePLCDriverCore.Protocols.EtherNetIP;

namespace SimplePLCDriverCore.Tests.EtherNetIP;

public class EipEncapsulationTests
{
    [Fact]
    public void BuildRegisterSession_CreatesCorrectPacket()
    {
        var packet = EipEncapsulation.BuildRegisterSession();

        Assert.Equal(28, packet.Length); // 24 header + 4 data

        // Command = RegisterSession (0x0065)
        Assert.Equal(0x65, packet[0]);
        Assert.Equal(0x00, packet[1]);

        // Data length = 4
        Assert.Equal(0x04, packet[2]);
        Assert.Equal(0x00, packet[3]);

        // Session handle = 0 (not yet assigned)
        Assert.Equal(0x00, packet[4]);
        Assert.Equal(0x00, packet[5]);
        Assert.Equal(0x00, packet[6]);
        Assert.Equal(0x00, packet[7]);

        // Status = 0
        Assert.Equal(0x00, packet[8]);

        // Data: protocol version = 1
        Assert.Equal(0x01, packet[24]);
        Assert.Equal(0x00, packet[25]);

        // Data: options = 0
        Assert.Equal(0x00, packet[26]);
        Assert.Equal(0x00, packet[27]);
    }

    [Fact]
    public void BuildUnregisterSession_CreatesCorrectPacket()
    {
        var packet = EipEncapsulation.BuildUnregisterSession(0x12345678);

        Assert.Equal(24, packet.Length); // header only, no data

        // Command = UnregisterSession (0x0066)
        Assert.Equal(0x66, packet[0]);
        Assert.Equal(0x00, packet[1]);

        // Data length = 0
        Assert.Equal(0x00, packet[2]);
        Assert.Equal(0x00, packet[3]);

        // Session handle
        var sessionHandle = BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(4));
        Assert.Equal(0x12345678U, sessionHandle);
    }

    [Fact]
    public void DecodeHeader_ParsesCorrectly()
    {
        // Build a RegisterSession response
        var responseBytes = new byte[28];
        BinaryPrimitives.WriteUInt16LittleEndian(responseBytes.AsSpan(0), 0x0065); // command
        BinaryPrimitives.WriteUInt16LittleEndian(responseBytes.AsSpan(2), 4);       // data length
        BinaryPrimitives.WriteUInt32LittleEndian(responseBytes.AsSpan(4), 0xABCD);  // session handle
        BinaryPrimitives.WriteUInt32LittleEndian(responseBytes.AsSpan(8), 0);       // status = success
        // sender context at offset 12-19
        responseBytes[12] = 0x42;
        BinaryPrimitives.WriteUInt32LittleEndian(responseBytes.AsSpan(20), 0);      // options

        var header = EipEncapsulation.DecodeHeader(responseBytes);

        Assert.Equal(EipCommand.RegisterSession, header.Command);
        Assert.Equal((ushort)4, header.DataLength);
        Assert.Equal(0xABCDU, header.SessionHandle);
        Assert.Equal(EipStatus.Success, header.Status);
        Assert.Equal(0x42, header.SenderContext.Span[0]);
        Assert.Equal(0U, header.Options);
    }

    [Fact]
    public void DecodeHeader_ThrowsOnShortData()
    {
        var shortData = new byte[10];
        Assert.Throws<InvalidDataException>(() =>
            EipEncapsulation.DecodeHeader(shortData));
    }

    [Fact]
    public void Decode_ParsesHeaderAndData()
    {
        var message = new byte[28]; // 24 header + 4 data
        BinaryPrimitives.WriteUInt16LittleEndian(message.AsSpan(0), 0x0065);
        BinaryPrimitives.WriteUInt16LittleEndian(message.AsSpan(2), 4);
        BinaryPrimitives.WriteUInt32LittleEndian(message.AsSpan(4), 0x1234);
        // data payload
        message[24] = 0xAA;
        message[25] = 0xBB;
        message[26] = 0xCC;
        message[27] = 0xDD;

        var (header, data) = EipEncapsulation.Decode(message);

        Assert.Equal(EipCommand.RegisterSession, header.Command);
        Assert.Equal(0x1234U, header.SessionHandle);
        Assert.Equal(4, data.Length);
        Assert.Equal(0xAA, data.Span[0]);
        Assert.Equal(0xDD, data.Span[3]);
    }

    [Fact]
    public void Decode_EmptyDataPayload()
    {
        var message = new byte[24];
        BinaryPrimitives.WriteUInt16LittleEndian(message.AsSpan(0), 0x0066); // UnregisterSession
        BinaryPrimitives.WriteUInt16LittleEndian(message.AsSpan(2), 0);      // no data

        var (header, data) = EipEncapsulation.Decode(message);

        Assert.Equal(EipCommand.UnregisterSession, header.Command);
        Assert.Equal(0, header.DataLength);
        Assert.True(data.IsEmpty);
    }

    [Fact]
    public void GetTotalLengthFromHeader_CalculatesCorrectly()
    {
        var header = new byte[24];
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(2), 100); // data length = 100

        var totalLength = EipEncapsulation.GetTotalLengthFromHeader(header);

        Assert.Equal(124, totalLength); // 24 + 100
    }

    [Fact]
    public void GetTotalLengthFromHeader_ZeroDataLength()
    {
        var header = new byte[24];
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(2), 0);

        var totalLength = EipEncapsulation.GetTotalLengthFromHeader(header);

        Assert.Equal(24, totalLength);
    }

    [Fact]
    public void BuildSendRRData_CreatesCorrectStructure()
    {
        var cipMessage = new byte[] { 0x01, 0x02, 0x03 };
        var packet = EipEncapsulation.BuildSendRRData(0xAAAA, cipMessage, timeout: 5);

        // Command = SendRRData (0x006F)
        Assert.Equal(0x6F, packet[0]);
        Assert.Equal(0x00, packet[1]);

        // Session handle
        var sessionHandle = BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(4));
        Assert.Equal(0xAAAAU, sessionHandle);

        // After header (offset 24):
        // Interface handle (4 bytes) = 0
        Assert.Equal(0U, BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(24)));

        // Timeout (2 bytes) = 5
        Assert.Equal(5, BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(28)));

        // Item count (2 bytes) = 2
        Assert.Equal(2, BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(30)));

        // Item 1: Null Address (type=0x0000, length=0)
        Assert.Equal(0x0000, BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(32)));
        Assert.Equal(0, BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(34)));

        // Item 2: Unconnected Data (type=0x00B2, length=3)
        Assert.Equal(0x00B2, BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(36)));
        Assert.Equal(3, BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(38)));

        // CIP message data
        Assert.Equal(0x01, packet[40]);
        Assert.Equal(0x02, packet[41]);
        Assert.Equal(0x03, packet[42]);
    }

    [Fact]
    public void BuildSendUnitData_CreatesCorrectStructure()
    {
        var cipMessage = new byte[] { 0xAA, 0xBB };
        var packet = EipEncapsulation.BuildSendUnitData(
            sessionHandle: 0x5678,
            connectionId: 0x1234,
            sequenceNumber: 42,
            cipMessage: cipMessage);

        // Command = SendUnitData (0x0070)
        Assert.Equal(0x70, packet[0]);
        Assert.Equal(0x00, packet[1]);

        // After header (offset 24):
        // Interface handle = 0
        Assert.Equal(0U, BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(24)));

        // Timeout = 0 (connected messaging)
        Assert.Equal(0, BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(28)));

        // Item count = 2
        Assert.Equal(2, BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(30)));

        // Item 1: Connected Address (type=0x00A1, length=4)
        Assert.Equal(0x00A1, BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(32)));
        Assert.Equal(4, BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(34)));
        Assert.Equal(0x1234U, BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(36)));

        // Item 2: Connected Data (type=0x00B1, length=4) (2 seq + 2 data)
        Assert.Equal(0x00B1, BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(40)));
        Assert.Equal(4, BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(42)));

        // Sequence number
        Assert.Equal(42, BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(44)));

        // CIP message
        Assert.Equal(0xAA, packet[46]);
        Assert.Equal(0xBB, packet[47]);
    }

    [Fact]
    public void BuildListIdentity_CreatesHeaderOnly()
    {
        var packet = EipEncapsulation.BuildListIdentity();

        Assert.Equal(24, packet.Length);

        // Command = ListIdentity (0x0063)
        Assert.Equal(0x63, packet[0]);
        Assert.Equal(0x00, packet[1]);

        // Data length = 0
        Assert.Equal(0, BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(2)));
    }

    [Fact]
    public void GenerateSenderContext_IsUnique()
    {
        var ctx1 = EipEncapsulation.GenerateSenderContext();
        var ctx2 = EipEncapsulation.GenerateSenderContext();

        Assert.Equal(8, ctx1.Length);
        Assert.Equal(8, ctx2.Length);
        Assert.NotEqual(ctx1, ctx2);
    }

    [Fact]
    public void ExtractCipData_FromUnconnectedResponse()
    {
        // Simulate a SendRRData response payload (after EIP header)
        using var writer = new PacketWriter();
        writer.WriteUInt32LE(0);          // interface handle
        writer.WriteUInt16LE(0);          // timeout
        writer.WriteUInt16LE(2);          // 2 CPF items

        // Item 1: Null Address
        writer.WriteUInt16LE(0x0000);     // type
        writer.WriteUInt16LE(0);          // length

        // Item 2: Unconnected Data
        writer.WriteUInt16LE(0x00B2);     // type
        writer.WriteUInt16LE(3);          // length
        writer.WriteBytes(new byte[] { 0xAA, 0xBB, 0xCC }); // CIP data

        var eipData = writer.ToArray();
        var cipData = EipEncapsulation.ExtractCipData(eipData, isConnected: false);

        Assert.Equal(3, cipData.Length);
        Assert.Equal(0xAA, cipData.Span[0]);
        Assert.Equal(0xBB, cipData.Span[1]);
        Assert.Equal(0xCC, cipData.Span[2]);
    }

    [Fact]
    public void ExtractCipData_FromConnectedResponse()
    {
        using var writer = new PacketWriter();
        writer.WriteUInt32LE(0);          // interface handle
        writer.WriteUInt16LE(0);          // timeout
        writer.WriteUInt16LE(2);          // 2 items

        // Item 1: Connected Address
        writer.WriteUInt16LE(0x00A1);
        writer.WriteUInt16LE(4);
        writer.WriteUInt32LE(0x1234);     // connection ID

        // Item 2: Connected Data (includes sequence number)
        writer.WriteUInt16LE(0x00B1);
        writer.WriteUInt16LE(5);          // 2 seq + 3 data
        writer.WriteUInt16LE(1);          // sequence number
        writer.WriteBytes(new byte[] { 0xDD, 0xEE, 0xFF }); // CIP data

        var eipData = writer.ToArray();
        var cipData = EipEncapsulation.ExtractCipData(eipData, isConnected: true);

        Assert.Equal(3, cipData.Length);
        Assert.Equal(0xDD, cipData.Span[0]);
        Assert.Equal(0xEE, cipData.Span[1]);
        Assert.Equal(0xFF, cipData.Span[2]);
    }
}
