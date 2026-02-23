using SimplePLCDriverCore.Protocols.EtherNetIP.Pccc;

namespace SimplePLCDriverCore.Tests.EtherNetIP.Pccc;

public class PcccCommandTests
{
    private const uint TestSerial = 0x12345678;
    private const ushort TestVendor = 0x0001;

    // ==========================================================================
    // Build Read Request
    // ==========================================================================

    [Fact]
    public void BuildReadRequest_IntegerFile()
    {
        var address = PcccAddress.Parse("N7:0");
        var request = PcccCommand.BuildReadRequest(address, transactionId: 1, TestSerial, TestVendor);

        Assert.NotNull(request);
        Assert.True(request.Length > 0);

        // Verify CIP service byte (Execute PCCC = 0x4B)
        Assert.Equal(0x4B, request[0]);

        // Find the PCCC command byte in the request
        // After CIP header (service + path size + path) + requestor ID
        // The PCCC command should be 0x0F
        Assert.Contains((byte)0x0F, request);
    }

    [Fact]
    public void BuildReadRequest_ContainsFileNumber()
    {
        var address = PcccAddress.Parse("N7:5");
        var request = PcccCommand.BuildReadRequest(address, transactionId: 1, TestSerial, TestVendor);

        // File number (7) should appear in the address fields
        Assert.Contains((byte)7, request);
        // Element number (5) should appear
        Assert.Contains((byte)5, request);
    }

    [Fact]
    public void BuildReadRequest_FloatFile()
    {
        var address = PcccAddress.Parse("F8:1");
        var request = PcccCommand.BuildReadRequest(address, transactionId: 2, TestSerial, TestVendor);

        Assert.NotNull(request);
        Assert.True(request.Length > 0);

        // File type code for Float (0x8A) should be in the request
        Assert.Contains((byte)PcccFileType.Float, request);
    }

    [Fact]
    public void BuildReadRequest_CorrectReadSize_Integer()
    {
        var address = PcccAddress.Parse("N7:0");
        var request = PcccCommand.BuildReadRequest(address, transactionId: 1, TestSerial, TestVendor);

        // Read size for integer is 2 bytes
        // The read size byte should be present after the function code
        Assert.Contains((byte)2, request);
    }

    [Fact]
    public void BuildReadRequest_CorrectReadSize_Float()
    {
        var address = PcccAddress.Parse("F8:0");
        var request = PcccCommand.BuildReadRequest(address, transactionId: 1, TestSerial, TestVendor);

        // Read size for float is 4 bytes
        Assert.Contains((byte)4, request);
    }

    [Fact]
    public void BuildReadRequest_CorrectReadSize_Timer()
    {
        var address = PcccAddress.Parse("T4:0");
        var request = PcccCommand.BuildReadRequest(address, transactionId: 1, TestSerial, TestVendor);

        // Read size for timer is 6 bytes
        Assert.Contains((byte)6, request);
    }

    [Fact]
    public void BuildReadRequest_Timer_SubElement()
    {
        var address = PcccAddress.Parse("T4:0.ACC");
        var request = PcccCommand.BuildReadRequest(address, transactionId: 1, TestSerial, TestVendor);

        // Sub-element read is 2 bytes
        Assert.Contains((byte)2, request);
    }

    [Fact]
    public void BuildReadRequest_DifferentTransactionIds()
    {
        var address = PcccAddress.Parse("N7:0");
        var request1 = PcccCommand.BuildReadRequest(address, transactionId: 1, TestSerial, TestVendor);
        var request2 = PcccCommand.BuildReadRequest(address, transactionId: 2, TestSerial, TestVendor);

        // Requests should differ (at least in the transaction ID bytes)
        Assert.NotEqual(request1, request2);
    }

    // ==========================================================================
    // Build Write Request
    // ==========================================================================

    [Fact]
    public void BuildWriteRequest_IntegerFile()
    {
        var address = PcccAddress.Parse("N7:0");
        var data = new byte[] { 0x2A, 0x00 }; // 42 in LE
        var request = PcccCommand.BuildWriteRequest(address, data, transactionId: 1, TestSerial, TestVendor);

        Assert.NotNull(request);
        Assert.True(request.Length > 0);

        // CIP Execute PCCC service
        Assert.Equal(0x4B, request[0]);

        // Data should appear at the end of the request
        Assert.Equal(0x2A, request[^2]);
        Assert.Equal(0x00, request[^1]);
    }

    [Fact]
    public void BuildWriteRequest_FloatFile()
    {
        var address = PcccAddress.Parse("F8:1");
        var data = new byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian(data, 3.14f);
        var request = PcccCommand.BuildWriteRequest(address, data, transactionId: 1, TestSerial, TestVendor);

        Assert.NotNull(request);
        // The last 4 bytes should be the float data
        Assert.Equal(data[0], request[^4]);
        Assert.Equal(data[1], request[^3]);
        Assert.Equal(data[2], request[^2]);
        Assert.Equal(data[3], request[^1]);
    }

    [Fact]
    public void BuildWriteRequest_ContainsWriteFunctionCode()
    {
        var address = PcccAddress.Parse("N7:0");
        var data = new byte[] { 0x00, 0x00 };
        var request = PcccCommand.BuildWriteRequest(address, data, transactionId: 1, TestSerial, TestVendor);

        // Write function code 0xAA should be present
        Assert.Contains(PcccTypes.FnProtectedTypedLogicalWrite3, request);
    }

    // ==========================================================================
    // Parse Response
    // ==========================================================================

    [Fact]
    public void ParseResponse_Success()
    {
        // Build a mock PCCC response:
        // requestor_id_length(1) + requestor_id(6) + command(1) + status(1) + txn_id(2) + data
        var response = new byte[]
        {
            6,                          // Requestor ID length
            0x01, 0x00,                 // Vendor ID
            0x78, 0x56, 0x34, 0x12,     // Serial number
            0x4F,                       // Command reply (0x0F | 0x40)
            0x00,                       // Status: success
            0x01, 0x00,                 // Transaction ID
            0x2A, 0x00,                 // Response data: 42
        };

        var result = PcccCommand.ParseResponse(response);

        Assert.True(result.IsSuccess);
        Assert.Equal(0x4F, result.Command);
        Assert.Equal(1, result.TransactionId);
        Assert.Equal(2, result.Data.Length);
        Assert.Equal(0x2A, result.Data.Span[0]);
    }

    [Fact]
    public void ParseResponse_Error()
    {
        var response = new byte[]
        {
            6,                          // Requestor ID length
            0x01, 0x00,                 // Vendor ID
            0x78, 0x56, 0x34, 0x12,     // Serial number
            0x4F,                       // Command reply
            0x50,                       // Status: address problem
            0x01, 0x00,                 // Transaction ID
        };

        var result = PcccCommand.ParseResponse(response);

        Assert.False(result.IsSuccess);
        Assert.Equal(0x50, result.Status);
        Assert.Contains("address", result.GetErrorMessage(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ParseResponse_FloatData()
    {
        var floatBytes = new byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian(floatBytes, 3.14f);

        var response = new byte[11 + 4];
        response[0] = 6;                    // Requestor ID length
        response[1] = 0x01; response[2] = 0x00; // Vendor ID
        response[3] = 0x78; response[4] = 0x56; response[5] = 0x34; response[6] = 0x12;
        response[7] = 0x4F;                 // Command reply
        response[8] = 0x00;                 // Status: success
        response[9] = 0x02; response[10] = 0x00; // Transaction ID
        Array.Copy(floatBytes, 0, response, 11, 4);

        var result = PcccCommand.ParseResponse(response);

        Assert.True(result.IsSuccess);
        Assert.Equal(4, result.Data.Length);
    }

    [Fact]
    public void ParseResponse_TooShort_ThrowsException()
    {
        var response = new byte[] { 0x01, 0x02 };
        Assert.Throws<InvalidDataException>(() => PcccCommand.ParseResponse(response));
    }

    // ==========================================================================
    // WrapInUnconnectedSend
    // ==========================================================================

    [Fact]
    public void WrapInUnconnectedSend_ProducesValidMessage()
    {
        var innerRequest = new byte[] { 0x4B, 0x02, 0x20, 0x67, 0x24, 0x01 };
        var wrapped = PcccCommand.WrapInUnconnectedSend(innerRequest, slot: 0);

        Assert.NotNull(wrapped);
        Assert.True(wrapped.Length > innerRequest.Length);

        // Unconnected Send service code
        Assert.Equal(0x52, wrapped[0]);
    }

    [Fact]
    public void WrapInUnconnectedSend_IncludesRoutePath()
    {
        var innerRequest = new byte[] { 0x4B, 0x02, 0x20, 0x67, 0x24, 0x01 };
        var wrapped = PcccCommand.WrapInUnconnectedSend(innerRequest, slot: 2);

        // Route path should contain backplane port (0x01) and slot (0x02)
        Assert.Contains((byte)0x01, wrapped); // Backplane port
        Assert.Contains((byte)0x02, wrapped); // Slot 2
    }
}
