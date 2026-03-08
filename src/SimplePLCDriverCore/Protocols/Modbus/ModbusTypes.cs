using System.Buffers.Binary;
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
