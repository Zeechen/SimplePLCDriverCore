using System.Buffers.Binary;
using SimplePLCDriverCore.Abstractions;
using SimplePLCDriverCore.Common;
using SimplePLCDriverCore.Common.Buffers;
using SimplePLCDriverCore.Common.Transport;
using SimplePLCDriverCore.Drivers;
using SimplePLCDriverCore.Protocols.EtherNetIP;
using SimplePLCDriverCore.Protocols.EtherNetIP.Cip;
using SimplePLCDriverCore.Protocols.EtherNetIP.Pccc;
using SimplePLCDriverCore.Tests.EtherNetIP;

namespace SimplePLCDriverCore.Tests.Drivers;

public class SlcDriverTests
{
    private const uint SessionHandle = 0xABCD;

    private static (FakeTransport Transport, SlcDriver Driver) CreateTestDriver(
        SlcPlcType plcType = SlcPlcType.Slc500)
    {
        var transport = new FakeTransport();
        var options = new ConnectionOptions();
        var driver = new SlcDriver("127.0.0.1", options, () => transport, plcType);
        return (transport, driver);
    }

    private static void EnqueueConnectSequence(FakeTransport transport)
    {
        // SLC only needs RegisterSession (no Forward Open)
        transport.EnqueueResponse(MockEipResponse.BuildRegisterSessionResponse(SessionHandle));
    }

    /// <summary>
    /// Build a mock CIP Execute PCCC response wrapped in SendRRData.
    /// This simulates the PLC responding to a PCCC read/write request.
    /// </summary>
    private static byte[] BuildPcccCipResponse(
        byte pcccCommand, byte pcccStatus, ushort transactionId,
        byte[]? data = null)
    {
        // CIP response: Execute PCCC reply
        using var cipWriter = new PacketWriter();
        cipWriter.WriteUInt8(0x4B | CipServices.ReplyMask); // Execute PCCC reply
        cipWriter.WriteUInt8(0); // reserved
        cipWriter.WriteUInt8(0); // CIP general status: success
        cipWriter.WriteUInt8(0); // additional status size

        // PCCC response data within CIP
        cipWriter.WriteUInt8(6); // Requestor ID length
        cipWriter.WriteUInt16LE(0x0001); // Vendor ID
        cipWriter.WriteUInt32LE(0x12345678); // Serial

        cipWriter.WriteUInt8(pcccCommand); // PCCC command reply
        cipWriter.WriteUInt8(pcccStatus);   // PCCC status

        cipWriter.WriteUInt16LE(transactionId);

        if (data != null)
            cipWriter.WriteBytes(data);

        // Wrap in Unconnected Send response (SendRRData)
        // The CIP response from the UnconnectedSend wrapper is the inner CIP response
        // But SendRoutedUnconnectedAsync wraps in Unconnected Send and expects
        // SendRRData response. We need to build the full response chain.

        // Actually, the SendRoutedUnconnectedAsync sends via SendRRData,
        // and the response comes back as a SendRRData with the inner CIP reply.
        return MockEipResponse.BuildSendRRDataResponse(SessionHandle, cipWriter.ToArray());
    }

    // ==========================================================================
    // Connection Tests
    // ==========================================================================

    [Fact]
    public async Task Connect_SetsIsConnected()
    {
        var (transport, driver) = CreateTestDriver();
        EnqueueConnectSequence(transport);

        await using (driver)
        {
            await driver.ConnectAsync();
            Assert.True(driver.IsConnected);
        }
    }

    [Fact]
    public async Task Disconnect_ClearsIsConnected()
    {
        var (transport, driver) = CreateTestDriver();
        EnqueueConnectSequence(transport);

        await driver.ConnectAsync();
        Assert.True(driver.IsConnected);

        await driver.DisconnectAsync();
        Assert.False(driver.IsConnected);

        await driver.DisposeAsync();
    }

    [Fact]
    public async Task ReadAsync_NotConnected_Throws()
    {
        var (_, driver) = CreateTestDriver();

        await using (driver)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => driver.ReadAsync("N7:0").AsTask());
        }
    }

    // ==========================================================================
    // Read Tests
    // ==========================================================================

    [Fact]
    public async Task ReadAsync_Integer_ReturnsValue()
    {
        var (transport, driver) = CreateTestDriver();
        EnqueueConnectSequence(transport);

        await using (driver)
        {
            await driver.ConnectAsync();

            // Enqueue PCCC response with integer value 42
            var intData = new byte[2];
            BinaryPrimitives.WriteInt16LittleEndian(intData, 42);
            transport.EnqueueResponse(BuildPcccCipResponse(0x4F, 0x00, 1, intData));

            var result = await driver.ReadAsync("N7:0");

            Assert.True(result.IsSuccess, $"Read failed: {result.Error}");
            Assert.Equal(42, result.Value.AsInt32());
            Assert.Equal("INT", result.TypeName);
        }
    }

    [Fact]
    public async Task ReadAsync_Float_ReturnsValue()
    {
        var (transport, driver) = CreateTestDriver();
        EnqueueConnectSequence(transport);

        await using (driver)
        {
            await driver.ConnectAsync();

            var floatData = new byte[4];
            BinaryPrimitives.WriteSingleLittleEndian(floatData, 3.14f);
            transport.EnqueueResponse(BuildPcccCipResponse(0x4F, 0x00, 1, floatData));

            var result = await driver.ReadAsync("F8:1");

            Assert.True(result.IsSuccess);
            Assert.Equal(3.14f, result.Value.AsSingle(), 0.001f);
            Assert.Equal("FLOAT", result.TypeName);
        }
    }

    [Fact]
    public async Task ReadAsync_Bit_ReturnsTrue()
    {
        var (transport, driver) = CreateTestDriver();
        EnqueueConnectSequence(transport);

        await using (driver)
        {
            await driver.ConnectAsync();

            // Word with bit 5 set
            var bitData = new byte[2];
            BinaryPrimitives.WriteUInt16LittleEndian(bitData, 0x0020);
            transport.EnqueueResponse(BuildPcccCipResponse(0x4F, 0x00, 1, bitData));

            var result = await driver.ReadAsync("B3:0/5");

            Assert.True(result.IsSuccess);
            Assert.True(result.Value.AsBoolean());
            Assert.Equal("BIT", result.TypeName);
        }
    }

    [Fact]
    public async Task ReadAsync_Bit_ReturnsFalse()
    {
        var (transport, driver) = CreateTestDriver();
        EnqueueConnectSequence(transport);

        await using (driver)
        {
            await driver.ConnectAsync();

            // Word with bit 5 clear (other bits set)
            var bitData = new byte[2];
            BinaryPrimitives.WriteUInt16LittleEndian(bitData, 0xFFDF);
            transport.EnqueueResponse(BuildPcccCipResponse(0x4F, 0x00, 1, bitData));

            var result = await driver.ReadAsync("B3:0/5");

            Assert.True(result.IsSuccess);
            Assert.False(result.Value.AsBoolean());
        }
    }

    [Fact]
    public async Task ReadAsync_TimerAccumulator_ReturnsSubElement()
    {
        var (transport, driver) = CreateTestDriver();
        EnqueueConnectSequence(transport);

        await using (driver)
        {
            await driver.ConnectAsync();

            var accData = new byte[2];
            BinaryPrimitives.WriteInt16LittleEndian(accData, 750);
            transport.EnqueueResponse(BuildPcccCipResponse(0x4F, 0x00, 1, accData));

            var result = await driver.ReadAsync("T4:0.ACC");

            Assert.True(result.IsSuccess);
            Assert.Equal(750, result.Value.AsInt32());
            Assert.Equal("INT", result.TypeName);
        }
    }

    [Fact]
    public async Task ReadAsync_TimerFull_ReturnsStructure()
    {
        var (transport, driver) = CreateTestDriver();
        EnqueueConnectSequence(transport);

        await using (driver)
        {
            await driver.ConnectAsync();

            var timerData = new byte[6];
            BinaryPrimitives.WriteInt16LittleEndian(timerData, unchecked((short)0xE000)); // EN, TT, DN
            BinaryPrimitives.WriteInt16LittleEndian(timerData.AsSpan(2), 1000); // PRE
            BinaryPrimitives.WriteInt16LittleEndian(timerData.AsSpan(4), 500);  // ACC
            transport.EnqueueResponse(BuildPcccCipResponse(0x4F, 0x00, 1, timerData));

            var result = await driver.ReadAsync("T4:0");

            Assert.True(result.IsSuccess);
            Assert.Equal("TIMER", result.TypeName);

            var members = result.Value.AsStructure();
            Assert.NotNull(members);
            Assert.Equal(1000, members!["PRE"].AsInt32());
            Assert.Equal(500, members["ACC"].AsInt32());
            Assert.True(members["EN"].AsBoolean());
            Assert.True(members["DN"].AsBoolean());
        }
    }

    [Fact]
    public async Task ReadAsync_String_ReturnsText()
    {
        var (transport, driver) = CreateTestDriver();
        EnqueueConnectSequence(transport);

        await using (driver)
        {
            await driver.ConnectAsync();

            var stringData = new byte[84];
            BinaryPrimitives.WriteInt16LittleEndian(stringData, 5);
            stringData[2] = (byte)'H';
            stringData[3] = (byte)'e';
            stringData[4] = (byte)'l';
            stringData[5] = (byte)'l';
            stringData[6] = (byte)'o';
            transport.EnqueueResponse(BuildPcccCipResponse(0x4F, 0x00, 1, stringData));

            var result = await driver.ReadAsync("ST9:0");

            Assert.True(result.IsSuccess);
            Assert.Equal("Hello", result.Value.AsString());
            Assert.Equal("STRING", result.TypeName);
        }
    }

    [Fact]
    public async Task ReadAsync_InvalidAddress_ReturnsFailure()
    {
        var (transport, driver) = CreateTestDriver();
        EnqueueConnectSequence(transport);

        await using (driver)
        {
            await driver.ConnectAsync();

            var result = await driver.ReadAsync("INVALID");

            Assert.False(result.IsSuccess);
            Assert.Contains("Invalid address", result.Error);
        }
    }

    [Fact]
    public async Task ReadAsync_PcccError_ReturnsFailure()
    {
        var (transport, driver) = CreateTestDriver();
        EnqueueConnectSequence(transport);

        await using (driver)
        {
            await driver.ConnectAsync();

            // PCCC error: address problem (0x50)
            transport.EnqueueResponse(BuildPcccCipResponse(0x4F, 0x50, 1));

            var result = await driver.ReadAsync("N7:0");

            Assert.False(result.IsSuccess);
            Assert.Contains("address", result.Error!, StringComparison.OrdinalIgnoreCase);
        }
    }

    // ==========================================================================
    // Write Tests
    // ==========================================================================

    [Fact]
    public async Task WriteAsync_Integer_Success()
    {
        var (transport, driver) = CreateTestDriver();
        EnqueueConnectSequence(transport);

        await using (driver)
        {
            await driver.ConnectAsync();

            // PCCC write success response
            transport.EnqueueResponse(BuildPcccCipResponse(0x4F, 0x00, 1));

            var result = await driver.WriteAsync("N7:0", 100);

            Assert.True(result.IsSuccess, $"Write failed: {result.Error}");
        }
    }

    [Fact]
    public async Task WriteAsync_Float_Success()
    {
        var (transport, driver) = CreateTestDriver();
        EnqueueConnectSequence(transport);

        await using (driver)
        {
            await driver.ConnectAsync();

            transport.EnqueueResponse(BuildPcccCipResponse(0x4F, 0x00, 1));

            var result = await driver.WriteAsync("F8:1", 3.14f);

            Assert.True(result.IsSuccess);
        }
    }

    [Fact]
    public async Task WriteAsync_Bit_PerformsReadModifyWrite()
    {
        var (transport, driver) = CreateTestDriver();
        EnqueueConnectSequence(transport);

        await using (driver)
        {
            await driver.ConnectAsync();

            // First: read response for the current word value (read-modify-write)
            var currentWord = new byte[2];
            BinaryPrimitives.WriteUInt16LittleEndian(currentWord, 0x0000); // no bits set
            transport.EnqueueResponse(BuildPcccCipResponse(0x4F, 0x00, 1, currentWord));

            // Second: write response (success)
            transport.EnqueueResponse(BuildPcccCipResponse(0x4F, 0x00, 2));

            var result = await driver.WriteAsync("B3:0/5", true);

            Assert.True(result.IsSuccess, $"Write failed: {result.Error}");
        }
    }

    [Fact]
    public async Task WriteAsync_InvalidAddress_ReturnsFailure()
    {
        var (transport, driver) = CreateTestDriver();
        EnqueueConnectSequence(transport);

        await using (driver)
        {
            await driver.ConnectAsync();

            var result = await driver.WriteAsync("INVALID", 42);

            Assert.False(result.IsSuccess);
            Assert.Contains("Invalid address", result.Error);
        }
    }

    // ==========================================================================
    // Batch Operations
    // ==========================================================================

    [Fact]
    public async Task ReadAsync_Batch_ReturnsAllResults()
    {
        var (transport, driver) = CreateTestDriver();
        EnqueueConnectSequence(transport);

        await using (driver)
        {
            await driver.ConnectAsync();

            // Enqueue individual responses for each tag
            var intData = new byte[2];
            BinaryPrimitives.WriteInt16LittleEndian(intData, 42);
            transport.EnqueueResponse(BuildPcccCipResponse(0x4F, 0x00, 1, intData));

            var floatData = new byte[4];
            BinaryPrimitives.WriteSingleLittleEndian(floatData, 3.14f);
            transport.EnqueueResponse(BuildPcccCipResponse(0x4F, 0x00, 2, floatData));

            var results = await driver.ReadAsync(["N7:0", "F8:1"]);

            Assert.Equal(2, results.Length);
            Assert.True(results[0].IsSuccess);
            Assert.True(results[1].IsSuccess);
            Assert.Equal(42, results[0].Value.AsInt32());
            Assert.Equal(3.14f, results[1].Value.AsSingle(), 0.001f);
        }
    }

    [Fact]
    public async Task WriteAsync_Batch_ReturnsAllResults()
    {
        var (transport, driver) = CreateTestDriver();
        EnqueueConnectSequence(transport);

        await using (driver)
        {
            await driver.ConnectAsync();

            transport.EnqueueResponse(BuildPcccCipResponse(0x4F, 0x00, 1));
            transport.EnqueueResponse(BuildPcccCipResponse(0x4F, 0x00, 2));

            var results = await driver.WriteAsync([
                ("N7:0", (object)100),
                ("F8:1", (object)3.14f),
            ]);

            Assert.Equal(2, results.Length);
            Assert.True(results[0].IsSuccess);
            Assert.True(results[1].IsSuccess);
        }
    }

    // ==========================================================================
    // Unsupported Operations
    // ==========================================================================

    [Fact]
    public async Task ReadJsonAsync_ReturnsFailure()
    {
        var (transport, driver) = CreateTestDriver();
        EnqueueConnectSequence(transport);

        await using (driver)
        {
            await driver.ConnectAsync();

            var result = await driver.ReadJsonAsync("N7:0");
            Assert.False(result.IsSuccess);
            Assert.Contains("not supported", result.Error!, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task WriteJsonAsync_ReturnsFailure()
    {
        var (transport, driver) = CreateTestDriver();
        EnqueueConnectSequence(transport);

        await using (driver)
        {
            await driver.ConnectAsync();

            var result = await driver.WriteJsonAsync("N7:0", "{}");
            Assert.False(result.IsSuccess);
            Assert.Contains("not supported", result.Error!, StringComparison.OrdinalIgnoreCase);
        }
    }

    // ==========================================================================
    // Factory Methods
    // ==========================================================================

    [Fact]
    public void PlcDriverFactory_CreateSlc_ReturnsDriver()
    {
        var driver = PlcDriverFactory.CreateSlc("192.168.1.50");
        Assert.NotNull(driver);
        Assert.False(driver.IsConnected);
        driver.Dispose();
    }

    [Fact]
    public void PlcDriverFactory_CreateMicroLogix_ReturnsDriver()
    {
        var driver = PlcDriverFactory.CreateMicroLogix("192.168.1.50");
        Assert.NotNull(driver);
        driver.Dispose();
    }

    [Fact]
    public void PlcDriverFactory_CreatePlc5_ReturnsDriver()
    {
        var driver = PlcDriverFactory.CreatePlc5("192.168.1.50");
        Assert.NotNull(driver);
        driver.Dispose();
    }
}
