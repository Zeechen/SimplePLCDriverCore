using System.Buffers.Binary;
using System.Text;
using SimplePLCDriverCore.Drivers;
using SimplePLCDriverCore.Protocols.Modbus;
using SimplePLCDriverCore.Tests.EtherNetIP;

namespace SimplePLCDriverCore.Tests.Modbus;

/// <summary>
/// Tests for Phase 5 advanced ModbusDriver methods:
/// FC 22 (Mask Write Register), FC 23 (Read/Write Multiple Registers),
/// FC 08 (Diagnostics), FC 43/14 (Read Device Identification),
/// FC 20 (Read File Record), FC 21 (Write File Record),
/// FC 24 (Read FIFO Queue), multi-register typed reads/writes,
/// SendRawAsync, and DefaultByteOrder.
/// </summary>
public class ModbusDriverAdvancedTests
{
    private static (FakeTransport Transport, ModbusDriver Driver) CreateTestDriver(
        ModbusByteOrder byteOrder = ModbusByteOrder.ABCD)
    {
        var transport = new FakeTransport();
        var driver = new ModbusDriver("127.0.0.1", 502, 1, null, () => transport, byteOrder);
        return (transport, driver);
    }

    // =========================================================================
    // Helper: Build MBAP header + FC + data
    // =========================================================================

    /// <summary>
    /// Build a complete Modbus TCP response frame.
    /// MBAP header (7 bytes) + FC (1 byte) + data (variable).
    /// </summary>
    private static byte[] BuildResponse(byte functionCode, params byte[] data)
    {
        // Length field = unitId(1) + FC(1) + data.Length
        var length = (ushort)(2 + data.Length);
        var frame = new byte[7 + 1 + data.Length];

        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(0), 1);      // TX ID
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(2), 0);      // Protocol ID
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(4), length); // Length
        frame[6] = 1;                                                    // Unit ID
        frame[7] = functionCode;
        Array.Copy(data, 0, frame, 8, data.Length);

        return frame;
    }

    private static byte[] BuildExceptionResponse(byte functionCode, byte exceptionCode)
    {
        var frame = new byte[9];
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(0), 1);
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(2), 0);
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(4), 3);
        frame[6] = 1;
        frame[7] = (byte)(functionCode | 0x80);
        frame[8] = exceptionCode;
        return frame;
    }

    /// <summary>
    /// Build a read-holding-registers response with N register values.
    /// MBAP(7) + FC(1) + byteCount(1) + regData(N*2).
    /// </summary>
    private static byte[] BuildReadRegistersResponse(byte fc, params short[] values)
    {
        var byteCount = (byte)(values.Length * 2);
        var data = new byte[1 + byteCount];
        data[0] = byteCount;
        for (int i = 0; i < values.Length; i++)
            BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(1 + i * 2), values[i]);
        return BuildResponse(fc, data);
    }

    private static byte[] BuildReadHRResponse(params short[] values)
        => BuildReadRegistersResponse(0x03, values);

    /// <summary>
    /// Build a Write Multiple Registers (FC 10) echo response:
    /// MBAP(7) + FC(1) + startAddr(2) + quantity(2).
    /// </summary>
    private static byte[] BuildWriteMultipleResponse(ushort startAddr, ushort quantity)
    {
        var data = new byte[4];
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(0), startAddr);
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(2), quantity);
        return BuildResponse(0x10, data);
    }

    // =========================================================================
    // FC 22 - MaskWriteRegisterAsync
    // =========================================================================

    [Fact]
    public async Task MaskWriteRegister_Success()
    {
        var (transport, driver) = CreateTestDriver();
        await using (driver)
        {
            await driver.ConnectAsync();

            // FC 22 echo response: address(2) + andMask(2) + orMask(2)
            var echoData = new byte[6];
            BinaryPrimitives.WriteUInt16BigEndian(echoData.AsSpan(0), 100);
            BinaryPrimitives.WriteUInt16BigEndian(echoData.AsSpan(2), 0x00FF);
            BinaryPrimitives.WriteUInt16BigEndian(echoData.AsSpan(4), 0x0100);
            transport.EnqueueResponse(BuildResponse(0x16, echoData));

            var result = await driver.MaskWriteRegisterAsync("HR100", 0x00FF, 0x0100);

            Assert.True(result.IsSuccess, $"Expected success: {result.Error}");
        }
    }

    [Fact]
    public async Task MaskWriteRegister_NonHoldingRegister_ReturnsFailure()
    {
        var (_, driver) = CreateTestDriver();
        await using (driver)
        {
            await driver.ConnectAsync();

            var result = await driver.MaskWriteRegisterAsync("IR50", 0xFFFF, 0x0001);

            Assert.False(result.IsSuccess);
            Assert.Contains("Holding Register", result.Error);
        }
    }

    [Fact]
    public async Task MaskWriteRegister_CoilAddress_ReturnsFailure()
    {
        var (_, driver) = CreateTestDriver();
        await using (driver)
        {
            await driver.ConnectAsync();

            var result = await driver.MaskWriteRegisterAsync("C0", 0xFFFF, 0x0001);

            Assert.False(result.IsSuccess);
            Assert.Contains("Holding Register", result.Error);
        }
    }

    // =========================================================================
    // SetBitAsync / ClearBitAsync
    // =========================================================================

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(15)]
    public async Task SetBitAsync_Success_VariousBits(int bit)
    {
        var (transport, driver) = CreateTestDriver();
        await using (driver)
        {
            await driver.ConnectAsync();

            var echoData = new byte[6];
            BinaryPrimitives.WriteUInt16BigEndian(echoData.AsSpan(0), 10);
            BinaryPrimitives.WriteUInt16BigEndian(echoData.AsSpan(2), 0xFFFF);
            BinaryPrimitives.WriteUInt16BigEndian(echoData.AsSpan(4), (ushort)(1 << bit));
            transport.EnqueueResponse(BuildResponse(0x16, echoData));

            var result = await driver.SetBitAsync("HR10", bit);

            Assert.True(result.IsSuccess, $"SetBit({bit}) failed: {result.Error}");

            // Verify sent request contains correct masks
            var sent = transport.SentData[^1]; // last sent
            // Request: MBAP(7) + unitId(1=index 6) + FC(1=0x16) + addr(2) + andMask(2) + orMask(2)
            var sentAndMask = BinaryPrimitives.ReadUInt16BigEndian(sent.AsSpan(10));
            var sentOrMask = BinaryPrimitives.ReadUInt16BigEndian(sent.AsSpan(12));
            Assert.Equal(0xFFFF, sentAndMask);
            Assert.Equal((ushort)(1 << bit), sentOrMask);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    [InlineData(15)]
    public async Task ClearBitAsync_Success_VerifiesMasks(int bit)
    {
        var (transport, driver) = CreateTestDriver();
        await using (driver)
        {
            await driver.ConnectAsync();

            ushort expectedAndMask = (ushort)~(1 << bit);
            var echoData = new byte[6];
            BinaryPrimitives.WriteUInt16BigEndian(echoData.AsSpan(0), 10);
            BinaryPrimitives.WriteUInt16BigEndian(echoData.AsSpan(2), expectedAndMask);
            BinaryPrimitives.WriteUInt16BigEndian(echoData.AsSpan(4), 0x0000);
            transport.EnqueueResponse(BuildResponse(0x16, echoData));

            var result = await driver.ClearBitAsync("HR10", bit);

            Assert.True(result.IsSuccess, $"ClearBit({bit}) failed: {result.Error}");

            var sent = transport.SentData[^1];
            var sentAndMask = BinaryPrimitives.ReadUInt16BigEndian(sent.AsSpan(10));
            var sentOrMask = BinaryPrimitives.ReadUInt16BigEndian(sent.AsSpan(12));
            Assert.Equal(expectedAndMask, sentAndMask);
            Assert.Equal((ushort)0x0000, sentOrMask);
        }
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(16)]
    [InlineData(100)]
    public async Task SetBitAsync_InvalidBitPosition_ReturnsFailure(int bit)
    {
        var (_, driver) = CreateTestDriver();
        await using (driver)
        {
            await driver.ConnectAsync();

            var result = await driver.SetBitAsync("HR10", bit);

            Assert.False(result.IsSuccess);
            Assert.Contains("0-15", result.Error);
        }
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(16)]
    public async Task ClearBitAsync_InvalidBitPosition_ReturnsFailure(int bit)
    {
        var (_, driver) = CreateTestDriver();
        await using (driver)
        {
            await driver.ConnectAsync();

            var result = await driver.ClearBitAsync("HR10", bit);

            Assert.False(result.IsSuccess);
            Assert.Contains("0-15", result.Error);
        }
    }

    // =========================================================================
    // FC 23 - ReadWriteMultipleRegistersAsync
    // =========================================================================

    [Fact]
    public async Task ReadWriteMultipleRegisters_Success()
    {
        var (transport, driver) = CreateTestDriver();
        await using (driver)
        {
            await driver.ConnectAsync();

            // Response for FC 23: byteCount(1) + register data
            // Reading 2 registers: byteCount=4, values = 100, 200
            var data = new byte[5];
            data[0] = 4; // byte count
            BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(1), 100);
            BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(3), 200);
            transport.EnqueueResponse(BuildResponse(0x17, data));

            var writeValues = new short[] { 10, 20 };
            var result = await driver.ReadWriteMultipleRegistersAsync(
                "HR0", 2, "HR100", writeValues);

            Assert.True(result.IsSuccess, $"Expected success: {result.ErrorMessage}");
            Assert.Equal(2, result.ReadValues.Length);
            Assert.Equal(100, result.ReadValues[0]);
            Assert.Equal(200, result.ReadValues[1]);
        }
    }

    [Fact]
    public async Task ReadWriteMultipleRegisters_NonHR_ReadAddress_ReturnsFailure()
    {
        var (_, driver) = CreateTestDriver();
        await using (driver)
        {
            await driver.ConnectAsync();

            var result = await driver.ReadWriteMultipleRegistersAsync(
                "IR0", 1, "HR0", new short[] { 1 });

            Assert.False(result.IsSuccess);
            Assert.Contains("Read address", result.ErrorMessage);
        }
    }

    [Fact]
    public async Task ReadWriteMultipleRegisters_NonHR_WriteAddress_ReturnsFailure()
    {
        var (_, driver) = CreateTestDriver();
        await using (driver)
        {
            await driver.ConnectAsync();

            var result = await driver.ReadWriteMultipleRegistersAsync(
                "HR0", 1, "C0", new short[] { 1 });

            Assert.False(result.IsSuccess);
            Assert.Contains("Write address", result.ErrorMessage);
        }
    }

    // =========================================================================
    // FC 08 - DiagnosticsAsync
    // =========================================================================

    [Fact]
    public async Task Diagnostics_ReturnQueryData_Loopback()
    {
        var (transport, driver) = CreateTestDriver();
        await using (driver)
        {
            await driver.ConnectAsync();

            // FC 08 response: subFunction(2) + data(2)
            var data = new byte[4];
            BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(0), 0x0000); // ReturnQueryData
            BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(2), 0x1234); // echoed data
            transport.EnqueueResponse(BuildResponse(0x08, data));

            var result = await driver.DiagnosticsAsync(
                ModbusDiagnosticSubFunction.ReturnQueryData, 0x1234);

            Assert.True(result.IsSuccess);
            Assert.Equal(ModbusDiagnosticSubFunction.ReturnQueryData, result.SubFunction);
            Assert.Equal(0x1234, result.Data);
        }
    }

    [Fact]
    public async Task Diagnostics_ClearCounters()
    {
        var (transport, driver) = CreateTestDriver();
        await using (driver)
        {
            await driver.ConnectAsync();

            var data = new byte[4];
            BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(0), 0x000A); // ClearCounters
            BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(2), 0x0000);
            transport.EnqueueResponse(BuildResponse(0x08, data));

            var result = await driver.DiagnosticsAsync(
                ModbusDiagnosticSubFunction.ClearCounters);

            Assert.True(result.IsSuccess);
            Assert.Equal(ModbusDiagnosticSubFunction.ClearCounters, result.SubFunction);
        }
    }

    [Fact]
    public async Task Diagnostics_ReturnBusMessageCount()
    {
        var (transport, driver) = CreateTestDriver();
        await using (driver)
        {
            await driver.ConnectAsync();

            var data = new byte[4];
            BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(0), 0x000B); // ReturnBusMessageCount
            BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(2), 42);
            transport.EnqueueResponse(BuildResponse(0x08, data));

            var result = await driver.DiagnosticsAsync(
                ModbusDiagnosticSubFunction.ReturnBusMessageCount);

            Assert.True(result.IsSuccess);
            Assert.Equal((ushort)42, result.Data);
        }
    }

    [Fact]
    public async Task Diagnostics_ExceptionResponse_ReturnsFailure()
    {
        var (transport, driver) = CreateTestDriver();
        await using (driver)
        {
            await driver.ConnectAsync();

            transport.EnqueueResponse(BuildExceptionResponse(0x08, 0x01));

            var result = await driver.DiagnosticsAsync(
                ModbusDiagnosticSubFunction.ReturnQueryData, 0x1234);

            Assert.False(result.IsSuccess);
            Assert.NotNull(result.ErrorMessage);
        }
    }

    // =========================================================================
    // FC 43/14 - ReadDeviceIdentificationAsync
    // =========================================================================

    [Fact]
    public async Task ReadDeviceIdentification_BasicLevel()
    {
        var (transport, driver) = CreateTestDriver();
        await using (driver)
        {
            await driver.ConnectAsync();

            // FC 2B (43) response data (after MBAP+FC):
            // MEI type(1)=0x0E, readDevIdCode(1), conformityLevel(1),
            // moreFollows(1), nextObjId(1), numObjects(1),
            // then object entries: objId(1) + objLen(1) + objValue(N)
            var vendor = Encoding.ASCII.GetBytes("TestVendor");
            var product = Encoding.ASCII.GetBytes("PLC100");
            var revision = Encoding.ASCII.GetBytes("1.0.0");

            var dataList = new List<byte>
            {
                0x0E,       // MEI type
                0x01,       // read device id code (Basic)
                0x01,       // conformity level
                0x00,       // more follows = false
                0x00,       // next object ID
                0x03,       // number of objects
                // Object 0: Vendor Name
                0x00, (byte)vendor.Length
            };
            dataList.AddRange(vendor);
            // Object 1: Product Code
            dataList.Add(0x01);
            dataList.Add((byte)product.Length);
            dataList.AddRange(product);
            // Object 2: Major Minor Revision
            dataList.Add(0x02);
            dataList.Add((byte)revision.Length);
            dataList.AddRange(revision);

            transport.EnqueueResponse(BuildResponse(0x2B, dataList.ToArray()));

            var id = await driver.ReadDeviceIdentificationAsync(ModbusDeviceIdLevel.Basic);

            Assert.Equal("TestVendor", id.VendorName);
            Assert.Equal("PLC100", id.ProductCode);
            Assert.Equal("1.0.0", id.MajorMinorRevision);
        }
    }

    [Fact]
    public async Task ReadDeviceIdentification_MultiObject_WithMoreFollows()
    {
        var (transport, driver) = CreateTestDriver();
        await using (driver)
        {
            await driver.ConnectAsync();

            // First response: vendor only, moreFollows=true, nextObjectId=0x01
            var vendor = Encoding.ASCII.GetBytes("Acme");
            var data1 = new List<byte>
            {
                0x0E, 0x01, 0x01,
                0x01,       // more follows = true
                0x01,       // next object ID
                0x01,       // number of objects
                0x00, (byte)vendor.Length
            };
            data1.AddRange(vendor);
            transport.EnqueueResponse(BuildResponse(0x2B, data1.ToArray()));

            // Second response: product + revision, moreFollows=false
            var product = Encoding.ASCII.GetBytes("Widget");
            var revision = Encoding.ASCII.GetBytes("2.5");
            var data2 = new List<byte>
            {
                0x0E, 0x01, 0x01,
                0x00,       // more follows = false
                0x00,
                0x02,       // number of objects
                0x01, (byte)product.Length
            };
            data2.AddRange(product);
            data2.Add(0x02);
            data2.Add((byte)revision.Length);
            data2.AddRange(revision);
            transport.EnqueueResponse(BuildResponse(0x2B, data2.ToArray()));

            var id = await driver.ReadDeviceIdentificationAsync(ModbusDeviceIdLevel.Basic);

            Assert.Equal("Acme", id.VendorName);
            Assert.Equal("Widget", id.ProductCode);
            Assert.Equal("2.5", id.MajorMinorRevision);
            Assert.Equal(3, id.AllObjects.Count);
        }
    }

    // =========================================================================
    // FC 20 - ReadFileRecordAsync
    // =========================================================================

    [Fact]
    public async Task ReadFileRecord_Success()
    {
        var (transport, driver) = CreateTestDriver();
        await using (driver)
        {
            await driver.ConnectAsync();

            // FC 20 response data: totalByteCount(1) + sub-response groups
            // Sub-response: groupLen(1) + refType(1=0x06) + recordData(N)
            var recordData = new byte[] { 0x01, 0x02, 0x03, 0x04 };
            var groupLen = (byte)(1 + recordData.Length); // refType + data
            var totalLen = (byte)(1 + 1 + recordData.Length); // groupLen + refType + data
            var data = new List<byte> { totalLen, groupLen, 0x06 };
            data.AddRange(recordData);

            transport.EnqueueResponse(BuildResponse(0x14, data.ToArray()));

            var records = await driver.ReadFileRecordAsync(1, 0, 2);

            Assert.Single(records);
            Assert.Equal(recordData, records[0]);
        }
    }

    // =========================================================================
    // FC 21 - WriteFileRecordAsync
    // =========================================================================

    [Fact]
    public async Task WriteFileRecord_Success()
    {
        var (transport, driver) = CreateTestDriver();
        await using (driver)
        {
            await driver.ConnectAsync();

            // FC 21 echo response - same structure as request data portion
            var echoData = new byte[] { 0x07, 0x06, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01 };
            transport.EnqueueResponse(BuildResponse(0x15, echoData));

            var fileData = new byte[] { 0x00, 0x01 };
            var result = await driver.WriteFileRecordAsync(1, 0, fileData);

            Assert.True(result.IsSuccess, $"Expected success: {result.Error}");
        }
    }

    [Fact]
    public async Task WriteFileRecord_Exception_ReturnsFailure()
    {
        var (transport, driver) = CreateTestDriver();
        await using (driver)
        {
            await driver.ConnectAsync();

            transport.EnqueueResponse(BuildExceptionResponse(0x15, 0x02));

            var result = await driver.WriteFileRecordAsync(1, 0, new byte[] { 0x00, 0x01 });

            Assert.False(result.IsSuccess);
        }
    }

    // =========================================================================
    // FC 24 - ReadFifoQueueAsync
    // =========================================================================

    [Fact]
    public async Task ReadFifoQueue_Success()
    {
        var (transport, driver) = CreateTestDriver();
        await using (driver)
        {
            await driver.ConnectAsync();

            // FC 24 response data: byteCount(2) + fifoCount(2) + values(N*2)
            // byteCount = 2 + fifoCount * 2
            ushort fifoCount = 3;
            var totalBytes = (ushort)(2 + fifoCount * 2);
            var data = new byte[2 + 2 + fifoCount * 2];
            BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(0), totalBytes);
            BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(2), fifoCount);
            BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(4), 10);
            BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(6), 20);
            BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(8), 30);

            transport.EnqueueResponse(BuildResponse(0x18, data));

            var values = await driver.ReadFifoQueueAsync("HR0");

            Assert.Equal(3, values.Length);
            Assert.Equal(10, values[0]);
            Assert.Equal(20, values[1]);
            Assert.Equal(30, values[2]);
        }
    }

    [Fact]
    public async Task ReadFifoQueue_EmptyQueue()
    {
        var (transport, driver) = CreateTestDriver();
        await using (driver)
        {
            await driver.ConnectAsync();

            var data = new byte[4];
            BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(0), 2); // byte count
            BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(2), 0); // fifo count
            transport.EnqueueResponse(BuildResponse(0x18, data));

            var values = await driver.ReadFifoQueueAsync("HR0");

            Assert.Empty(values);
        }
    }

    // =========================================================================
    // Multi-register typed reads
    // =========================================================================

    [Fact]
    public async Task ReadFloat32Async_Success()
    {
        var (transport, driver) = CreateTestDriver();
        await using (driver)
        {
            await driver.ConnectAsync();

            // FC 03 response with 2 registers (4 bytes) representing float 3.14f
            float expected = 3.14f;
            var bytes = new byte[4];
            BinaryPrimitives.WriteSingleBigEndian(bytes, expected);

            // Response: byteCount(1) + 4 data bytes
            var data = new byte[5];
            data[0] = 4;
            Array.Copy(bytes, 0, data, 1, 4);
            transport.EnqueueResponse(BuildResponse(0x03, data));

            var result = await driver.ReadFloat32Async("HR0");

            Assert.Equal(expected, result, 5);
        }
    }

    [Fact]
    public async Task ReadInt32Async_Success()
    {
        var (transport, driver) = CreateTestDriver();
        await using (driver)
        {
            await driver.ConnectAsync();

            int expected = 123456;
            var bytes = new byte[4];
            BinaryPrimitives.WriteInt32BigEndian(bytes, expected);

            var data = new byte[5];
            data[0] = 4;
            Array.Copy(bytes, 0, data, 1, 4);
            transport.EnqueueResponse(BuildResponse(0x03, data));

            var result = await driver.ReadInt32Async("HR0");

            Assert.Equal(expected, result);
        }
    }

    [Fact]
    public async Task ReadInt32Async_NegativeValue()
    {
        var (transport, driver) = CreateTestDriver();
        await using (driver)
        {
            await driver.ConnectAsync();

            int expected = -98765;
            var bytes = new byte[4];
            BinaryPrimitives.WriteInt32BigEndian(bytes, expected);

            var data = new byte[5];
            data[0] = 4;
            Array.Copy(bytes, 0, data, 1, 4);
            transport.EnqueueResponse(BuildResponse(0x03, data));

            var result = await driver.ReadInt32Async("HR0");

            Assert.Equal(expected, result);
        }
    }

    [Fact]
    public async Task ReadStringAsync_Success()
    {
        var (transport, driver) = CreateTestDriver();
        await using (driver)
        {
            await driver.ConnectAsync();

            string expected = "HELLO";
            var strBytes = Encoding.ASCII.GetBytes(expected);
            // 3 registers = 6 bytes; pad with nulls
            var regBytes = new byte[6];
            Array.Copy(strBytes, regBytes, strBytes.Length);

            var data = new byte[7];
            data[0] = 6; // byte count
            Array.Copy(regBytes, 0, data, 1, 6);
            transport.EnqueueResponse(BuildResponse(0x03, data));

            var result = await driver.ReadStringAsync("HR0", 3);

            Assert.Equal("HELLO", result);
        }
    }

    // =========================================================================
    // Multi-register typed writes
    // =========================================================================

    [Fact]
    public async Task WriteFloat32Async_Success()
    {
        var (transport, driver) = CreateTestDriver();
        await using (driver)
        {
            await driver.ConnectAsync();

            // WriteMultipleRegisters (FC 10) echo response
            transport.EnqueueResponse(BuildWriteMultipleResponse(0, 2));

            var result = await driver.WriteFloat32Async("HR0", 3.14f);

            Assert.True(result.IsSuccess, $"Expected success: {result.Error}");
        }
    }

    [Fact]
    public async Task WriteInt32Async_Success()
    {
        var (transport, driver) = CreateTestDriver();
        await using (driver)
        {
            await driver.ConnectAsync();

            transport.EnqueueResponse(BuildWriteMultipleResponse(0, 2));

            var result = await driver.WriteInt32Async("HR0", 123456);

            Assert.True(result.IsSuccess, $"Expected success: {result.Error}");
        }
    }

    [Fact]
    public async Task WriteStringAsync_Success()
    {
        var (transport, driver) = CreateTestDriver();
        await using (driver)
        {
            await driver.ConnectAsync();

            transport.EnqueueResponse(BuildWriteMultipleResponse(0, 5));

            var result = await driver.WriteStringAsync("HR0", "HELLO", 5);

            Assert.True(result.IsSuccess, $"Expected success: {result.Error}");
        }
    }

    [Fact]
    public async Task WriteFloat32Async_NonHR_ReturnsFailure()
    {
        var (_, driver) = CreateTestDriver();
        await using (driver)
        {
            await driver.ConnectAsync();

            var result = await driver.WriteFloat32Async("IR0", 1.0f);

            Assert.False(result.IsSuccess);
            Assert.Contains("Holding Register", result.Error);
        }
    }

    // =========================================================================
    // SendRawAsync
    // =========================================================================

    [Fact]
    public async Task SendRawAsync_Success_CustomFC()
    {
        var (transport, driver) = CreateTestDriver();
        await using (driver)
        {
            await driver.ConnectAsync();

            var responsePayload = new byte[] { 0xAA, 0xBB };
            transport.EnqueueResponse(BuildResponse(0x41, responsePayload));

            var result = await driver.SendRawAsync(0x41, new byte[] { 0x01, 0x02 });

            Assert.False(result.IsException);
            Assert.Null(result.ErrorMessage);
            Assert.Equal(0x41, result.FunctionCode);
            Assert.Equal(2, result.Data.Length);
            Assert.Equal(0xAA, result.Data.Span[0]);
            Assert.Equal(0xBB, result.Data.Span[1]);
        }
    }

    [Fact]
    public async Task SendRawAsync_ExceptionResponse()
    {
        var (transport, driver) = CreateTestDriver();
        await using (driver)
        {
            await driver.ConnectAsync();

            transport.EnqueueResponse(BuildExceptionResponse(0x41, 0x01));

            var result = await driver.SendRawAsync(0x41, new byte[] { 0x01 });

            Assert.True(result.IsException);
            Assert.NotNull(result.ErrorMessage);
            Assert.Equal((byte)0x01, result.ExceptionCode);
        }
    }

    [Fact]
    public async Task SendRawAsync_EmptyPayload_Success()
    {
        var (transport, driver) = CreateTestDriver();
        await using (driver)
        {
            await driver.ConnectAsync();

            transport.EnqueueResponse(BuildResponse(0x42, Array.Empty<byte>()));

            var result = await driver.SendRawAsync(0x42, ReadOnlyMemory<byte>.Empty);

            Assert.False(result.IsException);
            Assert.Equal(0x42, result.FunctionCode);
        }
    }

    // =========================================================================
    // DefaultByteOrder
    // =========================================================================

    [Fact]
    public void DefaultByteOrder_DefaultIs_ABCD()
    {
        var driver = new ModbusDriver("127.0.0.1");
        Assert.Equal(ModbusByteOrder.ABCD, driver.DefaultByteOrder);
        driver.Dispose();
    }

    [Theory]
    [InlineData(ModbusByteOrder.ABCD)]
    [InlineData(ModbusByteOrder.DCBA)]
    [InlineData(ModbusByteOrder.BADC)]
    [InlineData(ModbusByteOrder.CDAB)]
    public void DefaultByteOrder_CustomOrder_IsPreserved(ModbusByteOrder order)
    {
        var driver = new ModbusDriver("127.0.0.1", byteOrder: order);
        Assert.Equal(order, driver.DefaultByteOrder);
        driver.Dispose();
    }

    [Fact]
    public async Task ReadFloat32_WithCustomByteOrder_CDAB()
    {
        var (transport, driver) = CreateTestDriver(ModbusByteOrder.CDAB);
        await using (driver)
        {
            await driver.ConnectAsync();

            // For CDAB byte order, the device sends bytes in C D A B order
            // We want to read float 3.14f, which in big-endian (ABCD) is some 4 bytes.
            float expected = 3.14f;
            var abcd = new byte[4];
            BinaryPrimitives.WriteSingleBigEndian(abcd, expected);

            // CDAB: swap words -> C D A B
            var wire = new byte[] { abcd[2], abcd[3], abcd[0], abcd[1] };

            var data = new byte[5];
            data[0] = 4;
            Array.Copy(wire, 0, data, 1, 4);
            transport.EnqueueResponse(BuildResponse(0x03, data));

            var result = await driver.ReadFloat32Async("HR0");

            Assert.Equal(expected, result, 5);
        }
    }

    // =========================================================================
    // Additional edge case tests
    // =========================================================================

    [Fact]
    public async Task MaskWriteRegister_ExceptionResponse_ReturnsFailure()
    {
        var (transport, driver) = CreateTestDriver();
        await using (driver)
        {
            await driver.ConnectAsync();

            transport.EnqueueResponse(BuildExceptionResponse(0x16, 0x02));

            var result = await driver.MaskWriteRegisterAsync("HR100", 0xFFFF, 0x0001);

            Assert.False(result.IsSuccess);
            Assert.Contains("Mask write failed", result.Error);
        }
    }

    [Fact]
    public async Task ReadWriteMultipleRegisters_ExceptionResponse_ReturnsFailure()
    {
        var (transport, driver) = CreateTestDriver();
        await using (driver)
        {
            await driver.ConnectAsync();

            transport.EnqueueResponse(BuildExceptionResponse(0x17, 0x02));

            var result = await driver.ReadWriteMultipleRegistersAsync(
                "HR0", 1, "HR10", new short[] { 1 });

            Assert.False(result.IsSuccess);
        }
    }

    [Fact]
    public async Task ReadDeviceIdentification_ExceptionResponse_Throws()
    {
        var (transport, driver) = CreateTestDriver();
        await using (driver)
        {
            await driver.ConnectAsync();

            transport.EnqueueResponse(BuildExceptionResponse(0x2B, 0x01));

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => driver.ReadDeviceIdentificationAsync().AsTask());
        }
    }

    [Fact]
    public async Task ReadFifoQueue_ExceptionResponse_Throws()
    {
        var (transport, driver) = CreateTestDriver();
        await using (driver)
        {
            await driver.ConnectAsync();

            transport.EnqueueResponse(BuildExceptionResponse(0x18, 0x02));

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => driver.ReadFifoQueueAsync("HR0").AsTask());
        }
    }

    [Fact]
    public async Task ReadFloat32Async_ExceptionResponse_Throws()
    {
        var (transport, driver) = CreateTestDriver();
        await using (driver)
        {
            await driver.ConnectAsync();

            transport.EnqueueResponse(BuildExceptionResponse(0x03, 0x02));

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => driver.ReadFloat32Async("HR0").AsTask());
        }
    }

    [Fact]
    public async Task WriteInt32Async_ExceptionResponse_ReturnsFailure()
    {
        var (transport, driver) = CreateTestDriver();
        await using (driver)
        {
            await driver.ConnectAsync();

            transport.EnqueueResponse(BuildExceptionResponse(0x10, 0x02));

            var result = await driver.WriteInt32Async("HR0", 42);

            Assert.False(result.IsSuccess);
        }
    }
}
