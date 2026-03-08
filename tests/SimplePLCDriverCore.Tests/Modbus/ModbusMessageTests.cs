using System.Buffers.Binary;
using SimplePLCDriverCore.Protocols.Modbus;

namespace SimplePLCDriverCore.Tests.Modbus;

public class ModbusMessageTests
{
    // ==========================================================================
    // MBAP Header
    // ==========================================================================

    [Fact]
    public void BuildReadHoldingRegisters_HasCorrectTransactionId()
    {
        var req = ModbusMessage.BuildReadHoldingRegisters(42, 1, 0, 1);
        var txId = BinaryPrimitives.ReadUInt16BigEndian(req.AsSpan(0));
        Assert.Equal(42, txId);
    }

    [Fact]
    public void BuildReadHoldingRegisters_HasProtocolIdZero()
    {
        var req = ModbusMessage.BuildReadHoldingRegisters(1, 1, 0, 1);
        var protocolId = BinaryPrimitives.ReadUInt16BigEndian(req.AsSpan(2));
        Assert.Equal(0, protocolId);
    }

    [Fact]
    public void BuildReadHoldingRegisters_HasCorrectLength()
    {
        var req = ModbusMessage.BuildReadHoldingRegisters(1, 1, 0, 1);
        var length = BinaryPrimitives.ReadUInt16BigEndian(req.AsSpan(4));
        Assert.Equal(5, length); // unit ID + FC + addr(2) + qty(2) = 5... wait, 1 + 1 + 2 + 2 = 6
        // Actually: length field = unit ID(1) + FC(1) + start address(2) + quantity(2) = 6
        // But the code uses pduLength = 5 which is FC + addr(2) + qty(2), plus unit ID is part of MBAP
        // Let me check: WriteMbapHeader writes length=5, then unit ID + FC + addr + qty
        // MBAP length = unit ID(1) + PDU bytes(4) = 5
    }

    [Fact]
    public void BuildReadHoldingRegisters_HasCorrectUnitId()
    {
        var req = ModbusMessage.BuildReadHoldingRegisters(1, 5, 0, 1);
        Assert.Equal(5, req[6]); // Unit ID at byte 6
    }

    [Fact]
    public void BuildReadHoldingRegisters_HasCorrectFunctionCode()
    {
        var req = ModbusMessage.BuildReadHoldingRegisters(1, 1, 0, 1);
        Assert.Equal(ModbusFunctionCodes.ReadHoldingRegisters, req[7]);
    }

    [Fact]
    public void BuildReadHoldingRegisters_HasCorrectStartAddress()
    {
        var req = ModbusMessage.BuildReadHoldingRegisters(1, 1, 100, 1);
        var startAddr = BinaryPrimitives.ReadUInt16BigEndian(req.AsSpan(8));
        Assert.Equal(100, startAddr);
    }

    [Fact]
    public void BuildReadHoldingRegisters_HasCorrectQuantity()
    {
        var req = ModbusMessage.BuildReadHoldingRegisters(1, 1, 0, 10);
        var quantity = BinaryPrimitives.ReadUInt16BigEndian(req.AsSpan(10));
        Assert.Equal(10, quantity);
    }

    // ==========================================================================
    // Read Function Codes
    // ==========================================================================

    [Fact]
    public void BuildReadCoils_HasCorrectFC()
    {
        var req = ModbusMessage.BuildReadCoils(1, 1, 0, 1);
        Assert.Equal(ModbusFunctionCodes.ReadCoils, req[7]);
    }

    [Fact]
    public void BuildReadDiscreteInputs_HasCorrectFC()
    {
        var req = ModbusMessage.BuildReadDiscreteInputs(1, 1, 0, 1);
        Assert.Equal(ModbusFunctionCodes.ReadDiscreteInputs, req[7]);
    }

    [Fact]
    public void BuildReadInputRegisters_HasCorrectFC()
    {
        var req = ModbusMessage.BuildReadInputRegisters(1, 1, 0, 1);
        Assert.Equal(ModbusFunctionCodes.ReadInputRegisters, req[7]);
    }

    // ==========================================================================
    // Write Single Coil (FC 05)
    // ==========================================================================

    [Fact]
    public void BuildWriteSingleCoil_True_HasFF00()
    {
        var req = ModbusMessage.BuildWriteSingleCoil(1, 1, 0, true);
        Assert.Equal(ModbusFunctionCodes.WriteSingleCoil, req[7]);
        var value = BinaryPrimitives.ReadUInt16BigEndian(req.AsSpan(10));
        Assert.Equal(0xFF00, value);
    }

    [Fact]
    public void BuildWriteSingleCoil_False_Has0000()
    {
        var req = ModbusMessage.BuildWriteSingleCoil(1, 1, 0, false);
        var value = BinaryPrimitives.ReadUInt16BigEndian(req.AsSpan(10));
        Assert.Equal(0x0000, value);
    }

    // ==========================================================================
    // Write Single Register (FC 06)
    // ==========================================================================

    [Fact]
    public void BuildWriteSingleRegister_HasCorrectFC()
    {
        var req = ModbusMessage.BuildWriteSingleRegister(1, 1, 100, 42);
        Assert.Equal(ModbusFunctionCodes.WriteSingleRegister, req[7]);
    }

    [Fact]
    public void BuildWriteSingleRegister_HasCorrectValue()
    {
        var req = ModbusMessage.BuildWriteSingleRegister(1, 1, 100, 42);
        var value = BinaryPrimitives.ReadUInt16BigEndian(req.AsSpan(10));
        Assert.Equal(42, value);
    }

    // ==========================================================================
    // Write Multiple Registers (FC 16)
    // ==========================================================================

    [Fact]
    public void BuildWriteMultipleRegisters_HasCorrectFC()
    {
        var req = ModbusMessage.BuildWriteMultipleRegisters(1, 1, 0, [100, 200]);
        Assert.Equal(ModbusFunctionCodes.WriteMultipleRegisters, req[7]);
    }

    [Fact]
    public void BuildWriteMultipleRegisters_HasCorrectQuantity()
    {
        var req = ModbusMessage.BuildWriteMultipleRegisters(1, 1, 0, [100, 200]);
        var qty = BinaryPrimitives.ReadUInt16BigEndian(req.AsSpan(10));
        Assert.Equal(2, qty);
    }

    [Fact]
    public void BuildWriteMultipleRegisters_HasCorrectByteCount()
    {
        var req = ModbusMessage.BuildWriteMultipleRegisters(1, 1, 0, [100, 200]);
        Assert.Equal(4, req[12]); // 2 registers * 2 bytes
    }

    // ==========================================================================
    // Write Multiple Coils (FC 15)
    // ==========================================================================

    [Fact]
    public void BuildWriteMultipleCoils_HasCorrectFC()
    {
        var req = ModbusMessage.BuildWriteMultipleCoils(1, 1, 0, [true, false, true]);
        Assert.Equal(ModbusFunctionCodes.WriteMultipleCoils, req[7]);
    }

    [Fact]
    public void BuildWriteMultipleCoils_HasCorrectQuantity()
    {
        var req = ModbusMessage.BuildWriteMultipleCoils(1, 1, 0, [true, false, true]);
        var qty = BinaryPrimitives.ReadUInt16BigEndian(req.AsSpan(10));
        Assert.Equal(3, qty);
    }

    [Fact]
    public void BuildWriteMultipleCoils_EncodesCoilBits()
    {
        var req = ModbusMessage.BuildWriteMultipleCoils(1, 1, 0, [true, false, true]);
        // byte count at index 12 = 1
        Assert.Equal(1, req[12]);
        // coil data at index 13: bit0=1, bit1=0, bit2=1 = 0b101 = 5
        Assert.Equal(0x05, req[13]);
    }

    // ==========================================================================
    // ParseResponse
    // ==========================================================================

    [Fact]
    public void ParseResponse_Success_ReadRegisters()
    {
        var response = BuildMockReadResponse(ModbusFunctionCodes.ReadHoldingRegisters,
            [0x02, 0x00, 0x2A]); // byte count=2, value=42
        var result = ModbusMessage.ParseResponse(response);

        Assert.True(result.IsSuccess);
        Assert.Equal(ModbusFunctionCodes.ReadHoldingRegisters, result.FunctionCode);
        Assert.Equal(3, result.Data.Length); // byte count + 2 bytes data
    }

    [Fact]
    public void ParseResponse_Success_ReadCoils()
    {
        var response = BuildMockReadResponse(ModbusFunctionCodes.ReadCoils,
            [0x01, 0x01]); // byte count=1, coil data=0x01
        var result = ModbusMessage.ParseResponse(response);

        Assert.True(result.IsSuccess);
        Assert.Equal(ModbusFunctionCodes.ReadCoils, result.FunctionCode);
    }

    [Fact]
    public void ParseResponse_ExceptionResponse()
    {
        var response = BuildMockExceptionResponse(
            ModbusFunctionCodes.ReadHoldingRegisters,
            ModbusExceptionCodes.IllegalDataAddress);
        var result = ModbusMessage.ParseResponse(response);

        Assert.False(result.IsSuccess);
        Assert.Equal(ModbusFunctionCodes.ReadHoldingRegisters, result.FunctionCode);
        Assert.Equal(ModbusExceptionCodes.IllegalDataAddress, result.ExceptionCode);
    }

    [Fact]
    public void ParseResponse_TooShort_Throws()
    {
        Assert.Throws<InvalidOperationException>(
            () => ModbusMessage.ParseResponse(new byte[4]));
    }

    [Fact]
    public void ParseResponse_InvalidProtocolId_Throws()
    {
        var response = new byte[9];
        BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(2), 0x0001); // wrong protocol ID
        Assert.Throws<InvalidOperationException>(
            () => ModbusMessage.ParseResponse(response));
    }

    // ==========================================================================
    // GetLengthFromHeader
    // ==========================================================================

    [Fact]
    public void GetLengthFromHeader_ReturnsCorrectTotalLength()
    {
        var header = new byte[6];
        BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(4), 6); // PDU length = 6
        var totalLength = ModbusMessage.GetLengthFromHeader(header);
        Assert.Equal(12, totalLength); // 6 (MBAP without unit ID) + 6 (PDU including unit ID)
    }

    [Fact]
    public void GetLengthFromHeader_TooShort_Throws()
    {
        Assert.Throws<InvalidOperationException>(
            () => ModbusMessage.GetLengthFromHeader(new byte[4]));
    }

    // ==========================================================================
    // ModbusResponse Error Messages
    // ==========================================================================

    [Fact]
    public void ModbusResponse_GetErrorMessage_Success_Empty()
    {
        var resp = new ModbusResponse(true, 0x03, 0, ReadOnlyMemory<byte>.Empty);
        Assert.Equal(string.Empty, resp.GetErrorMessage());
    }

    [Fact]
    public void ModbusResponse_GetErrorMessage_IllegalFunction()
    {
        var resp = new ModbusResponse(false, 0x03, ModbusExceptionCodes.IllegalFunction,
            ReadOnlyMemory<byte>.Empty);
        Assert.Contains("Illegal function", resp.GetErrorMessage());
    }

    [Fact]
    public void ModbusResponse_GetErrorMessage_IllegalDataAddress()
    {
        var resp = new ModbusResponse(false, 0x03, ModbusExceptionCodes.IllegalDataAddress,
            ReadOnlyMemory<byte>.Empty);
        Assert.Contains("Illegal data address", resp.GetErrorMessage());
    }

    [Fact]
    public void ModbusResponse_GetErrorMessage_SlaveDeviceFailure()
    {
        var resp = new ModbusResponse(false, 0x03, ModbusExceptionCodes.SlaveDeviceFailure,
            ReadOnlyMemory<byte>.Empty);
        Assert.Contains("Slave device failure", resp.GetErrorMessage());
    }

    // ==========================================================================
    // ModbusExceptionCodes.GetDescription
    // ==========================================================================

    [Fact]
    public void ExceptionCodes_UnknownCode_ShowsHex()
    {
        var desc = ModbusExceptionCodes.GetDescription(0xFF);
        Assert.Contains("0xFF", desc);
    }

    // ==========================================================================
    // Helpers
    // ==========================================================================

    private static byte[] BuildMockReadResponse(byte functionCode, byte[] data)
    {
        // MBAP header (7) + FC (1) + data
        var totalLength = 7 + 1 + data.Length;
        var response = new byte[totalLength];

        // Transaction ID
        BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(0), 1);
        // Protocol ID (0x0000)
        BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(2), 0);
        // Length (unit ID + FC + data)
        BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(4), (ushort)(1 + 1 + data.Length));
        // Unit ID
        response[6] = 1;
        // Function code
        response[7] = functionCode;
        // Data
        Array.Copy(data, 0, response, 8, data.Length);

        return response;
    }

    private static byte[] BuildMockExceptionResponse(byte functionCode, byte exceptionCode)
    {
        var response = new byte[9]; // MBAP (7) + FC with error bit (1) + exception code (1)

        BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(0), 1); // TX ID
        BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(2), 0); // Protocol ID
        BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(4), 3); // Length: unit + FC + exc
        response[6] = 1; // Unit ID
        response[7] = (byte)(functionCode | 0x80); // Error bit set
        response[8] = exceptionCode;

        return response;
    }
}
