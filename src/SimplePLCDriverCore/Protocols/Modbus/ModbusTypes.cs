using System.Buffers.Binary;
using System.Text;
using SimplePLCDriverCore.Abstractions;

namespace SimplePLCDriverCore.Protocols.Modbus;

/// <summary>
/// Modbus data type encoding and decoding.
/// Modbus registers are 16-bit big-endian words.
/// </summary>
internal static class ModbusTypes
{
    /// <summary>
    /// Decode a value from Modbus response data based on the register type.
    /// </summary>
    public static PlcTagValue DecodeValue(ReadOnlySpan<byte> data, ModbusAddress address)
    {
        if (address.IsBitRegister)
        {
            if (data.Length < 2)
                throw new InvalidOperationException("Coil/discrete response data too short.");

            // FC 01/02 response: byte 0 = byte count, byte 1+ = coil data
            var byteCount = data[0];
            if (data.Length < 1 + byteCount || byteCount < 1)
                throw new InvalidOperationException("Coil response data truncated.");

            bool bitValue = (data[1] & 0x01) != 0;
            return PlcTagValue.FromBool(bitValue);
        }

        // Register response: byte 0 = byte count, byte 1+ = register data (big-endian)
        if (data.Length < 3)
            throw new InvalidOperationException("Register response data too short.");

        var regByteCount = data[0];
        var regData = data.Slice(1, regByteCount);

        if (regByteCount >= 2)
        {
            short value = BinaryPrimitives.ReadInt16BigEndian(regData);
            return PlcTagValue.FromInt(value);
        }

        return PlcTagValue.FromUSInt(regData[0]);
    }

    /// <summary>
    /// Decode multiple coil values from Modbus response data.
    /// </summary>
    public static bool[] DecodeCoils(ReadOnlySpan<byte> data, int count)
    {
        if (data.Length < 2)
            throw new InvalidOperationException("Coil response data too short.");

        var byteCount = data[0];
        var coilData = data.Slice(1, byteCount);
        var result = new bool[count];

        for (int i = 0; i < count; i++)
        {
            if (i / 8 < coilData.Length)
                result[i] = (coilData[i / 8] & (1 << (i % 8))) != 0;
        }

        return result;
    }

    /// <summary>
    /// Decode multiple register values from Modbus response data.
    /// </summary>
    public static short[] DecodeRegisters(ReadOnlySpan<byte> data, int count)
    {
        if (data.Length < 1)
            throw new InvalidOperationException("Register response data too short.");

        var byteCount = data[0];
        var regData = data.Slice(1, byteCount);
        var result = new short[count];

        for (int i = 0; i < count && i * 2 + 1 < regData.Length; i++)
            result[i] = BinaryPrimitives.ReadInt16BigEndian(regData.Slice(i * 2, 2));

        return result;
    }

    /// <summary>
    /// Decode a 32-bit integer from two consecutive holding registers (big-endian word order).
    /// </summary>
    public static PlcTagValue DecodeDWord(ReadOnlySpan<byte> data)
    {
        if (data.Length < 5) // 1 byte count + 4 bytes data
            throw new InvalidOperationException("DWord response data too short.");

        var regData = data.Slice(1, 4);
        return PlcTagValue.FromDInt(BinaryPrimitives.ReadInt32BigEndian(regData));
    }

    /// <summary>
    /// Decode a 32-bit float from two consecutive holding registers (big-endian word order).
    /// </summary>
    public static PlcTagValue DecodeFloat(ReadOnlySpan<byte> data)
    {
        if (data.Length < 5) // 1 byte count + 4 bytes data
            throw new InvalidOperationException("Float response data too short.");

        var regData = data.Slice(1, 4);
        return PlcTagValue.FromReal(BinaryPrimitives.ReadSingleBigEndian(regData));
    }

    /// <summary>
    /// Encode a value for Modbus write.
    /// Returns a single register value (16-bit).
    /// </summary>
    public static ushort EncodeRegister(object value)
    {
        return value switch
        {
            short s => (ushort)s,
            ushort u => u,
            int i => (ushort)i,
            byte b => b,
            bool bv => bv ? (ushort)0xFF00 : (ushort)0x0000,
            float f => (ushort)(short)f,
            _ => (ushort)Convert.ToInt16(value)
        };
    }

    /// <summary>
    /// Encode a 32-bit integer as two registers (big-endian word order).
    /// </summary>
    public static ushort[] EncodeDWord(int value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(bytes, value);
        return
        [
            BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(0, 2)),
            BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(2, 2)),
        ];
    }

    /// <summary>
    /// Encode a 32-bit float as two registers (big-endian word order).
    /// </summary>
    public static ushort[] EncodeFloat(float value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteSingleBigEndian(bytes, value);
        return
        [
            BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(0, 2)),
            BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(2, 2)),
        ];
    }

    // ===== Phase 5: Multi-Register Decode (with byte order) =====

    /// <summary>
    /// Decode a 32-bit float from 2 consecutive register bytes with configurable byte order.
    /// Input: raw register data (4 bytes, no byte-count prefix).
    /// </summary>
    public static float DecodeFloat32(ReadOnlySpan<byte> data, ModbusByteOrder order)
    {
        if (data.Length < 4)
            throw new InvalidOperationException("Float32 requires at least 4 bytes (2 registers).");
        Span<byte> buf = stackalloc byte[4];
        data[..4].CopyTo(buf);
        ModbusByteOrderHelper.Reorder4(buf, order);
        return BinaryPrimitives.ReadSingleBigEndian(buf);
    }

    /// <summary>
    /// Decode a 64-bit double from 4 consecutive register bytes with configurable byte order.
    /// Input: raw register data (8 bytes, no byte-count prefix).
    /// </summary>
    public static double DecodeFloat64(ReadOnlySpan<byte> data, ModbusByteOrder order)
    {
        if (data.Length < 8)
            throw new InvalidOperationException("Float64 requires at least 8 bytes (4 registers).");
        Span<byte> buf = stackalloc byte[8];
        data[..8].CopyTo(buf);
        ModbusByteOrderHelper.Reorder8(buf, order);
        return BinaryPrimitives.ReadDoubleBigEndian(buf);
    }

    /// <summary>
    /// Decode a 32-bit signed integer from 2 consecutive register bytes with configurable byte order.
    /// </summary>
    public static int DecodeInt32(ReadOnlySpan<byte> data, ModbusByteOrder order)
    {
        if (data.Length < 4)
            throw new InvalidOperationException("Int32 requires at least 4 bytes (2 registers).");
        Span<byte> buf = stackalloc byte[4];
        data[..4].CopyTo(buf);
        ModbusByteOrderHelper.Reorder4(buf, order);
        return BinaryPrimitives.ReadInt32BigEndian(buf);
    }

    /// <summary>
    /// Decode a 32-bit unsigned integer from 2 consecutive register bytes.
    /// </summary>
    public static uint DecodeUInt32(ReadOnlySpan<byte> data, ModbusByteOrder order)
    {
        if (data.Length < 4)
            throw new InvalidOperationException("UInt32 requires at least 4 bytes (2 registers).");
        Span<byte> buf = stackalloc byte[4];
        data[..4].CopyTo(buf);
        ModbusByteOrderHelper.Reorder4(buf, order);
        return BinaryPrimitives.ReadUInt32BigEndian(buf);
    }

    /// <summary>
    /// Decode a 64-bit signed integer from 4 consecutive register bytes.
    /// </summary>
    public static long DecodeInt64(ReadOnlySpan<byte> data, ModbusByteOrder order)
    {
        if (data.Length < 8)
            throw new InvalidOperationException("Int64 requires at least 8 bytes (4 registers).");
        Span<byte> buf = stackalloc byte[8];
        data[..8].CopyTo(buf);
        ModbusByteOrderHelper.Reorder8(buf, order);
        return BinaryPrimitives.ReadInt64BigEndian(buf);
    }

    /// <summary>
    /// Decode a 64-bit unsigned integer from 4 consecutive register bytes.
    /// </summary>
    public static ulong DecodeUInt64(ReadOnlySpan<byte> data, ModbusByteOrder order)
    {
        if (data.Length < 8)
            throw new InvalidOperationException("UInt64 requires at least 8 bytes (4 registers).");
        Span<byte> buf = stackalloc byte[8];
        data[..8].CopyTo(buf);
        ModbusByteOrderHelper.Reorder8(buf, order);
        return BinaryPrimitives.ReadUInt64BigEndian(buf);
    }

    /// <summary>
    /// Decode a string from register bytes (2 chars per register, ASCII).
    /// </summary>
    public static string DecodeString(ReadOnlySpan<byte> data, int registerCount)
    {
        var byteCount = registerCount * 2;
        if (data.Length < byteCount)
            byteCount = data.Length;
        var text = Encoding.ASCII.GetString(data[..byteCount]);
        // Trim null characters
        var nullIdx = text.IndexOf('\0');
        return nullIdx >= 0 ? text[..nullIdx] : text;
    }

    // ===== Phase 5: Multi-Register Encode (with byte order) =====

    /// <summary>
    /// Encode a 32-bit float as 2 register values with configurable byte order.
    /// </summary>
    public static ushort[] EncodeFloat32(float value, ModbusByteOrder order)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteSingleBigEndian(bytes, value);
        ModbusByteOrderHelper.ToWire4(bytes, order);
        return
        [
            BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(0, 2)),
            BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(2, 2)),
        ];
    }

    /// <summary>
    /// Encode a 64-bit double as 4 register values with configurable byte order.
    /// </summary>
    public static ushort[] EncodeFloat64(double value, ModbusByteOrder order)
    {
        var bytes = new byte[8];
        BinaryPrimitives.WriteDoubleBigEndian(bytes, value);
        ModbusByteOrderHelper.ToWire8(bytes, order);
        return
        [
            BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(0, 2)),
            BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(2, 2)),
            BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(4, 2)),
            BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(6, 2)),
        ];
    }

    /// <summary>
    /// Encode a 32-bit integer as 2 register values with configurable byte order.
    /// </summary>
    public static ushort[] EncodeInt32(int value, ModbusByteOrder order)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(bytes, value);
        ModbusByteOrderHelper.ToWire4(bytes, order);
        return
        [
            BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(0, 2)),
            BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(2, 2)),
        ];
    }

    /// <summary>
    /// Encode a 64-bit integer as 4 register values with configurable byte order.
    /// </summary>
    public static ushort[] EncodeInt64(long value, ModbusByteOrder order)
    {
        var bytes = new byte[8];
        BinaryPrimitives.WriteInt64BigEndian(bytes, value);
        ModbusByteOrderHelper.ToWire8(bytes, order);
        return
        [
            BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(0, 2)),
            BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(2, 2)),
            BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(4, 2)),
            BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(6, 2)),
        ];
    }

    /// <summary>
    /// Encode a string as register values (2 ASCII chars per register).
    /// </summary>
    public static ushort[] EncodeString(string value, int registerCount)
    {
        var bytes = new byte[registerCount * 2];
        var strBytes = Encoding.ASCII.GetBytes(value);
        Array.Copy(strBytes, bytes, Math.Min(strBytes.Length, bytes.Length));

        var regs = new ushort[registerCount];
        for (int i = 0; i < registerCount; i++)
            regs[i] = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(i * 2, 2));
        return regs;
    }

    /// <summary>
    /// Get a human-readable type name for a Modbus address.
    /// </summary>
    public static string GetTypeName(ModbusAddress address) => address.RegisterType switch
    {
        ModbusRegisterType.Coil => "COIL",
        ModbusRegisterType.DiscreteInput => "DISCRETE_INPUT",
        ModbusRegisterType.HoldingRegister => "HOLDING_REGISTER",
        ModbusRegisterType.InputRegister => "INPUT_REGISTER",
        _ => "UNKNOWN"
    };

    /// <summary>
    /// Get the PlcDataType for a Modbus address.
    /// </summary>
    public static PlcDataType ToPlcDataType(ModbusAddress address) =>
        address.IsBitRegister ? PlcDataType.Bool : PlcDataType.Int;
}
