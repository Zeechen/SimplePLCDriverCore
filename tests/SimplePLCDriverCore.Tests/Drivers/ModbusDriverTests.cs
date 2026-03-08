using System.Buffers.Binary;
using SimplePLCDriverCore.Drivers;
using SimplePLCDriverCore.Protocols.Modbus;
using SimplePLCDriverCore.Tests.EtherNetIP;

namespace SimplePLCDriverCore.Tests.Drivers;

public class ModbusDriverTests
{
    private static (FakeTransport Transport, ModbusDriver Driver) CreateTestDriver()
    {
        var transport = new FakeTransport();
        var driver = new ModbusDriver("127.0.0.1", 502, 1, null, () => transport);
        return (transport, driver);
    }

    // ==========================================================================
    // Connection Tests
    // ==========================================================================

    [Fact]
    public async Task Connect_SetsIsConnected()
    {
        var (_, driver) = CreateTestDriver();

        await using (driver)
        {
            await driver.ConnectAsync();
            Assert.True(driver.IsConnected);
        }
    }

    [Fact]
    public async Task Disconnect_ClearsIsConnected()
    {
        var (_, driver) = CreateTestDriver();

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
                () => driver.ReadAsync("HR0").AsTask());
        }
    }

    // ==========================================================================
    // Read Tests - Holding Register
    // ==========================================================================

    [Fact]
    public async Task ReadAsync_HoldingRegister_ReturnsValue()
    {
        var (transport, driver) = CreateTestDriver();

        await using (driver)
        {
            await driver.ConnectAsync();

            transport.EnqueueResponse(BuildReadRegisterResponse(42));

            var result = await driver.ReadAsync("HR100");

            Assert.True(result.IsSuccess, $"Read failed: {result.Error}");
            Assert.Equal(42, result.Value.AsInt32());
            Assert.Equal("HOLDING_REGISTER", result.TypeName);
        }
    }

    [Fact]
    public async Task ReadAsync_HoldingRegister_NegativeValue()
    {
        var (transport, driver) = CreateTestDriver();

        await using (driver)
        {
            await driver.ConnectAsync();

            transport.EnqueueResponse(BuildReadRegisterResponse(-100));

            var result = await driver.ReadAsync("HR0");

            Assert.True(result.IsSuccess);
            Assert.Equal(-100, result.Value.AsInt32());
        }
    }

    // ==========================================================================
    // Read Tests - Input Register
    // ==========================================================================

    [Fact]
    public async Task ReadAsync_InputRegister_ReturnsValue()
    {
        var (transport, driver) = CreateTestDriver();

        await using (driver)
        {
            await driver.ConnectAsync();

            transport.EnqueueResponse(BuildReadRegisterResponse(1000,
                ModbusFunctionCodes.ReadInputRegisters));

            var result = await driver.ReadAsync("IR50");

            Assert.True(result.IsSuccess);
            Assert.Equal(1000, result.Value.AsInt32());
            Assert.Equal("INPUT_REGISTER", result.TypeName);
        }
    }

    // ==========================================================================
    // Read Tests - Coil
    // ==========================================================================

    [Fact]
    public async Task ReadAsync_Coil_True()
    {
        var (transport, driver) = CreateTestDriver();

        await using (driver)
        {
            await driver.ConnectAsync();

            transport.EnqueueResponse(BuildReadCoilResponse(true));

            var result = await driver.ReadAsync("C0");

            Assert.True(result.IsSuccess);
            Assert.True(result.Value.AsBoolean());
            Assert.Equal("COIL", result.TypeName);
        }
    }

    [Fact]
    public async Task ReadAsync_Coil_False()
    {
        var (transport, driver) = CreateTestDriver();

        await using (driver)
        {
            await driver.ConnectAsync();

            transport.EnqueueResponse(BuildReadCoilResponse(false));

            var result = await driver.ReadAsync("C0");

            Assert.True(result.IsSuccess);
            Assert.False(result.Value.AsBoolean());
        }
    }

    // ==========================================================================
    // Read Tests - Discrete Input
    // ==========================================================================

    [Fact]
    public async Task ReadAsync_DiscreteInput_True()
    {
        var (transport, driver) = CreateTestDriver();

        await using (driver)
        {
            await driver.ConnectAsync();

            transport.EnqueueResponse(BuildReadCoilResponse(true,
                ModbusFunctionCodes.ReadDiscreteInputs));

            var result = await driver.ReadAsync("DI0");

            Assert.True(result.IsSuccess);
            Assert.True(result.Value.AsBoolean());
            Assert.Equal("DISCRETE_INPUT", result.TypeName);
        }
    }

    // ==========================================================================
    // Read Tests - Error Cases
    // ==========================================================================

    [Fact]
    public async Task ReadAsync_InvalidAddress_ReturnsFailure()
    {
        var (_, driver) = CreateTestDriver();

        await using (driver)
        {
            await driver.ConnectAsync();

            var result = await driver.ReadAsync("INVALID");

            Assert.False(result.IsSuccess);
            Assert.Contains("Invalid address", result.Error);
        }
    }

    [Fact]
    public async Task ReadAsync_ModbusException_ReturnsFailure()
    {
        var (transport, driver) = CreateTestDriver();

        await using (driver)
        {
            await driver.ConnectAsync();

            transport.EnqueueResponse(BuildExceptionResponse(
                ModbusFunctionCodes.ReadHoldingRegisters,
                ModbusExceptionCodes.IllegalDataAddress));

            var result = await driver.ReadAsync("HR0");

            Assert.False(result.IsSuccess);
            Assert.Contains("Modbus read failed", result.Error);
        }
    }

    // ==========================================================================
    // Write Tests
    // ==========================================================================

    [Fact]
    public async Task WriteAsync_HoldingRegister_Success()
    {
        var (transport, driver) = CreateTestDriver();

        await using (driver)
        {
            await driver.ConnectAsync();

            transport.EnqueueResponse(BuildWriteResponse(
                ModbusFunctionCodes.WriteSingleRegister));

            var result = await driver.WriteAsync("HR100", (short)42);

            Assert.True(result.IsSuccess, $"Write failed: {result.Error}");
        }
    }

    [Fact]
    public async Task WriteAsync_Coil_Success()
    {
        var (transport, driver) = CreateTestDriver();

        await using (driver)
        {
            await driver.ConnectAsync();

            transport.EnqueueResponse(BuildWriteResponse(
                ModbusFunctionCodes.WriteSingleCoil));

            var result = await driver.WriteAsync("C0", true);

            Assert.True(result.IsSuccess);
        }
    }

    [Fact]
    public async Task WriteAsync_DiscreteInput_ReturnsFailure()
    {
        var (_, driver) = CreateTestDriver();

        await using (driver)
        {
            await driver.ConnectAsync();

            var result = await driver.WriteAsync("DI0", true);

            Assert.False(result.IsSuccess);
            Assert.Contains("read-only", result.Error);
        }
    }

    [Fact]
    public async Task WriteAsync_InputRegister_ReturnsFailure()
    {
        var (_, driver) = CreateTestDriver();

        await using (driver)
        {
            await driver.ConnectAsync();

            var result = await driver.WriteAsync("IR0", (short)42);

            Assert.False(result.IsSuccess);
            Assert.Contains("read-only", result.Error);
        }
    }

    [Fact]
    public async Task WriteAsync_InvalidAddress_ReturnsFailure()
    {
        var (_, driver) = CreateTestDriver();

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

        await using (driver)
        {
            await driver.ConnectAsync();

            transport.EnqueueResponse(BuildReadRegisterResponse(42));
            transport.EnqueueResponse(BuildReadRegisterResponse(100));

            var results = await driver.ReadAsync(["HR100", "HR200"]);

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

        await using (driver)
        {
            await driver.ConnectAsync();

            transport.EnqueueResponse(BuildWriteResponse(
                ModbusFunctionCodes.WriteSingleRegister));
            transport.EnqueueResponse(BuildWriteResponse(
                ModbusFunctionCodes.WriteSingleRegister));

            var results = await driver.WriteAsync([
                ("HR100", (object)(short)42),
                ("HR200", (object)(short)100),
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
        var (_, driver) = CreateTestDriver();

        await using (driver)
        {
            await driver.ConnectAsync();
            var result = await driver.ReadJsonAsync("HR0");
            Assert.False(result.IsSuccess);
            Assert.Contains("not supported", result.Error!, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task WriteJsonAsync_ReturnsFailure()
    {
        var (_, driver) = CreateTestDriver();

        await using (driver)
        {
            await driver.ConnectAsync();
            var result = await driver.WriteJsonAsync("HR0", "{}");
            Assert.False(result.IsSuccess);
            Assert.Contains("not supported", result.Error!, StringComparison.OrdinalIgnoreCase);
        }
    }

    // ==========================================================================
    // Factory
    // ==========================================================================

    [Fact]
    public void PlcDriverFactory_CreateModbus_ReturnsDriver()
    {
        var driver = PlcDriverFactory.CreateModbus("192.168.1.100");
        Assert.NotNull(driver);
        Assert.False(driver.IsConnected);
        driver.Dispose();
    }

    [Fact]
    public void PlcDriverFactory_CreateModbusTcp_ReturnsDriver()
    {
        var driver = PlcDriverFactory.CreateModbusTcp("192.168.1.100");
        Assert.NotNull(driver);
        Assert.False(driver.IsConnected);
        driver.Dispose();
    }

    // ==========================================================================
    // Mock Response Builders
    // ==========================================================================

    private static byte[] BuildReadRegisterResponse(short value,
        byte functionCode = ModbusFunctionCodes.ReadHoldingRegisters)
    {
        var response = new byte[11]; // MBAP(7) + FC(1) + byte count(1) + data(2)

        BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(0), 1); // TX ID
        BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(2), 0); // Protocol ID
        BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(4), 4); // Length: unit + FC + byteCount + 2
        response[6] = 1; // Unit ID
        response[7] = functionCode;
        response[8] = 0x02; // byte count
        BinaryPrimitives.WriteInt16BigEndian(response.AsSpan(9), value);

        return response;
    }

    private static byte[] BuildReadCoilResponse(bool value,
        byte functionCode = ModbusFunctionCodes.ReadCoils)
    {
        var response = new byte[10]; // MBAP(7) + FC(1) + byte count(1) + data(1)

        BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(0), 1); // TX ID
        BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(2), 0); // Protocol ID
        BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(4), 3); // Length: unit + FC + byteCount + 1
        response[6] = 1; // Unit ID
        response[7] = functionCode;
        response[8] = 0x01; // byte count
        response[9] = value ? (byte)0x01 : (byte)0x00;

        return response;
    }

    private static byte[] BuildWriteResponse(byte functionCode)
    {
        var response = new byte[12]; // MBAP(7) + FC(1) + address(2) + value(2)

        BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(0), 1); // TX ID
        BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(2), 0); // Protocol ID
        BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(4), 5); // Length
        response[6] = 1; // Unit ID
        response[7] = functionCode;
        BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(8), 0); // address echo
        BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(10), 0); // value echo

        return response;
    }

    private static byte[] BuildExceptionResponse(byte functionCode, byte exceptionCode)
    {
        var response = new byte[9]; // MBAP(7) + FC with error(1) + exception(1)

        BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(0), 1); // TX ID
        BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(2), 0); // Protocol ID
        BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(4), 3); // Length
        response[6] = 1; // Unit ID
        response[7] = (byte)(functionCode | 0x80); // Error bit set
        response[8] = exceptionCode;

        return response;
    }
}
