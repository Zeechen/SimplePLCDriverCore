using System.Buffers.Binary;
using SimplePLCDriverCore.Common.Buffers;
using SimplePLCDriverCore.Drivers;
using SimplePLCDriverCore.Protocols.Fins;
using SimplePLCDriverCore.Tests.EtherNetIP;

namespace SimplePLCDriverCore.Tests.Drivers;

public class OmronDriverTests
{
    private const byte ClientNode = 10;
    private const byte ServerNode = 1;

    private static (FakeTransport Transport, OmronDriver Driver) CreateTestDriver()
    {
        var transport = new FakeTransport();
        var driver = new OmronDriver("127.0.0.1", null, () => transport);
        return (transport, driver);
    }

    private static void EnqueueConnectSequence(FakeTransport transport)
    {
        transport.EnqueueResponse(BuildNodeAddressResponse(ClientNode, ServerNode));
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
                () => driver.ReadAsync("D0").AsTask());
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

            var wordData = new byte[2];
            BinaryPrimitives.WriteUInt16BigEndian(wordData, 42);
            transport.EnqueueResponse(BuildFinsReadResponse(wordData));

            var result = await driver.ReadAsync("D100");

            Assert.True(result.IsSuccess, $"Read failed: {result.Error}");
            Assert.Equal(42, result.Value.AsInt32());
            Assert.Equal("WORD", result.TypeName);
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

            transport.EnqueueResponse(BuildFinsReadResponse([0x01]));

            var result = await driver.ReadAsync("CIO0.00");

            Assert.True(result.IsSuccess);
            Assert.True(result.Value.AsBoolean());
            Assert.Equal("BIT", result.TypeName);
        }
    }

    [Fact]
    public async Task ReadAsync_Bit_False()
    {
        var (transport, driver) = CreateTestDriver();
        EnqueueConnectSequence(transport);

        await using (driver)
        {
            await driver.ConnectAsync();

            transport.EnqueueResponse(BuildFinsReadResponse([0x00]));

            var result = await driver.ReadAsync("CIO0.00");

            Assert.True(result.IsSuccess);
            Assert.False(result.Value.AsBoolean());
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
    public async Task ReadAsync_FinsError_ReturnsFailure()
    {
        var (transport, driver) = CreateTestDriver();
        EnqueueConnectSequence(transport);

        await using (driver)
        {
            await driver.ConnectAsync();

            transport.EnqueueResponse(BuildFinsErrorResponse(0x01, 0x01));

            var result = await driver.ReadAsync("D0");

            Assert.False(result.IsSuccess);
            Assert.Contains("FINS read failed", result.Error);
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

            transport.EnqueueResponse(BuildFinsWriteResponse());

            var result = await driver.WriteAsync("D100", (short)42);

            Assert.True(result.IsSuccess, $"Write failed: {result.Error}");
        }
    }

    [Fact]
    public async Task WriteAsync_Bit_Success()
    {
        var (transport, driver) = CreateTestDriver();
        EnqueueConnectSequence(transport);

        await using (driver)
        {
            await driver.ConnectAsync();

            transport.EnqueueResponse(BuildFinsWriteResponse());

            var result = await driver.WriteAsync("CIO0.00", true);

            Assert.True(result.IsSuccess);
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

            var word1 = new byte[2];
            BinaryPrimitives.WriteUInt16BigEndian(word1, 42);
            transport.EnqueueResponse(BuildFinsReadResponse(word1));

            var word2 = new byte[2];
            BinaryPrimitives.WriteUInt16BigEndian(word2, 100);
            transport.EnqueueResponse(BuildFinsReadResponse(word2));

            var results = await driver.ReadAsync(["D100", "D200"]);

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

            transport.EnqueueResponse(BuildFinsWriteResponse());
            transport.EnqueueResponse(BuildFinsWriteResponse());

            var results = await driver.WriteAsync([
                ("D100", (object)(short)42),
                ("D200", (object)(short)100),
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
            var result = await driver.ReadJsonAsync("D0");
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
            var result = await driver.WriteJsonAsync("D0", "{}");
            Assert.False(result.IsSuccess);
            Assert.Contains("not supported", result.Error!, StringComparison.OrdinalIgnoreCase);
        }
    }

    // ==========================================================================
    // Factory
    // ==========================================================================

    [Fact]
    public void PlcDriverFactory_CreateOmron_ReturnsDriver()
    {
        var driver = PlcDriverFactory.CreateOmron("192.168.1.100");
        Assert.NotNull(driver);
        Assert.False(driver.IsConnected);
        driver.Dispose();
    }

    // ==========================================================================
    // Mock Response Builders
    // ==========================================================================

    private static byte[] BuildNodeAddressResponse(byte clientNode, byte serverNode)
    {
        using var writer = new PacketWriter(32);
        writer.WriteBytes(FinsMessage.FinsMagic);
        writer.WriteUInt32BE(16);
        writer.WriteUInt32BE(FinsMessage.TcpCommandNodeAddressResponse);
        writer.WriteUInt32BE(0); // no error
        writer.WriteUInt32BE(clientNode);
        writer.WriteUInt32BE(serverNode);
        return writer.ToArray();
    }

    private static byte[] BuildFinsReadResponse(byte[] responseData)
    {
        return BuildFinsResponse(0x00, 0x00, responseData);
    }

    private static byte[] BuildFinsWriteResponse()
    {
        return BuildFinsResponse(0x00, 0x00, []);
    }

    private static byte[] BuildFinsErrorResponse(byte mainCode, byte subCode)
    {
        return BuildFinsResponse(mainCode, subCode, []);
    }

    private static byte[] BuildFinsResponse(byte mainCode, byte subCode, byte[] data)
    {
        using var writer = new PacketWriter(64);

        // Build FINS payload
        using var finsWriter = new PacketWriter(32);

        // FINS header (10 bytes)
        finsWriter.WriteUInt8(0xC0); // ICF
        finsWriter.WriteUInt8(0x00); // RSV
        finsWriter.WriteUInt8(0x02); // GCT
        finsWriter.WriteUInt8(0x00); // DNA
        finsWriter.WriteUInt8(ClientNode); // DA1
        finsWriter.WriteUInt8(0x00); // DA2
        finsWriter.WriteUInt8(0x00); // SNA
        finsWriter.WriteUInt8(ServerNode); // SA1
        finsWriter.WriteUInt8(0x00); // SA2
        finsWriter.WriteUInt8(0x01); // SID

        // Command echo
        finsWriter.WriteUInt16BE(FinsCommands.MemoryAreaRead);

        // Response codes
        finsWriter.WriteUInt8(mainCode);
        finsWriter.WriteUInt8(subCode);

        // Data
        finsWriter.WriteBytes(data);

        var finsPayload = finsWriter.ToArray();

        // TCP header
        writer.WriteBytes(FinsMessage.FinsMagic);
        writer.WriteUInt32BE((uint)(8 + finsPayload.Length));
        writer.WriteUInt32BE(FinsMessage.TcpCommandSendFrame);
        writer.WriteUInt32BE(0); // no error
        writer.WriteBytes(finsPayload);

        return writer.ToArray();
    }
}
