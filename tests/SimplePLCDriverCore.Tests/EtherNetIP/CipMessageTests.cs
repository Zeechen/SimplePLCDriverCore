using System.Buffers.Binary;
using SimplePLCDriverCore.Common.Buffers;
using SimplePLCDriverCore.Protocols.EtherNetIP.Cip;

namespace SimplePLCDriverCore.Tests.EtherNetIP;

public class CipMessageTests
{
    [Fact]
    public void ParseResponse_Success()
    {
        // Build a mock CIP response: ReadTag reply, success, DINT value = 42
        using var writer = new PacketWriter();
        writer.WriteUInt8(CipServices.ReadTag | CipServices.ReplyMask); // service reply
        writer.WriteUInt8(0);                   // reserved
        writer.WriteUInt8(0x00);                // general status = success
        writer.WriteUInt8(0);                   // additional status size = 0
        // Response data: type code (2) + pad (2) + value (4)
        writer.WriteUInt16LE(CipDataTypes.Dint);
        writer.WriteUInt16LE(0); // padding (not always present but typical)
        writer.WriteInt32LE(42);

        var response = CipMessage.ParseResponse(writer.ToArray());

        Assert.Equal(CipServices.ReadTag, response.Service);
        Assert.Equal(CipGeneralStatus.Success, response.GeneralStatus);
        Assert.True(response.IsSuccess);
        Assert.False(response.IsPartialTransfer);
        Assert.Empty(response.AdditionalStatus);
        Assert.True(response.Data.Length > 0);
    }

    [Fact]
    public void ParseResponse_WithError()
    {
        using var writer = new PacketWriter();
        writer.WriteUInt8(CipServices.ReadTag | CipServices.ReplyMask);
        writer.WriteUInt8(0);
        writer.WriteUInt8(0x05); // PathDestinationUnknown
        writer.WriteUInt8(0);    // no additional status

        var response = CipMessage.ParseResponse(writer.ToArray());

        Assert.Equal(CipGeneralStatus.PathDestinationUnknown, response.GeneralStatus);
        Assert.False(response.IsSuccess);
    }

    [Fact]
    public void ParseResponse_WithAdditionalStatus()
    {
        using var writer = new PacketWriter();
        writer.WriteUInt8(CipServices.ReadTag | CipServices.ReplyMask);
        writer.WriteUInt8(0);
        writer.WriteUInt8(0x01); // ConnectionFailure
        writer.WriteUInt8(1);    // 1 word of additional status
        writer.WriteUInt16LE(0x0100); // extended error code

        var response = CipMessage.ParseResponse(writer.ToArray());

        Assert.Equal(CipGeneralStatus.ConnectionFailure, response.GeneralStatus);
        Assert.Single(response.AdditionalStatus);
        Assert.Equal((ushort)0x0100, response.AdditionalStatus[0]);
    }

    [Fact]
    public void ParseResponse_PartialTransfer()
    {
        using var writer = new PacketWriter();
        writer.WriteUInt8(CipServices.ReadTagFragmented | CipServices.ReplyMask);
        writer.WriteUInt8(0);
        writer.WriteUInt8(0x06); // PartialTransfer
        writer.WriteUInt8(0);
        writer.WriteBytes(new byte[] { 0x01, 0x02, 0x03 }); // partial data

        var response = CipMessage.ParseResponse(writer.ToArray());

        Assert.True(response.IsSuccess); // partial transfer is still "success"
        Assert.True(response.IsPartialTransfer);
        Assert.Equal(3, response.Data.Length);
    }

    [Fact]
    public void BuildReadTagRequest_SimpleTag()
    {
        var request = CipMessage.BuildReadTagRequest("MyDINT", elementCount: 1);

        // Service code
        Assert.Equal(CipServices.ReadTag, request[0]); // 0x4C

        // Path size (in words)
        var pathSizeWords = request[1];
        Assert.True(pathSizeWords > 0);

        // Verify it starts with symbolic segment
        Assert.Equal(0x91, request[2]); // symbolic segment type

        // Element count at end (last 2 bytes)
        var elemCount = BinaryPrimitives.ReadUInt16LittleEndian(
            request.AsSpan(request.Length - 2));
        Assert.Equal(1, elemCount);
    }

    [Fact]
    public void BuildReadTagRequest_MultipleElements()
    {
        var request = CipMessage.BuildReadTagRequest("MyArray", elementCount: 10);

        var elemCount = BinaryPrimitives.ReadUInt16LittleEndian(
            request.AsSpan(request.Length - 2));
        Assert.Equal(10, elemCount);
    }

    [Fact]
    public void BuildWriteTagRequest_DintValue()
    {
        var data = BitConverter.GetBytes(42);
        var request = CipMessage.BuildWriteTagRequest("MyDINT", CipDataTypes.Dint, 1, data);

        // Service code
        Assert.Equal(CipServices.WriteTag, request[0]); // 0x4D

        // Path starts at offset 2
        Assert.Equal(0x91, request[2]);

        // After the path: data type, element count, data
        // We need to find where the path ends
        var pathSizeWords = request[1];
        var pathEnd = 2 + pathSizeWords * 2;

        // Data type (after path)
        var dataType = BinaryPrimitives.ReadUInt16LittleEndian(request.AsSpan(pathEnd));
        Assert.Equal(CipDataTypes.Dint, dataType);

        // Element count
        var elemCount = BinaryPrimitives.ReadUInt16LittleEndian(request.AsSpan(pathEnd + 2));
        Assert.Equal(1, elemCount);

        // Data value (42 as DINT = 4 bytes LE)
        var value = BinaryPrimitives.ReadInt32LittleEndian(request.AsSpan(pathEnd + 4));
        Assert.Equal(42, value);
    }

    [Fact]
    public void BuildReadTagFragmentedRequest_HasOffset()
    {
        var request = CipMessage.BuildReadTagFragmentedRequest("BigTag", 1, offset: 500);

        Assert.Equal(CipServices.ReadTagFragmented, request[0]); // 0x52

        // Offset is the last 4 bytes
        var offset = BinaryPrimitives.ReadUInt32LittleEndian(
            request.AsSpan(request.Length - 4));
        Assert.Equal(500U, offset);
    }

    [Fact]
    public void BuildGetAttributeListRequest_StructureCorrect()
    {
        var attributes = new ushort[] { 1, 2, 7 };
        var request = CipMessage.BuildGetAttributeListRequest(
            CipClasses.Identity, 1, attributes);

        // Service = GetAttributeList (0x03)
        Assert.Equal(CipServices.GetAttributeList, request[0]);

        // Path should contain class 0x01, instance 1
        Assert.Equal(0x20, request[2]); // class segment
        Assert.Equal(0x01, request[3]); // Identity class
        Assert.Equal(0x24, request[4]); // instance segment
        Assert.Equal(0x01, request[5]); // instance 1

        // After path: attribute count (3) and attribute IDs
        var pathSizeWords = request[1];
        var attrStart = 2 + pathSizeWords * 2;

        var attrCount = BinaryPrimitives.ReadUInt16LittleEndian(request.AsSpan(attrStart));
        Assert.Equal(3, attrCount);

        Assert.Equal(1, BinaryPrimitives.ReadUInt16LittleEndian(request.AsSpan(attrStart + 2)));
        Assert.Equal(2, BinaryPrimitives.ReadUInt16LittleEndian(request.AsSpan(attrStart + 4)));
        Assert.Equal(7, BinaryPrimitives.ReadUInt16LittleEndian(request.AsSpan(attrStart + 6)));
    }

    [Fact]
    public void GetErrorMessage_FormatsCorrectly()
    {
        var response = new CipResponse(
            CipServices.ReadTag,
            CipGeneralStatus.PathSegmentError,
            new ushort[] { 0x0001 },
            ReadOnlyMemory<byte>.Empty);

        var msg = response.GetErrorMessage();
        Assert.Contains("PathSegmentError", msg);
        Assert.Contains("0x04", msg);
        Assert.Contains("0x0001", msg);
    }

    [Fact]
    public void GetErrorMessage_WithoutAdditionalStatus()
    {
        var response = new CipResponse(
            CipServices.ReadTag,
            CipGeneralStatus.PathSegmentError,
            Array.Empty<ushort>(),
            ReadOnlyMemory<byte>.Empty);

        var msg = response.GetErrorMessage();
        Assert.Contains("PathSegmentError", msg);
        Assert.DoesNotContain("extended", msg);
    }

    // --- GetUserFriendlyMessage tests ---

    [Theory]
    [InlineData(0x01, "Connection to PLC failed")]
    [InlineData(0x02, "PLC resource unavailable")]
    [InlineData(0x03, "Invalid parameter value")]
    [InlineData(0x04, "Tag not found in PLC")]
    [InlineData(0x05, "Tag path destination unknown")]
    [InlineData(0x07, "Connection to PLC lost")]
    [InlineData(0x08, "Operation not supported by PLC")]
    [InlineData(0x09, "Invalid attribute value")]
    [InlineData(0x0F, "Access denied")]
    [InlineData(0x10, "PLC is in a conflicting state")]
    [InlineData(0x13, "Not enough data")]
    [InlineData(0x15, "Too much data")]
    [InlineData(0x16, "Tag does not exist")]
    [InlineData(0x19, "Write operation failed")]
    [InlineData(0x26, "Invalid tag path size")]
    [InlineData(0x28, "Invalid UDT member")]
    [InlineData(0x29, "UDT member is read-only")]
    public void GetUserFriendlyMessage_ReturnsExpected(byte statusCode, string expectedSubstring)
    {
        var status = (CipGeneralStatus)statusCode;
        var response = new CipResponse(0, status, Array.Empty<ushort>(), ReadOnlyMemory<byte>.Empty);
        var msg = response.GetUserFriendlyMessage();
        Assert.Contains(expectedSubstring, msg);
    }

    [Fact]
    public void GetUserFriendlyMessage_UnknownStatus_ReturnsHexCode()
    {
        var response = new CipResponse(0, (CipGeneralStatus)0xFE, Array.Empty<ushort>(), ReadOnlyMemory<byte>.Empty);
        var msg = response.GetUserFriendlyMessage();
        Assert.Contains("0xFE", msg);
    }

    // --- BuildUnconnectedSend ---

    [Fact]
    public void BuildUnconnectedSend_ProducesValidRequest()
    {
        var innerRequest = CipMessage.BuildReadTagRequest("Test");
        var ucmm = CipMessage.BuildUnconnectedSend(innerRequest, slot: 0);

        Assert.Equal(0x52, ucmm[0]); // Unconnected Send service code
        Assert.True(ucmm.Length > innerRequest.Length);
    }

    // --- BuildWriteTagFragmentedRequest ---

    [Fact]
    public void BuildWriteTagFragmentedRequest_HasOffsetAndData()
    {
        var data = new byte[] { 0x01, 0x02, 0x03, 0x04 };
        var request = CipMessage.BuildWriteTagFragmentedRequest(
            "BigTag", CipDataTypes.Dint, 1, offset: 100, data);

        Assert.Equal(CipServices.WriteTagFragmented, request[0]);
        Assert.True(request.Length > data.Length);
    }

    // --- ForwardOpen tests ---

    [Fact]
    public void BuildLargeForwardOpen_ProducesValidRequest()
    {
        var (request, serialNumber) = ForwardOpen.BuildLargeForwardOpen(slot: 0, originatorSerialNumber: 12345);

        Assert.Equal(CipServices.LargeForwardOpen, request[0]);
        Assert.True(serialNumber > 0);
        Assert.True(request.Length > 40);
    }

    [Fact]
    public void BuildForwardOpen_ProducesValidRequest()
    {
        var (request, serialNumber) = ForwardOpen.BuildForwardOpen(slot: 0, originatorSerialNumber: 12345);

        Assert.Equal(CipServices.ForwardOpen, request[0]);
        Assert.True(serialNumber > 0);
        Assert.True(request.Length > 30);
    }

    [Fact]
    public void BuildForwardClose_ProducesValidRequest()
    {
        var request = ForwardOpen.BuildForwardClose(
            connectionSerialNumber: 1,
            vendorId: 0x0001,
            originatorSerialNumber: 12345,
            slot: 0);

        Assert.Equal(CipServices.ForwardClose, request[0]);
        Assert.True(request.Length > 10);
    }

    [Fact]
    public void ForwardOpen_ParseResponse_ExtractsConnectionIds()
    {
        using var writer = new PacketWriter();
        writer.WriteUInt32LE(0x11223344); // O->T Connection ID
        writer.WriteUInt32LE(0x55667788); // T->O Connection ID
        writer.WriteUInt16LE(1);          // Connection Serial Number
        writer.WriteUInt16LE(0x0001);     // Originator Vendor ID
        writer.WriteUInt32LE(12345);      // Originator Serial Number
        writer.WriteUInt32LE(2000000);    // O->T Actual Packet Rate
        writer.WriteUInt32LE(2000000);    // T->O Actual Packet Rate

        var result = ForwardOpen.ParseResponse(writer.ToArray());

        Assert.Equal(0x11223344U, result.OtoTConnectionId);
        Assert.Equal(0x55667788U, result.TtoOConnectionId);
        Assert.Equal((ushort)1, result.ConnectionSerialNumber);
        Assert.Equal((ushort)0x0001, result.OriginatorVendorId);
        Assert.Equal(12345U, result.OriginatorSerialNumber);
    }

    [Fact]
    public void BuildLargeForwardOpen_SerialNumbersIncrement()
    {
        var (_, sn1) = ForwardOpen.BuildLargeForwardOpen(0, 1);
        var (_, sn2) = ForwardOpen.BuildLargeForwardOpen(0, 1);
        Assert.NotEqual(sn1, sn2);
    }
}
