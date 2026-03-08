using System.Buffers.Binary;
using SimplePLCDriverCore.Common.Buffers;
using SimplePLCDriverCore.Protocols.S7;

namespace SimplePLCDriverCore.Tests.S7;

public class S7MessageTests
{
    // ==========================================================================
    // BuildSetupCommunication Tests
    // ==========================================================================

    [Fact]
    public void BuildSetupCommunication_HasCorrectHeader()
    {
        var msg = S7Message.BuildSetupCommunication(1, 1, 1, 480);

        Assert.Equal(S7Message.ProtocolId, msg[0]); // 0x32
        Assert.Equal((byte)S7MessageType.Job, msg[1]); // 0x01
        Assert.Equal(1, BinaryPrimitives.ReadUInt16BigEndian(msg.AsSpan(4))); // PDU ref
        Assert.Equal(8, BinaryPrimitives.ReadUInt16BigEndian(msg.AsSpan(6))); // param length
        Assert.Equal(0, BinaryPrimitives.ReadUInt16BigEndian(msg.AsSpan(8))); // data length
    }

    [Fact]
    public void BuildSetupCommunication_HasCorrectParams()
    {
        var msg = S7Message.BuildSetupCommunication(1, 2, 3, 960);

        Assert.Equal((byte)S7Function.CommunicationSetup, msg[10]); // function code 0xF0
        Assert.Equal(2, BinaryPrimitives.ReadUInt16BigEndian(msg.AsSpan(12))); // max AMQ calling
        Assert.Equal(3, BinaryPrimitives.ReadUInt16BigEndian(msg.AsSpan(14))); // max AMQ called
        Assert.Equal(960, BinaryPrimitives.ReadUInt16BigEndian(msg.AsSpan(16))); // PDU size
    }

    // ==========================================================================
    // BuildReadRequest Tests
    // ==========================================================================

    [Fact]
    public void BuildReadRequest_SingleItem_HasCorrectStructure()
    {
        var addr = S7Address.Parse("DB1.DBW0");
        var msg = S7Message.BuildReadRequest(1, [addr]);

        Assert.Equal(S7Message.ProtocolId, msg[0]);
        Assert.Equal((byte)S7MessageType.Job, msg[1]);
        Assert.Equal(0, BinaryPrimitives.ReadUInt16BigEndian(msg.AsSpan(8))); // no data section

        // Parameter section starts at offset 10
        Assert.Equal((byte)S7Function.ReadVar, msg[10]); // function code
        Assert.Equal(1, msg[11]); // item count
    }

    [Fact]
    public void BuildReadRequest_MultipleItems_CorrectItemCount()
    {
        var addresses = new[]
        {
            S7Address.Parse("DB1.DBW0"),
            S7Address.Parse("DB1.DBD4"),
            S7Address.Parse("DB1.DBX8.0"),
        };
        var msg = S7Message.BuildReadRequest(1, addresses);

        Assert.Equal(3, msg[11]); // item count
    }

    [Fact]
    public void BuildReadRequest_ItemSpec_HasCorrectFormat()
    {
        var addr = S7Address.Parse("DB1.DBW0");
        var msg = S7Message.BuildReadRequest(1, [addr]);

        // Item spec starts at offset 12
        Assert.Equal(0x12, msg[12]); // specification type
        Assert.Equal(0x0A, msg[13]); // length = 10
        Assert.Equal(0x10, msg[14]); // syntax ID: S7ANY
        Assert.Equal((byte)S7TransportSize.Word, msg[15]); // transport size
        Assert.Equal(1, BinaryPrimitives.ReadUInt16BigEndian(msg.AsSpan(18))); // DB number
        Assert.Equal((byte)S7Area.DataBlock, msg[20]); // area
    }

    // ==========================================================================
    // BuildWriteRequest Tests
    // ==========================================================================

    [Fact]
    public void BuildWriteRequest_HasCorrectFunctionCode()
    {
        var addr = S7Address.Parse("DB1.DBW0");
        var data = new byte[] { 0x00, 0x2A }; // 42 big-endian
        var msg = S7Message.BuildWriteRequest(1, addr, data);

        Assert.Equal(S7Message.ProtocolId, msg[0]);
        Assert.Equal((byte)S7MessageType.Job, msg[1]);

        // Find function code in parameter section
        var paramStart = S7Message.JobHeaderSize;
        Assert.Equal((byte)S7Function.WriteVar, msg[paramStart]);
        Assert.Equal(1, msg[paramStart + 1]); // item count
    }

    [Fact]
    public void BuildWriteRequest_BitAddress_UseBitTransportSize()
    {
        var addr = S7Address.Parse("DB1.DBX0.0");
        var data = new byte[] { 0x01 };
        var msg = S7Message.BuildWriteRequest(1, addr, data);

        // Data section should use bit access transport size
        var paramLength = BinaryPrimitives.ReadUInt16BigEndian(msg.AsSpan(6));
        var dataStart = S7Message.JobHeaderSize + paramLength;

        Assert.Equal(0x00, msg[dataStart]); // return code (reserved)
        Assert.Equal((byte)S7DataTransportSize.BitAccess, msg[dataStart + 1]);
        Assert.Equal(1, BinaryPrimitives.ReadUInt16BigEndian(msg.AsSpan(dataStart + 2))); // 1 bit
    }

    // ==========================================================================
    // ParseResponse Tests
    // ==========================================================================

    [Fact]
    public void ParseResponse_SetupResponse_Success()
    {
        var response = BuildMockSetupResponse(0, 0, 480);
        var result = S7Message.ParseResponse(response);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void ParseResponse_ErrorResponse_ReturnsFailure()
    {
        var response = BuildMockAckDataResponse(
            (byte)S7Function.ReadVar, errorClass: 0x85, errorCode: 0x01,
            paramExtra: [0x00], dataSection: []);

        var result = S7Message.ParseResponse(response);

        Assert.False(result.IsSuccess);
        Assert.Equal(0x85, result.ErrorClass);
    }

    [Fact]
    public void ParseResponse_ReadResponse_SingleItem_Success()
    {
        // Read response with one word item (value = 1000)
        var itemData = new byte[4];
        itemData[0] = 0xFF; // return code: success
        itemData[1] = (byte)S7DataTransportSize.ByteWordDWord;
        BinaryPrimitives.WriteUInt16BigEndian(itemData.AsSpan(2), 2); // 2 bytes
        var valueData = new byte[2];
        BinaryPrimitives.WriteInt16BigEndian(valueData, 1000);

        var response = BuildMockAckDataResponse(
            (byte)S7Function.ReadVar, errorClass: 0, errorCode: 0,
            paramExtra: [0x01], // 1 item
            dataSection: [.. itemData, .. valueData]);

        var result = S7Message.ParseResponse(response);

        Assert.True(result.IsSuccess);
        Assert.Single(result.ItemData);
        Assert.Equal(0xFF, result.ItemReturnCodes[0]);
        Assert.Equal(1000, BinaryPrimitives.ReadInt16BigEndian(result.ItemData[0]));
    }

    [Fact]
    public void ParseResponse_ReadResponse_ItemError()
    {
        var itemData = new byte[4];
        itemData[0] = 0x05; // return code: invalid address
        itemData[1] = 0x00;
        BinaryPrimitives.WriteUInt16BigEndian(itemData.AsSpan(2), 0);

        var response = BuildMockAckDataResponse(
            (byte)S7Function.ReadVar, errorClass: 0, errorCode: 0,
            paramExtra: [0x01],
            dataSection: itemData);

        var result = S7Message.ParseResponse(response);

        Assert.False(result.IsSuccess);
        Assert.Equal(0x05, result.ItemReturnCodes[0]);
    }

    [Fact]
    public void ParseResponse_WriteResponse_Success()
    {
        var response = BuildMockAckDataResponse(
            (byte)S7Function.WriteVar, errorClass: 0, errorCode: 0,
            paramExtra: [0x01], // 1 item
            dataSection: [0xFF]); // success return code

        var result = S7Message.ParseResponse(response);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void ParseResponse_WriteResponse_ItemFailed()
    {
        var response = BuildMockAckDataResponse(
            (byte)S7Function.WriteVar, errorClass: 0, errorCode: 0,
            paramExtra: [0x01],
            dataSection: [0x05]); // invalid address

        var result = S7Message.ParseResponse(response);

        Assert.False(result.IsSuccess);
        Assert.Equal(0x05, result.ItemReturnCodes[0]);
    }

    [Fact]
    public void ParseResponse_TooShort_Throws()
    {
        Assert.Throws<InvalidOperationException>(
            () => S7Message.ParseResponse(new byte[] { 0x32, 0x03 }));
    }

    [Fact]
    public void ParseResponse_InvalidProtocolId_Throws()
    {
        var bad = new byte[12];
        bad[0] = 0x00; // wrong protocol ID
        bad[1] = 0x03;
        Assert.Throws<InvalidOperationException>(() => S7Message.ParseResponse(bad));
    }

    [Fact]
    public void ParseResponse_NotAckData_Throws()
    {
        var bad = new byte[12];
        bad[0] = 0x32;
        bad[1] = 0x01; // Job, not AckData
        Assert.Throws<InvalidOperationException>(() => S7Message.ParseResponse(bad));
    }

    // ==========================================================================
    // ParseSetupPduSize Tests
    // ==========================================================================

    [Fact]
    public void ParseSetupPduSize_ReturnsNegotiatedSize()
    {
        var response = BuildMockSetupResponse(0, 0, 960);
        var pduSize = S7Message.ParseSetupPduSize(response);
        Assert.Equal(960, pduSize);
    }

    [Fact]
    public void ParseSetupPduSize_TooShort_ReturnsDefault()
    {
        Assert.Equal(240, S7Message.ParseSetupPduSize(new byte[4]));
    }

    // ==========================================================================
    // S7Response Tests
    // ==========================================================================

    [Fact]
    public void S7Response_GetErrorMessage_Success_Empty()
    {
        var resp = new S7Response(true, 0, 0, [], []);
        Assert.Equal(string.Empty, resp.GetErrorMessage());
    }

    [Fact]
    public void S7Response_GetErrorMessage_Error_HasDescription()
    {
        var resp = new S7Response(false, 0x85, 0x01, [], []);
        var msg = resp.GetErrorMessage();
        Assert.Contains("0x85", msg);
    }

    [Fact]
    public void S7Response_GetItemErrorMessage_Success()
    {
        Assert.Equal("Success", S7Response.GetItemErrorMessage(0xFF));
    }

    [Fact]
    public void S7Response_GetItemErrorMessage_InvalidAddress()
    {
        Assert.Contains("address", S7Response.GetItemErrorMessage(0x05),
            StringComparison.OrdinalIgnoreCase);
    }

    // ==========================================================================
    // Helpers
    // ==========================================================================

    private static byte[] BuildMockSetupResponse(byte errorClass, byte errorCode, ushort pduSize)
    {
        using var writer = new PacketWriter(32);

        // AckData header
        writer.WriteUInt8(S7Message.ProtocolId);
        writer.WriteUInt8((byte)S7MessageType.AckData);
        writer.WriteUInt16BE(0); // reserved
        writer.WriteUInt16BE(1); // PDU ref
        writer.WriteUInt16BE(8); // param length
        writer.WriteUInt16BE(0); // data length
        writer.WriteUInt8(errorClass);
        writer.WriteUInt8(errorCode);

        // Setup params
        writer.WriteUInt8((byte)S7Function.CommunicationSetup);
        writer.WriteUInt8(0); // reserved
        writer.WriteUInt16BE(1); // max AMQ calling
        writer.WriteUInt16BE(1); // max AMQ called
        writer.WriteUInt16BE(pduSize);

        return writer.ToArray();
    }

    private static byte[] BuildMockAckDataResponse(
        byte functionCode, byte errorClass, byte errorCode,
        byte[] paramExtra, byte[] dataSection)
    {
        using var writer = new PacketWriter(64);

        var paramLength = 1 + paramExtra.Length; // function code + extra

        // AckData header
        writer.WriteUInt8(S7Message.ProtocolId);
        writer.WriteUInt8((byte)S7MessageType.AckData);
        writer.WriteUInt16BE(0); // reserved
        writer.WriteUInt16BE(1); // PDU ref
        writer.WriteUInt16BE((ushort)paramLength);
        writer.WriteUInt16BE((ushort)dataSection.Length);
        writer.WriteUInt8(errorClass);
        writer.WriteUInt8(errorCode);

        // Parameter section
        writer.WriteUInt8(functionCode);
        writer.WriteBytes(paramExtra);

        // Data section
        writer.WriteBytes(dataSection);

        return writer.ToArray();
    }
}
