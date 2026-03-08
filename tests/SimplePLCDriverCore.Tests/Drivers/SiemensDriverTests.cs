using System.Buffers.Binary;
using SimplePLCDriverCore.Common.Buffers;
using SimplePLCDriverCore.Drivers;
using SimplePLCDriverCore.Protocols.S7;
using SimplePLCDriverCore.Tests.EtherNetIP;

namespace SimplePLCDriverCore.Tests.Drivers;

public class SiemensDriverTests
{
    private static (FakeTransport Transport, SiemensDriver Driver) CreateTestDriver(
        byte rack = 0, byte slot = 0)
    {
        var transport = new FakeTransport();
        var driver = new SiemensDriver("127.0.0.1", rack, slot, null, () => transport);
        return (transport, driver);
    }

    /// <summary>
    /// Enqueue the S7 connection sequence: COTP CC + S7 Setup response.
    /// </summary>
    private static void EnqueueConnectSequence(FakeTransport transport, ushort pduSize = 480)
    {
        // COTP Connection Confirm wrapped in TPKT
        transport.EnqueueResponse(BuildTpktFrame(BuildCotpCC()));

        // S7 Communication Setup response wrapped in COTP DT + TPKT
        transport.EnqueueResponse(BuildTpktFrame(
            BuildCotpDt(BuildS7SetupResponse(pduSize))));
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
                () => driver.ReadAsync("DB1.DBW0").AsTask());
        }
    }

    // ==========================================================================
    // Read Tests
    // ==========================================================================

    [Fact]
    public async Task ReadAsync_Word_ReturnsValue()
    {
        var (transport, driver) = CreateTestDriver();
        EnqueueConnectSequence(transport);

        await using (driver)
        {
            await driver.ConnectAsync();

            // Enqueue S7 read response with word value 42
            var valueData = new byte[2];
            BinaryPrimitives.WriteInt16BigEndian(valueData, 42);
            transport.EnqueueResponse(BuildTpktFrame(
                BuildCotpDt(BuildS7ReadResponse([valueData]))));

            var result = await driver.ReadAsync("DB1.DBW0");

            Assert.True(result.IsSuccess, $"Read failed: {result.Error}");
            Assert.Equal(42, result.Value.AsInt32());
            Assert.Equal("WORD", result.TypeName);
        }
    }

    [Fact]
    public async Task ReadAsync_DWord_ReturnsValue()
    {
        var (transport, driver) = CreateTestDriver();
        EnqueueConnectSequence(transport);

        await using (driver)
        {
            await driver.ConnectAsync();

            var valueData = new byte[4];
            BinaryPrimitives.WriteInt32BigEndian(valueData, 100000);
            transport.EnqueueResponse(BuildTpktFrame(
                BuildCotpDt(BuildS7ReadResponse([valueData]))));

            var result = await driver.ReadAsync("DB1.DBD0");

            Assert.True(result.IsSuccess);
            Assert.Equal(100000, result.Value.AsInt32());
            Assert.Equal("DWORD", result.TypeName);
        }
    }

    [Fact]
    public async Task ReadAsync_Bit_True()
    {
        var (transport, driver) = CreateTestDriver();
        EnqueueConnectSequence(transport);

        await using (driver)
        {
            await driver.ConnectAsync();

            transport.EnqueueResponse(BuildTpktFrame(
                BuildCotpDt(BuildS7ReadResponse([[0x01]], isBit: true))));

            var result = await driver.ReadAsync("DB1.DBX0.0");

            Assert.True(result.IsSuccess);
            Assert.True(result.Value.AsBoolean());
            Assert.Equal("BOOL", result.TypeName);
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
    public async Task ReadAsync_S7Error_ReturnsFailure()
    {
        var (transport, driver) = CreateTestDriver();
        EnqueueConnectSequence(transport);

        await using (driver)
        {
            await driver.ConnectAsync();

            transport.EnqueueResponse(BuildTpktFrame(
                BuildCotpDt(BuildS7ErrorResponse(0x85, 0x01))));

            var result = await driver.ReadAsync("DB1.DBW0");

            Assert.False(result.IsSuccess);
        }
    }

    [Fact]
    public async Task ReadAsync_ItemError_ReturnsFailure()
    {
        var (transport, driver) = CreateTestDriver();
        EnqueueConnectSequence(transport);

        await using (driver)
        {
            await driver.ConnectAsync();

            // Read response with item return code = 0x05 (invalid address)
            transport.EnqueueResponse(BuildTpktFrame(
                BuildCotpDt(BuildS7ReadResponseWithItemError(0x05))));

            var result = await driver.ReadAsync("DB1.DBW0");

            Assert.False(result.IsSuccess);
        }
    }

    // ==========================================================================
    // Write Tests
    // ==========================================================================

    [Fact]
    public async Task WriteAsync_Word_Success()
    {
        var (transport, driver) = CreateTestDriver();
        EnqueueConnectSequence(transport);

        await using (driver)
        {
            await driver.ConnectAsync();

            transport.EnqueueResponse(BuildTpktFrame(
                BuildCotpDt(BuildS7WriteResponse(0xFF))));

            var result = await driver.WriteAsync("DB1.DBW0", (short)42);

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

            // S7 multi-item read response
            var word1 = new byte[2];
            BinaryPrimitives.WriteInt16BigEndian(word1, 42);
            var word2 = new byte[2];
            BinaryPrimitives.WriteInt16BigEndian(word2, 100);

            transport.EnqueueResponse(BuildTpktFrame(
                BuildCotpDt(BuildS7ReadResponse([word1, word2]))));

            var results = await driver.ReadAsync(["DB1.DBW0", "DB1.DBW2"]);

            Assert.Equal(2, results.Length);
            Assert.True(results[0].IsSuccess);
            Assert.True(results[1].IsSuccess);
            Assert.Equal(42, results[0].Value.AsInt32());
            Assert.Equal(100, results[1].Value.AsInt32());
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

            // Sequential write responses
            transport.EnqueueResponse(BuildTpktFrame(
                BuildCotpDt(BuildS7WriteResponse(0xFF))));
            transport.EnqueueResponse(BuildTpktFrame(
                BuildCotpDt(BuildS7WriteResponse(0xFF))));

            var results = await driver.WriteAsync([
                ("DB1.DBW0", (object)(short)42),
                ("DB1.DBW2", (object)(short)100),
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
            var result = await driver.ReadJsonAsync("DB1.DBW0");
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
            var result = await driver.WriteJsonAsync("DB1.DBW0", "{}");
            Assert.False(result.IsSuccess);
            Assert.Contains("not supported", result.Error!, StringComparison.OrdinalIgnoreCase);
        }
    }

    // ==========================================================================
    // Factory Methods
    // ==========================================================================

    [Fact]
    public void PlcDriverFactory_CreateSiemens_ReturnsDriver()
    {
        var driver = PlcDriverFactory.CreateSiemens("192.168.1.200");
        Assert.NotNull(driver);
        Assert.False(driver.IsConnected);
        driver.Dispose();
    }

    [Fact]
    public void PlcDriverFactory_CreateS7_1200_ReturnsDriver()
    {
        var driver = PlcDriverFactory.CreateS7_1200("192.168.1.200");
        Assert.NotNull(driver);
        driver.Dispose();
    }

    [Fact]
    public void PlcDriverFactory_CreateS7_300_ReturnsDriver()
    {
        var driver = PlcDriverFactory.CreateS7_300("192.168.1.200");
        Assert.NotNull(driver);
        driver.Dispose();
    }

    // ==========================================================================
    // Mock Response Builders
    // ==========================================================================

    private static byte[] BuildTpktFrame(byte[] payload)
    {
        using var writer = new PacketWriter(payload.Length + 4);
        writer.WriteUInt8(3); // version
        writer.WriteUInt8(0); // reserved
        writer.WriteUInt16BE((ushort)(4 + payload.Length));
        writer.WriteBytes(payload);
        return writer.ToArray();
    }

    private static byte[] BuildCotpCC()
    {
        // Minimal COTP Connection Confirm
        return [0x06, CotpPacket.PduTypeCC, 0x00, 0x01, 0x00, 0x02, 0x00];
    }

    private static byte[] BuildCotpDt(byte[] s7Data)
    {
        using var writer = new PacketWriter(s7Data.Length + 3);
        writer.WriteUInt8(0x02); // length indicator
        writer.WriteUInt8(CotpPacket.PduTypeDT);
        writer.WriteUInt8(0x80); // EOT
        writer.WriteBytes(s7Data);
        return writer.ToArray();
    }

    private static byte[] BuildS7SetupResponse(ushort pduSize)
    {
        using var writer = new PacketWriter(32);
        writer.WriteUInt8(S7Message.ProtocolId);
        writer.WriteUInt8((byte)S7MessageType.AckData);
        writer.WriteUInt16BE(0); // reserved
        writer.WriteUInt16BE(1); // PDU ref
        writer.WriteUInt16BE(8); // param length
        writer.WriteUInt16BE(0); // data length
        writer.WriteUInt8(0); // error class
        writer.WriteUInt8(0); // error code
        // Setup params
        writer.WriteUInt8((byte)S7Function.CommunicationSetup);
        writer.WriteUInt8(0);
        writer.WriteUInt16BE(1);
        writer.WriteUInt16BE(1);
        writer.WriteUInt16BE(pduSize);
        return writer.ToArray();
    }

    private static byte[] BuildS7ReadResponse(byte[][] itemValues, bool isBit = false)
    {
        using var writer = new PacketWriter(128);

        // Build data section
        using var dataWriter = new PacketWriter(64);
        foreach (var val in itemValues)
        {
            dataWriter.WriteUInt8(0xFF); // return code: success
            dataWriter.WriteUInt8(isBit
                ? (byte)S7DataTransportSize.BitAccess
                : (byte)S7DataTransportSize.ByteWordDWord);
            dataWriter.WriteUInt16BE(isBit ? (ushort)1 : (ushort)val.Length); // length
            dataWriter.WriteBytes(val);

            // Pad to even if needed
            if (val.Length % 2 != 0)
                dataWriter.WriteUInt8(0);
        }
        var dataSectionBytes = dataWriter.ToArray();

        // Param section: function + item count
        var paramBytes = new byte[] { (byte)S7Function.ReadVar, (byte)itemValues.Length };

        // AckData header
        writer.WriteUInt8(S7Message.ProtocolId);
        writer.WriteUInt8((byte)S7MessageType.AckData);
        writer.WriteUInt16BE(0);
        writer.WriteUInt16BE(1);
        writer.WriteUInt16BE((ushort)paramBytes.Length);
        writer.WriteUInt16BE((ushort)dataSectionBytes.Length);
        writer.WriteUInt8(0);
        writer.WriteUInt8(0);
        writer.WriteBytes(paramBytes);
        writer.WriteBytes(dataSectionBytes);

        return writer.ToArray();
    }

    private static byte[] BuildS7ReadResponseWithItemError(byte returnCode)
    {
        using var writer = new PacketWriter(64);

        // Data section: item with error
        var dataSection = new byte[4];
        dataSection[0] = returnCode;
        dataSection[1] = 0x00;
        BinaryPrimitives.WriteUInt16BigEndian(dataSection.AsSpan(2), 0);

        var paramBytes = new byte[] { (byte)S7Function.ReadVar, 0x01 };

        writer.WriteUInt8(S7Message.ProtocolId);
        writer.WriteUInt8((byte)S7MessageType.AckData);
        writer.WriteUInt16BE(0);
        writer.WriteUInt16BE(1);
        writer.WriteUInt16BE((ushort)paramBytes.Length);
        writer.WriteUInt16BE((ushort)dataSection.Length);
        writer.WriteUInt8(0);
        writer.WriteUInt8(0);
        writer.WriteBytes(paramBytes);
        writer.WriteBytes(dataSection);

        return writer.ToArray();
    }

    private static byte[] BuildS7ErrorResponse(byte errorClass, byte errorCode)
    {
        using var writer = new PacketWriter(32);
        writer.WriteUInt8(S7Message.ProtocolId);
        writer.WriteUInt8((byte)S7MessageType.AckData);
        writer.WriteUInt16BE(0);
        writer.WriteUInt16BE(1);
        writer.WriteUInt16BE(2); // param length
        writer.WriteUInt16BE(0); // data length
        writer.WriteUInt8(errorClass);
        writer.WriteUInt8(errorCode);
        writer.WriteUInt8((byte)S7Function.ReadVar);
        writer.WriteUInt8(0);
        return writer.ToArray();
    }

    private static byte[] BuildS7WriteResponse(byte returnCode)
    {
        using var writer = new PacketWriter(32);

        var paramBytes = new byte[] { (byte)S7Function.WriteVar, 0x01 };
        var dataSection = new byte[] { returnCode };

        writer.WriteUInt8(S7Message.ProtocolId);
        writer.WriteUInt8((byte)S7MessageType.AckData);
        writer.WriteUInt16BE(0);
        writer.WriteUInt16BE(1);
        writer.WriteUInt16BE((ushort)paramBytes.Length);
        writer.WriteUInt16BE((ushort)dataSection.Length);
        writer.WriteUInt8(0);
        writer.WriteUInt8(0);
        writer.WriteBytes(paramBytes);
        writer.WriteBytes(dataSection);

        return writer.ToArray();
    }
}
