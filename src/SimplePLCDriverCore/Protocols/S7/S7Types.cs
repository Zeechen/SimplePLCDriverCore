using System.Buffers.Binary;
using SimplePLCDriverCore.Abstractions;

namespace SimplePLCDriverCore.Protocols.S7;

/// <summary>
/// S7 data type encoding and decoding.
/// All S7 data is BIG-ENDIAN (unlike Allen-Bradley which is little-endian).
/// </summary>
internal static class S7Types
{
    /// <summary>
    /// Decode a value from S7 response data based on the address type.
    /// </summary>
    public static PlcTagValue DecodeValue(ReadOnlySpan<byte> data, S7Address address)
    {
        if (address.IsBitAddress)
        {
            if (data.Length < 1)
                throw new InvalidOperationException("Bit response data too short.");

            // Bit data: single byte with bit value in bit 0
            bool bitValue = (data[0] & 0x01) != 0;
            return PlcTagValue.FromBool(bitValue);
        }

        if (address.IsString)
            return DecodeString(data);

        if (address.Area == S7Area.Timer || address.Area == S7Area.Counter)
        {
            if (data.Length < 2)
                throw new InvalidOperationException("Timer/Counter response data too short.");
            short value = BinaryPrimitives.ReadInt16BigEndian(data);
            return PlcTagValue.FromInt(value);
        }

        return address.TransportSize switch
        {
            S7TransportSize.Byte => data.Length >= 1
                ? PlcTagValue.FromUSInt(data[0])
                : throw new InvalidOperationException("Byte response data too short."),

            S7TransportSize.Word or S7TransportSize.Int => data.Length >= 2
                ? PlcTagValue.FromInt(BinaryPrimitives.ReadInt16BigEndian(data))
                : throw new InvalidOperationException("Word response data too short."),

            S7TransportSize.DWord or S7TransportSize.DInt => data.Length >= 4
                ? PlcTagValue.FromDInt(BinaryPrimitives.ReadInt32BigEndian(data))
                : throw new InvalidOperationException("DWord response data too short."),

            S7TransportSize.Real => data.Length >= 4
                ? PlcTagValue.FromReal(BinaryPrimitives.ReadSingleBigEndian(data))
                : throw new InvalidOperationException("Real response data too short."),

            _ => throw new InvalidOperationException($"Unsupported transport size: {address.TransportSize}")
        };
    }

    /// <summary>
    /// Decode an S7 string. S7 strings have:
    ///   byte 0: max length
    ///   byte 1: actual length
    ///   byte 2..N: characters (ASCII/Latin-1)
    /// </summary>
    public static PlcTagValue DecodeString(ReadOnlySpan<byte> data)
    {
        if (data.Length < 2)
            return PlcTagValue.FromString(string.Empty);

        int actualLength = data[1];
        if (actualLength > data.Length - 2)
            actualLength = data.Length - 2;

        var text = System.Text.Encoding.ASCII.GetString(data.Slice(2, actualLength));
        return PlcTagValue.FromString(text);
    }

    /// <summary>
    /// Encode a .NET value to S7 bytes based on the address type.
    /// </summary>
    public static byte[] EncodeValue(object value, S7Address address)
    {
        if (address.IsBitAddress)
        {
            bool bv = Convert.ToBoolean(value);
            return [bv ? (byte)1 : (byte)0];
        }

        if (address.IsString)
            return EncodeString(value?.ToString() ?? string.Empty, address.StringMaxLength);

        if (address.Area == S7Area.Timer || address.Area == S7Area.Counter)
        {
            var result = new byte[2];
            BinaryPrimitives.WriteInt16BigEndian(result, Convert.ToInt16(value));
            return result;
        }

        return address.TransportSize switch
        {
            S7TransportSize.Byte => [Convert.ToByte(value)],

            S7TransportSize.Word or S7TransportSize.Int => EncodeInt16(value),

            S7TransportSize.DWord or S7TransportSize.DInt => EncodeInt32(value),

            S7TransportSize.Real => EncodeReal(value),

            _ => throw new InvalidOperationException($"Unsupported transport size for write: {address.TransportSize}")
        };
    }

    /// <summary>
    /// Encode an S7 string with max length header.
    /// </summary>
    public static byte[] EncodeString(string text, int maxLength)
    {
        if (maxLength <= 0) maxLength = 254;
        var bytes = System.Text.Encoding.ASCII.GetBytes(text);
        var actualLength = Math.Min(bytes.Length, maxLength);
        var result = new byte[2 + maxLength];
        result[0] = (byte)maxLength;
        result[1] = (byte)actualLength;
        Array.Copy(bytes, 0, result, 2, actualLength);
        return result;
    }

    /// <summary>
    /// Get the PlcDataType that corresponds to an S7 address.
    /// </summary>
    public static PlcDataType ToPlcDataType(S7Address address)
    {
        if (address.IsBitAddress)
            return PlcDataType.Bool;
        if (address.IsString)
            return PlcDataType.String;
        if (address.Area == S7Area.Timer || address.Area == S7Area.Counter)
            return PlcDataType.Int;

        return address.TransportSize switch
        {
            S7TransportSize.Bit => PlcDataType.Bool,
            S7TransportSize.Byte => PlcDataType.Usint,
            S7TransportSize.Word or S7TransportSize.Int => PlcDataType.Int,
            S7TransportSize.DWord or S7TransportSize.DInt => PlcDataType.Dint,
            S7TransportSize.Real => PlcDataType.Real,
            _ => PlcDataType.Unknown,
        };
    }

    /// <summary>
    /// Get a human-readable type name for an S7 address.
    /// </summary>
    public static string GetTypeName(S7Address address)
    {
        if (address.IsBitAddress)
            return "BOOL";
        if (address.IsString)
            return "STRING";
        if (address.Area == S7Area.Timer)
            return "TIMER";
        if (address.Area == S7Area.Counter)
            return "COUNTER";

        return address.TransportSize switch
        {
            S7TransportSize.Bit => "BOOL",
            S7TransportSize.Byte => "BYTE",
            S7TransportSize.Word => "WORD",
            S7TransportSize.Int => "INT",
            S7TransportSize.DWord => "DWORD",
            S7TransportSize.DInt => "DINT",
            S7TransportSize.Real => "REAL",
            _ => "UNKNOWN",
        };
    }

    private static byte[] EncodeInt16(object value)
    {
        var result = new byte[2];
        BinaryPrimitives.WriteInt16BigEndian(result, Convert.ToInt16(value));
        return result;
    }

    private static byte[] EncodeInt32(object value)
    {
        var result = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(result, Convert.ToInt32(value));
        return result;
    }

    private static byte[] EncodeReal(object value)
    {
        var result = new byte[4];
        BinaryPrimitives.WriteSingleBigEndian(result, Convert.ToSingle(value));
        return result;
    }
}
