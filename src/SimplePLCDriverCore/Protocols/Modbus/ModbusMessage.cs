using System.Buffers.Binary;
using SimplePLCDriverCore.Common.Buffers;

namespace SimplePLCDriverCore.Protocols.Modbus;

/// <summary>
/// Modbus function codes.
/// </summary>
internal static class ModbusFunctionCodes
{
    public const byte ReadCoils = 0x01;
    public const byte ReadDiscreteInputs = 0x02;
    public const byte ReadHoldingRegisters = 0x03;
    public const byte ReadInputRegisters = 0x04;
    public const byte WriteSingleCoil = 0x05;
    public const byte WriteSingleRegister = 0x06;
    public const byte WriteMultipleCoils = 0x0F;
    public const byte WriteMultipleRegisters = 0x10;
}

/// <summary>
/// Modbus exception codes returned by the device.
/// </summary>
internal static class ModbusExceptionCodes
{
    public const byte IllegalFunction = 0x01;
    public const byte IllegalDataAddress = 0x02;
    public const byte IllegalDataValue = 0x03;
    public const byte SlaveDeviceFailure = 0x04;
    public const byte Acknowledge = 0x05;
    public const byte SlaveDeviceBusy = 0x06;
    public const byte MemoryParityError = 0x08;
    public const byte GatewayPathUnavailable = 0x0A;
    public const byte GatewayTargetDeviceFailedToRespond = 0x0B;

    public static string GetDescription(byte code) => code switch
    {
        IllegalFunction => "Illegal function",
        IllegalDataAddress => "Illegal data address",
        IllegalDataValue => "Illegal data value",
        SlaveDeviceFailure => "Slave device failure",
        Acknowledge => "Acknowledge (processing)",
        SlaveDeviceBusy => "Slave device busy",
        MemoryParityError => "Memory parity error",
        GatewayPathUnavailable => "Gateway path unavailable",
        GatewayTargetDeviceFailedToRespond => "Gateway target device failed to respond",
        _ => $"Unknown exception (0x{code:X2})"
    };
}

/// <summary>
/// Parsed Modbus response.
/// </summary>
internal readonly struct ModbusResponse
{
    public bool IsSuccess { get; }
    public byte FunctionCode { get; }
    public byte ExceptionCode { get; }
    public ReadOnlyMemory<byte> Data { get; }

    public ModbusResponse(bool isSuccess, byte functionCode, byte exceptionCode,
        ReadOnlyMemory<byte> data)
    {
        IsSuccess = isSuccess;
        FunctionCode = functionCode;
        ExceptionCode = exceptionCode;
        Data = data;
    }

    public string GetErrorMessage()
    {
        if (IsSuccess) return string.Empty;
        return $"Modbus exception: {ModbusExceptionCodes.GetDescription(ExceptionCode)} " +
               $"(function=0x{FunctionCode:X2}, exception=0x{ExceptionCode:X2})";
    }
}

/// <summary>
/// Modbus TCP message builder and parser.
///
/// MBAP (Modbus Application Protocol) Header (7 bytes):
///   byte 0-1: Transaction ID (big-endian)
///   byte 2-3: Protocol ID (always 0x0000 for Modbus TCP)
///   byte 4-5: Length (big-endian, number of following bytes including unit ID)
///   byte 6:   Unit ID (slave address, typically 1 or 0xFF)
///
/// Followed by PDU:
///   byte 7:   Function code
///   byte 8+:  Data (varies by function)
/// </summary>
internal static class ModbusMessage
{
    public const int MbapHeaderSize = 7;
    public const ushort ProtocolId = 0x0000;

    /// <summary>
    /// Build a Read Coils (FC 01) request.
    /// </summary>
    public static byte[] BuildReadCoils(ushort transactionId, byte unitId,
        ushort startAddress, ushort quantity)
    {
        return BuildReadRequest(transactionId, unitId,
            ModbusFunctionCodes.ReadCoils, startAddress, quantity);
    }

    /// <summary>
    /// Build a Read Discrete Inputs (FC 02) request.
    /// </summary>
    public static byte[] BuildReadDiscreteInputs(ushort transactionId, byte unitId,
        ushort startAddress, ushort quantity)
    {
        return BuildReadRequest(transactionId, unitId,
            ModbusFunctionCodes.ReadDiscreteInputs, startAddress, quantity);
    }

    /// <summary>
    /// Build a Read Holding Registers (FC 03) request.
    /// </summary>
    public static byte[] BuildReadHoldingRegisters(ushort transactionId, byte unitId,
        ushort startAddress, ushort quantity)
    {
        return BuildReadRequest(transactionId, unitId,
            ModbusFunctionCodes.ReadHoldingRegisters, startAddress, quantity);
    }

    /// <summary>
    /// Build a Read Input Registers (FC 04) request.
    /// </summary>
    public static byte[] BuildReadInputRegisters(ushort transactionId, byte unitId,
        ushort startAddress, ushort quantity)
    {
        return BuildReadRequest(transactionId, unitId,
            ModbusFunctionCodes.ReadInputRegisters, startAddress, quantity);
    }

    /// <summary>
    /// Build a Write Single Coil (FC 05) request.
    /// </summary>
    public static byte[] BuildWriteSingleCoil(ushort transactionId, byte unitId,
        ushort address, bool value)
    {
        using var writer = new PacketWriter(16);

        var pduLength = 5; // unit ID + function code + address(2) + value(2)

        // MBAP header
        WriteMbapHeader(writer, transactionId, (ushort)pduLength);

        // PDU
        writer.WriteUInt8(unitId);
        writer.WriteUInt8(ModbusFunctionCodes.WriteSingleCoil);
        writer.WriteUInt16BE(address);
        writer.WriteUInt16BE(value ? (ushort)0xFF00 : (ushort)0x0000);

        return writer.ToArray();
    }

    /// <summary>
    /// Build a Write Single Register (FC 06) request.
    /// </summary>
    public static byte[] BuildWriteSingleRegister(ushort transactionId, byte unitId,
        ushort address, ushort value)
    {
        using var writer = new PacketWriter(16);

        var pduLength = 5; // unit ID + function code + address(2) + value(2)

        // MBAP header
        WriteMbapHeader(writer, transactionId, (ushort)pduLength);

        // PDU
        writer.WriteUInt8(unitId);
        writer.WriteUInt8(ModbusFunctionCodes.WriteSingleRegister);
        writer.WriteUInt16BE(address);
        writer.WriteUInt16BE(value);

        return writer.ToArray();
    }

    /// <summary>
    /// Build a Write Multiple Registers (FC 16) request.
    /// </summary>
    public static byte[] BuildWriteMultipleRegisters(ushort transactionId, byte unitId,
        ushort startAddress, ushort[] values)
    {
        using var writer = new PacketWriter(32 + values.Length * 2);

        var byteCount = values.Length * 2;
        var pduLength = 6 + byteCount; // unit + fc + addr(2) + qty(2) + byteCount(1) + data

        // MBAP header
        WriteMbapHeader(writer, transactionId, (ushort)pduLength);

        // PDU
        writer.WriteUInt8(unitId);
        writer.WriteUInt8(ModbusFunctionCodes.WriteMultipleRegisters);
        writer.WriteUInt16BE(startAddress);
        writer.WriteUInt16BE((ushort)values.Length);
        writer.WriteUInt8((byte)byteCount);

        foreach (var v in values)
            writer.WriteUInt16BE(v);

        return writer.ToArray();
    }

    /// <summary>
    /// Build a Write Multiple Coils (FC 15) request.
    /// </summary>
    public static byte[] BuildWriteMultipleCoils(ushort transactionId, byte unitId,
        ushort startAddress, bool[] values)
    {
        var byteCount = (values.Length + 7) / 8;
        var coilBytes = new byte[byteCount];

        for (int i = 0; i < values.Length; i++)
        {
            if (values[i])
                coilBytes[i / 8] |= (byte)(1 << (i % 8));
        }

        using var writer = new PacketWriter(32 + byteCount);

        var pduLength = 6 + byteCount; // unit + fc + addr(2) + qty(2) + byteCount(1) + data

        // MBAP header
        WriteMbapHeader(writer, transactionId, (ushort)pduLength);

        // PDU
        writer.WriteUInt8(unitId);
        writer.WriteUInt8(ModbusFunctionCodes.WriteMultipleCoils);
        writer.WriteUInt16BE(startAddress);
        writer.WriteUInt16BE((ushort)values.Length);
        writer.WriteUInt8((byte)byteCount);
        writer.WriteBytes(coilBytes);

        return writer.ToArray();
    }

    /// <summary>
    /// Parse a Modbus TCP response.
    /// </summary>
    public static ModbusResponse ParseResponse(ReadOnlySpan<byte> data)
    {
        if (data.Length < MbapHeaderSize + 1)
            throw new InvalidOperationException($"Modbus response too short: {data.Length} bytes.");

        // MBAP header
        var protocolId = BinaryPrimitives.ReadUInt16BigEndian(data[2..]);
        if (protocolId != ProtocolId)
            throw new InvalidOperationException($"Invalid Modbus protocol ID: 0x{protocolId:X4}.");

        var unitId = data[6];
        var functionCode = data[7];

        // Check for exception response (bit 7 set)
        if ((functionCode & 0x80) != 0)
        {
            var exceptionCode = data.Length > 8 ? data[8] : (byte)0;
            return new ModbusResponse(false, (byte)(functionCode & 0x7F), exceptionCode,
                ReadOnlyMemory<byte>.Empty);
        }

        // Normal response - extract data after function code
        var responseData = data.Length > 8
            ? data[8..].ToArray()
            : Array.Empty<byte>();

        return new ModbusResponse(true, functionCode, 0, responseData);
    }

    /// <summary>
    /// Get the total frame length from the MBAP header.
    /// Used with ReceiveFramedAsync.
    /// </summary>
    public static int GetLengthFromHeader(byte[] header)
    {
        if (header.Length < 6)
            throw new InvalidOperationException("MBAP header too short.");

        var pduLength = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(4));
        return 6 + pduLength; // MBAP header (6 bytes without unit ID) + PDU length (includes unit ID)
    }

    private static byte[] BuildReadRequest(ushort transactionId, byte unitId,
        byte functionCode, ushort startAddress, ushort quantity)
    {
        using var writer = new PacketWriter(16);

        var pduLength = 5; // unit ID + function code + start address(2) + quantity(2)

        // MBAP header
        WriteMbapHeader(writer, transactionId, (ushort)pduLength);

        // PDU
        writer.WriteUInt8(unitId);
        writer.WriteUInt8(functionCode);
        writer.WriteUInt16BE(startAddress);
        writer.WriteUInt16BE(quantity);

        return writer.ToArray();
    }

    private static void WriteMbapHeader(PacketWriter writer, ushort transactionId, ushort length)
    {
        writer.WriteUInt16BE(transactionId);
        writer.WriteUInt16BE(ProtocolId);
        writer.WriteUInt16BE(length);
    }
}
