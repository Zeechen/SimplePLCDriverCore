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

    // Phase 5: Advanced function codes
    public const byte Diagnostics = 0x08;
    public const byte ReadFileRecord = 0x14;
    public const byte WriteFileRecord = 0x15;
    public const byte MaskWriteRegister = 0x16;
    public const byte ReadWriteMultipleRegisters = 0x17;
    public const byte ReadFifoQueue = 0x18;
    public const byte EncapsulatedInterfaceTransport = 0x2B;
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

    // ===== Phase 5: Advanced Function Code Builders =====

    /// <summary>
    /// Build a Diagnostics (FC 08) request.
    /// </summary>
    public static byte[] BuildDiagnostics(ushort transactionId, byte unitId,
        ushort subFunction, ushort data)
    {
        using var writer = new PacketWriter(16);
        var pduLength = 5; // unit + fc + sub-function(2) + data(2)
        WriteMbapHeader(writer, transactionId, (ushort)pduLength);
        writer.WriteUInt8(unitId);
        writer.WriteUInt8(ModbusFunctionCodes.Diagnostics);
        writer.WriteUInt16BE(subFunction);
        writer.WriteUInt16BE(data);
        return writer.ToArray();
    }

    /// <summary>
    /// Build a Mask Write Register (FC 22) request.
    /// Result = (Current AND And_Mask) OR (Or_Mask AND NOT(And_Mask))
    /// </summary>
    public static byte[] BuildMaskWriteRegister(ushort transactionId, byte unitId,
        ushort address, ushort andMask, ushort orMask)
    {
        using var writer = new PacketWriter(16);
        var pduLength = 7; // unit + fc + address(2) + andMask(2) + orMask(2)
        WriteMbapHeader(writer, transactionId, (ushort)pduLength);
        writer.WriteUInt8(unitId);
        writer.WriteUInt8(ModbusFunctionCodes.MaskWriteRegister);
        writer.WriteUInt16BE(address);
        writer.WriteUInt16BE(andMask);
        writer.WriteUInt16BE(orMask);
        return writer.ToArray();
    }

    /// <summary>
    /// Build a Read/Write Multiple Registers (FC 23) request.
    /// </summary>
    public static byte[] BuildReadWriteMultipleRegisters(ushort transactionId, byte unitId,
        ushort readAddress, ushort readQuantity,
        ushort writeAddress, ushort[] writeValues)
    {
        var writeByteCount = writeValues.Length * 2;
        using var writer = new PacketWriter(32 + writeByteCount);
        // unit + fc + readAddr(2) + readQty(2) + writeAddr(2) + writeQty(2) + byteCount(1) + data
        var pduLength = 10 + writeByteCount;
        WriteMbapHeader(writer, transactionId, (ushort)pduLength);
        writer.WriteUInt8(unitId);
        writer.WriteUInt8(ModbusFunctionCodes.ReadWriteMultipleRegisters);
        writer.WriteUInt16BE(readAddress);
        writer.WriteUInt16BE(readQuantity);
        writer.WriteUInt16BE(writeAddress);
        writer.WriteUInt16BE((ushort)writeValues.Length);
        writer.WriteUInt8((byte)writeByteCount);
        foreach (var v in writeValues)
            writer.WriteUInt16BE(v);
        return writer.ToArray();
    }

    /// <summary>
    /// Build a Read FIFO Queue (FC 24) request.
    /// </summary>
    public static byte[] BuildReadFifoQueue(ushort transactionId, byte unitId,
        ushort pointerAddress)
    {
        using var writer = new PacketWriter(16);
        var pduLength = 3; // unit + fc + pointer address(2)
        WriteMbapHeader(writer, transactionId, (ushort)pduLength);
        writer.WriteUInt8(unitId);
        writer.WriteUInt8(ModbusFunctionCodes.ReadFifoQueue);
        writer.WriteUInt16BE(pointerAddress);
        return writer.ToArray();
    }

    /// <summary>
    /// Build a Read Device Identification (FC 43 / MEI type 14) request.
    /// </summary>
    public static byte[] BuildReadDeviceIdentification(ushort transactionId, byte unitId,
        byte readDeviceIdCode, byte objectId)
    {
        using var writer = new PacketWriter(16);
        var pduLength = 4; // unit + fc + MEI type(1) + read code(1) + object ID(1)
        WriteMbapHeader(writer, transactionId, (ushort)pduLength);
        writer.WriteUInt8(unitId);
        writer.WriteUInt8(ModbusFunctionCodes.EncapsulatedInterfaceTransport);
        writer.WriteUInt8(0x0E); // MEI type: Read Device Identification
        writer.WriteUInt8(readDeviceIdCode);
        writer.WriteUInt8(objectId);
        return writer.ToArray();
    }

    /// <summary>
    /// Build a Read File Record (FC 20) request.
    /// </summary>
    public static byte[] BuildReadFileRecord(ushort transactionId, byte unitId,
        ushort fileNumber, ushort recordNumber, ushort recordLength)
    {
        using var writer = new PacketWriter(24);
        // Sub-request: ref type(1) + file#(2) + record#(2) + length(2) = 7 bytes
        var subRequestSize = 7;
        var pduLength = 2 + subRequestSize; // unit + fc + byteCount(1) + sub-request
        WriteMbapHeader(writer, transactionId, (ushort)pduLength);
        writer.WriteUInt8(unitId);
        writer.WriteUInt8(ModbusFunctionCodes.ReadFileRecord);
        writer.WriteUInt8((byte)subRequestSize);
        writer.WriteUInt8(0x06); // Reference type (always 0x06)
        writer.WriteUInt16BE(fileNumber);
        writer.WriteUInt16BE(recordNumber);
        writer.WriteUInt16BE(recordLength);
        return writer.ToArray();
    }

    /// <summary>
    /// Build a Write File Record (FC 21) request.
    /// </summary>
    public static byte[] BuildWriteFileRecord(ushort transactionId, byte unitId,
        ushort fileNumber, ushort recordNumber, ReadOnlySpan<byte> recordData)
    {
        var recordLength = recordData.Length / 2; // register count
        using var writer = new PacketWriter(24 + recordData.Length);
        // Sub-request: ref type(1) + file#(2) + record#(2) + length(2) + data = 7 + data
        var subRequestSize = 7 + recordData.Length;
        var pduLength = 2 + subRequestSize; // unit + fc + byteCount(1) + sub-request
        WriteMbapHeader(writer, transactionId, (ushort)pduLength);
        writer.WriteUInt8(unitId);
        writer.WriteUInt8(ModbusFunctionCodes.WriteFileRecord);
        writer.WriteUInt8((byte)subRequestSize);
        writer.WriteUInt8(0x06); // Reference type
        writer.WriteUInt16BE(fileNumber);
        writer.WriteUInt16BE(recordNumber);
        writer.WriteUInt16BE((ushort)recordLength);
        writer.WriteBytes(recordData);
        return writer.ToArray();
    }

    /// <summary>
    /// Build a raw Modbus request with an arbitrary function code and payload.
    /// </summary>
    public static byte[] BuildRaw(ushort transactionId, byte unitId,
        byte functionCode, ReadOnlySpan<byte> payload)
    {
        using var writer = new PacketWriter(16 + payload.Length);
        var pduLength = 1 + payload.Length; // unit + fc + payload
        WriteMbapHeader(writer, transactionId, (ushort)pduLength);
        writer.WriteUInt8(unitId);
        writer.WriteUInt8(functionCode);
        if (payload.Length > 0)
            writer.WriteBytes(payload);
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
