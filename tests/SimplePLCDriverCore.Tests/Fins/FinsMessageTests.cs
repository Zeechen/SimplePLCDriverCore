using System.Buffers.Binary;
using SimplePLCDriverCore.Common.Buffers;
using SimplePLCDriverCore.Protocols.Fins;

namespace SimplePLCDriverCore.Tests.Fins;

public class FinsMessageTests
{
    // ==========================================================================
    // Node Address Request/Response
    // ==========================================================================

    [Fact]
    public void BuildNodeAddressRequest_HasCorrectMagic()
    {
        var req = FinsMessage.BuildNodeAddressRequest();

        Assert.Equal(0x46, req[0]); // F
        Assert.Equal(0x49, req[1]); // I
        Assert.Equal(0x4E, req[2]); // N
        Assert.Equal(0x53, req[3]); // S
    }

    [Fact]
    public void BuildNodeAddressRequest_HasCorrectCommand()
    {
        var req = FinsMessage.BuildNodeAddressRequest();
        var command = BinaryPrimitives.ReadUInt32BigEndian(req.AsSpan(8));
        Assert.Equal(FinsMessage.TcpCommandNodeAddressRequest, command);
    }

    [Fact]
    public void BuildNodeAddressRequest_HasCorrectLength()
    {
        var req = FinsMessage.BuildNodeAddressRequest();
        var length = BinaryPrimitives.ReadUInt32BigEndian(req.AsSpan(4));
        Assert.Equal(8u, length); // 8 bytes remaining after length field
    }

    [Fact]
    public void ParseNodeAddressResponse_ReturnsNodes()
    {
        var response = BuildMockNodeAddressResponse(10, 1);
        var (clientNode, serverNode) = FinsMessage.ParseNodeAddressResponse(response);
        Assert.Equal(10, clientNode);
        Assert.Equal(1, serverNode);
    }

    [Fact]
    public void ParseNodeAddressResponse_TooShort_Throws()
    {
        Assert.Throws<InvalidOperationException>(
            () => FinsMessage.ParseNodeAddressResponse(new byte[8]));
    }

    [Fact]
    public void ParseNodeAddressResponse_ErrorCode_Throws()
    {
        var response = BuildMockNodeAddressResponse(10, 1, errorCode: 0x01);
        Assert.Throws<IOException>(
            () => FinsMessage.ParseNodeAddressResponse(response));
    }

    // ==========================================================================
    // Read/Write Request Building
    // ==========================================================================

    [Fact]
    public void BuildReadRequest_HasFinsMagic()
    {
        var addr = FinsAddress.Parse("D0");
        var req = FinsMessage.BuildReadRequest(addr, 1, 10, 1);

        Assert.Equal(0x46, req[0]); // F
        Assert.Equal(0x49, req[1]); // I
        Assert.Equal(0x4E, req[2]); // N
        Assert.Equal(0x53, req[3]); // S
    }

    [Fact]
    public void BuildReadRequest_HasCorrectTcpCommand()
    {
        var addr = FinsAddress.Parse("D0");
        var req = FinsMessage.BuildReadRequest(addr, 1, 10, 1);

        var command = BinaryPrimitives.ReadUInt32BigEndian(req.AsSpan(8));
        Assert.Equal(FinsMessage.TcpCommandSendFrame, command);
    }

    [Fact]
    public void BuildReadRequest_HasCorrectFinsCommand()
    {
        var addr = FinsAddress.Parse("D0");
        var req = FinsMessage.BuildReadRequest(addr, 1, 10, 1);

        // FINS command is at TCP header (16) + FINS header (10)
        var commandOffset = FinsMessage.TcpHeaderSize + FinsMessage.FinsHeaderSize;
        var command = BinaryPrimitives.ReadUInt16BigEndian(req.AsSpan(commandOffset));
        Assert.Equal(FinsCommands.MemoryAreaRead, command);
    }

    [Fact]
    public void BuildReadRequest_HasCorrectAreaCode()
    {
        var addr = FinsAddress.Parse("D0");
        var req = FinsMessage.BuildReadRequest(addr, 1, 10, 1);

        var areaOffset = FinsMessage.TcpHeaderSize + FinsMessage.FinsHeaderSize + 2;
        Assert.Equal((byte)FinsArea.DmWord, req[areaOffset]);
    }

    [Fact]
    public void BuildReadRequest_HasCorrectNodeAddresses()
    {
        var addr = FinsAddress.Parse("D0");
        var req = FinsMessage.BuildReadRequest(addr, sid: 5, sourceNode: 10, destNode: 1);

        var finsStart = FinsMessage.TcpHeaderSize;
        Assert.Equal(1, req[finsStart + 4]); // DA1 = dest node
        Assert.Equal(10, req[finsStart + 7]); // SA1 = source node
        Assert.Equal(5, req[finsStart + 9]); // SID
    }

    [Fact]
    public void BuildWriteRequest_HasCorrectFinsCommand()
    {
        var addr = FinsAddress.Parse("D0");
        var data = new byte[] { 0x00, 0x2A }; // 42 BE
        var req = FinsMessage.BuildWriteRequest(addr, data, 1, 10, 1);

        var commandOffset = FinsMessage.TcpHeaderSize + FinsMessage.FinsHeaderSize;
        var command = BinaryPrimitives.ReadUInt16BigEndian(req.AsSpan(commandOffset));
        Assert.Equal(FinsCommands.MemoryAreaWrite, command);
    }

    [Fact]
    public void BuildWriteRequest_ContainsData()
    {
        var addr = FinsAddress.Parse("D0");
        var writeData = new byte[] { 0x00, 0x2A };
        var req = FinsMessage.BuildWriteRequest(addr, writeData, 1, 10, 1);

        // Data should be at the end of the frame
        Assert.Equal(0x00, req[^2]);
        Assert.Equal(0x2A, req[^1]);
    }

    // ==========================================================================
    // ParseResponse
    // ==========================================================================

    [Fact]
    public void ParseResponse_Success()
    {
        var response = BuildMockFinsResponse(0x00, 0x00, [0x00, 0x2A]);
        var result = FinsMessage.ParseResponse(response);

        Assert.True(result.IsSuccess);
        Assert.Equal(0x00, result.MainResponseCode);
        Assert.Equal(0x00, result.SubResponseCode);
        Assert.Equal(2, result.Data.Length);
    }

    [Fact]
    public void ParseResponse_Error()
    {
        var response = BuildMockFinsResponse(0x01, 0x01, []);
        var result = FinsMessage.ParseResponse(response);

        Assert.False(result.IsSuccess);
        Assert.Equal(0x01, result.MainResponseCode);
    }

    [Fact]
    public void ParseResponse_TooShort_Throws()
    {
        Assert.Throws<InvalidOperationException>(
            () => FinsMessage.ParseResponse(new byte[4]));
    }

    [Fact]
    public void ParseResponse_TcpError_Throws()
    {
        var response = BuildMockFinsResponse(0x00, 0x00, [], tcpErrorCode: 1);
        Assert.Throws<IOException>(() => FinsMessage.ParseResponse(response));
    }

    // ==========================================================================
    // GetLengthFromHeader
    // ==========================================================================

    [Fact]
    public void GetLengthFromHeader_ReturnsCorrectLength()
    {
        var header = new byte[8];
        // FINS magic
        header[0] = 0x46; header[1] = 0x49; header[2] = 0x4E; header[3] = 0x53;
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4), 20); // payload = 20
        var totalLength = FinsMessage.GetLengthFromHeader(header);
        Assert.Equal(28, totalLength); // 8 + 20
    }

    [Fact]
    public void GetLengthFromHeader_TooShort_Throws()
    {
        Assert.Throws<InvalidOperationException>(
            () => FinsMessage.GetLengthFromHeader(new byte[4]));
    }

    // ==========================================================================
    // FinsResponse Tests
    // ==========================================================================

    [Fact]
    public void FinsResponse_GetErrorMessage_Success_Empty()
    {
        var resp = new FinsResponse(true, 0, 0, ReadOnlyMemory<byte>.Empty);
        Assert.Equal(string.Empty, resp.GetErrorMessage());
    }

    [Fact]
    public void FinsResponse_GetErrorMessage_Error()
    {
        var resp = new FinsResponse(false, 0x01, 0x01, ReadOnlyMemory<byte>.Empty);
        var msg = resp.GetErrorMessage();
        Assert.Contains("Local node error", msg);
    }

    [Fact]
    public void FinsResponse_GetErrorMessage_ReadNotPossible()
    {
        var resp = new FinsResponse(false, 0x20, 0x01, ReadOnlyMemory<byte>.Empty);
        Assert.Contains("Read not possible", resp.GetErrorMessage());
    }

    [Theory]
    [InlineData(0x00, 0x01, "FINS warning")]
    [InlineData(0x02, 0x01, "Destination node error")]
    [InlineData(0x03, 0x01, "Controller error")]
    [InlineData(0x04, 0x01, "Service unsupported")]
    [InlineData(0x05, 0x01, "Routing error")]
    [InlineData(0x10, 0x01, "Command format error")]
    [InlineData(0x11, 0x01, "Parameter error")]
    [InlineData(0x21, 0x01, "Write not possible")]
    [InlineData(0x22, 0x01, "Not executable")]
    [InlineData(0x23, 0x01, "No unit")]
    [InlineData(0x25, 0x01, "File/memory error")]
    [InlineData(0xFF, 0x01, "FINS error")]
    public void FinsResponse_GetErrorMessage_AllCodes(byte mainCode, byte subCode, string expectedSubstring)
    {
        var resp = new FinsResponse(false, mainCode, subCode, ReadOnlyMemory<byte>.Empty);
        Assert.Contains(expectedSubstring, resp.GetErrorMessage());
    }

    [Fact]
    public void ParseResponse_InvalidCommand_Throws()
    {
        using var writer = new PacketWriter(64);
        writer.WriteBytes(FinsMessage.FinsMagic);
        writer.WriteUInt32BE(20);
        writer.WriteUInt32BE(0x99999999); // invalid command
        writer.WriteUInt32BE(0); // no error
        writer.WriteBytes(new byte[12]);

        Assert.Throws<InvalidOperationException>(
            () => FinsMessage.ParseResponse(writer.ToArray()));
    }

    [Fact]
    public void ParseResponse_ShortFinsData_Throws()
    {
        using var writer = new PacketWriter(64);
        writer.WriteBytes(FinsMessage.FinsMagic);
        writer.WriteUInt32BE(10); // very small payload
        writer.WriteUInt32BE(FinsMessage.TcpCommandSendFrame);
        writer.WriteUInt32BE(0);
        writer.WriteBytes(new byte[2]); // too short for FINS header

        Assert.Throws<InvalidOperationException>(
            () => FinsMessage.ParseResponse(writer.ToArray()));
    }

    // ==========================================================================
    // Helpers
    // ==========================================================================

    private static byte[] BuildMockNodeAddressResponse(byte clientNode, byte serverNode,
        uint errorCode = 0)
    {
        using var writer = new PacketWriter(32);
        writer.WriteBytes(FinsMessage.FinsMagic);
        writer.WriteUInt32BE(16); // length
        writer.WriteUInt32BE(FinsMessage.TcpCommandNodeAddressResponse);
        writer.WriteUInt32BE(errorCode);
        writer.WriteUInt32BE(clientNode);
        writer.WriteUInt32BE(serverNode);
        return writer.ToArray();
    }

    private static byte[] BuildMockFinsResponse(byte mainCode, byte subCode,
        byte[] responseData, uint tcpErrorCode = 0)
    {
        using var writer = new PacketWriter(64);

        // FINS header + command echo + response codes + data
        using var finsPayloadWriter = new PacketWriter(32);

        // FINS header (10 bytes)
        finsPayloadWriter.WriteUInt8(0xC0); // ICF: response
        finsPayloadWriter.WriteUInt8(0x00); // RSV
        finsPayloadWriter.WriteUInt8(0x02); // GCT
        finsPayloadWriter.WriteUInt8(0x00); // DNA
        finsPayloadWriter.WriteUInt8(0x0A); // DA1
        finsPayloadWriter.WriteUInt8(0x00); // DA2
        finsPayloadWriter.WriteUInt8(0x00); // SNA
        finsPayloadWriter.WriteUInt8(0x01); // SA1
        finsPayloadWriter.WriteUInt8(0x00); // SA2
        finsPayloadWriter.WriteUInt8(0x01); // SID

        // Command echo
        finsPayloadWriter.WriteUInt16BE(FinsCommands.MemoryAreaRead);

        // Response codes
        finsPayloadWriter.WriteUInt8(mainCode);
        finsPayloadWriter.WriteUInt8(subCode);

        // Response data
        finsPayloadWriter.WriteBytes(responseData);

        var finsPayload = finsPayloadWriter.ToArray();

        // TCP header
        writer.WriteBytes(FinsMessage.FinsMagic);
        writer.WriteUInt32BE((uint)(8 + finsPayload.Length));
        writer.WriteUInt32BE(FinsMessage.TcpCommandSendFrame);
        writer.WriteUInt32BE(tcpErrorCode);
        writer.WriteBytes(finsPayload);

        return writer.ToArray();
    }
}
