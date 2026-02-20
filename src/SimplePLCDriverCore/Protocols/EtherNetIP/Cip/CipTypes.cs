using System.Buffers.Binary;
using System.Text;
using SimplePLCDriverCore.Abstractions;
using SimplePLCDriverCore.Common.Buffers;

namespace SimplePLCDriverCore.Protocols.EtherNetIP.Cip;

/// <summary>
/// Codec for CIP data types - encodes .NET values to CIP byte representation
/// and decodes CIP bytes back to PlcTagValue.
///
/// This is the core of the "typeless" tag access: the driver knows each tag's
/// CIP type code from the tag database, so it can automatically encode/decode
/// values without the user specifying types.
///
/// CIP data is always little-endian.
/// </summary>
internal static class CipTypeCodec
{
    /// <summary>Logix STRING structure: 4-byte length prefix + 82 chars + 2 padding = 88 bytes.</summary>
    private const int LogixStringTotalSize = 88;
    private const int LogixStringMaxChars = 82;

    /// <summary>
    /// Decode a CIP atomic value from raw response bytes into a PlcTagValue.
    /// </summary>
    /// <param name="data">Raw bytes from CIP ReadTag response (after type code if present).</param>
    /// <param name="typeCode">CIP type code (e.g., 0xC4 for DINT).</param>
    /// <returns>Decoded PlcTagValue.</returns>
    public static PlcTagValue DecodeAtomicValue(ReadOnlySpan<byte> data, ushort typeCode)
    {
        return typeCode switch
        {
            CipDataTypes.Bool => PlcTagValue.FromBool(data[0] != 0),
            CipDataTypes.Sint => PlcTagValue.FromSInt((sbyte)data[0]),
            CipDataTypes.Int => PlcTagValue.FromInt(BinaryPrimitives.ReadInt16LittleEndian(data)),
            CipDataTypes.Dint => PlcTagValue.FromDInt(BinaryPrimitives.ReadInt32LittleEndian(data)),
            CipDataTypes.Lint => PlcTagValue.FromLInt(BinaryPrimitives.ReadInt64LittleEndian(data)),
            CipDataTypes.Usint => PlcTagValue.FromUSInt(data[0]),
            CipDataTypes.Uint => PlcTagValue.FromUInt(BinaryPrimitives.ReadUInt16LittleEndian(data)),
            CipDataTypes.Udint => PlcTagValue.FromUDInt(BinaryPrimitives.ReadUInt32LittleEndian(data)),
            CipDataTypes.Ulint => PlcTagValue.FromULInt(BinaryPrimitives.ReadUInt64LittleEndian(data)),
            CipDataTypes.Real => PlcTagValue.FromReal(BinaryPrimitives.ReadSingleLittleEndian(data)),
            CipDataTypes.Lreal => PlcTagValue.FromLReal(BinaryPrimitives.ReadDoubleLittleEndian(data)),
            _ => new PlcTagValue(data.ToArray(), PlcDataType.Unknown),
        };
    }

    /// <summary>
    /// Decode a Logix STRING from raw bytes.
    /// Logix STRING format: 4-byte DINT length prefix (LE) + up to 82 ASCII chars.
    /// </summary>
    public static PlcTagValue DecodeString(ReadOnlySpan<byte> data)
    {
        if (data.Length < 4)
            return PlcTagValue.FromString(string.Empty);

        var charCount = BinaryPrimitives.ReadInt32LittleEndian(data);
        if (charCount < 0 || charCount > LogixStringMaxChars)
            charCount = Math.Min(Math.Max(charCount, 0), data.Length - 4);

        var str = Encoding.ASCII.GetString(data.Slice(4, charCount));
        return PlcTagValue.FromString(str);
    }

    /// <summary>
    /// Decode an array of atomic values from CIP response bytes.
    /// </summary>
    /// <param name="data">Raw bytes containing the array data.</param>
    /// <param name="typeCode">CIP type code of the array element.</param>
    /// <param name="elementCount">Number of elements to decode.</param>
    /// <returns>PlcTagValue wrapping a list of element PlcTagValues.</returns>
    public static PlcTagValue DecodeArray(ReadOnlySpan<byte> data, ushort typeCode, int elementCount)
    {
        var elementSize = CipDataTypes.GetAtomicSize(typeCode);
        if (elementSize == 0)
            return new PlcTagValue(data.ToArray(), PlcDataType.Unknown);

        var elements = new PlcTagValue[elementCount];
        var offset = 0;

        for (var i = 0; i < elementCount && offset + elementSize <= data.Length; i++)
        {
            elements[i] = DecodeAtomicValue(data.Slice(offset, elementSize), typeCode);
            offset += elementSize;
        }

        return new PlcTagValue(elements, (PlcDataType)typeCode);
    }

    /// <summary>
    /// Decode a ReadTag response which includes the type code prefix.
    ///
    /// ReadTag response format:
    ///   Data Type (2 bytes LE) - CIP type code
    ///   Data (variable) - value bytes
    /// </summary>
    /// <param name="responseData">CIP response data (from CipResponse.Data).</param>
    /// <param name="elementCount">Expected number of elements (1 for scalar).</param>
    /// <returns>Tuple of (PlcTagValue, typeCode).</returns>
    public static (PlcTagValue Value, ushort TypeCode) DecodeReadResponse(
        ReadOnlySpan<byte> responseData, int elementCount = 1)
    {
        if (responseData.Length < 2)
            throw new InvalidDataException("ReadTag response too short");

        var typeCode = BinaryPrimitives.ReadUInt16LittleEndian(responseData);

        // CIP ReadTag response for structures uses abbreviated type 0x02A0
        // followed by a 2-byte structure handle (CRC) — 4 bytes total.
        if (typeCode == CipDataTypes.AbbreviatedStructureType)
        {
            if (responseData.Length < 4)
                throw new InvalidDataException("Structure ReadTag response too short");

            // Skip type (2 bytes) + structure handle (2 bytes)
            var structValueData = responseData[4..];
            var value = new PlcTagValue(structValueData.ToArray(), PlcDataType.Structure);
            return (value, typeCode);
        }

        var valueData = responseData[2..];

        PlcTagValue normalValue;
        if (typeCode == CipDataTypes.String)
        {
            normalValue = DecodeString(valueData);
        }
        else if (CipDataTypes.IsStructure(typeCode))
        {
            // Structure data with full type code (e.g., from test mocks)
            normalValue = new PlcTagValue(valueData.ToArray(), PlcDataType.Structure);
        }
        else if (elementCount > 1)
        {
            normalValue = DecodeArray(valueData, typeCode, elementCount);
        }
        else
        {
            normalValue = DecodeAtomicValue(valueData, typeCode);
        }

        return (normalValue, typeCode);
    }

    /// <summary>
    /// Encode a .NET value to CIP bytes for a WriteTag request.
    /// The value type is determined by the CIP type code from the tag database.
    /// </summary>
    /// <param name="value">The value to encode.</param>
    /// <param name="typeCode">CIP type code of the target tag.</param>
    /// <returns>Encoded bytes ready for CIP WriteTag.</returns>
    public static byte[] EncodeValue(object value, ushort typeCode)
    {
        return typeCode switch
        {
            CipDataTypes.Bool => [Convert.ToBoolean(value) ? (byte)1 : (byte)0],
            CipDataTypes.Sint => [(byte)(sbyte)Convert.ToSByte(value)],
            CipDataTypes.Int => EncodeInt16(Convert.ToInt16(value)),
            CipDataTypes.Dint => EncodeInt32(Convert.ToInt32(value)),
            CipDataTypes.Lint => EncodeInt64(Convert.ToInt64(value)),
            CipDataTypes.Usint => [Convert.ToByte(value)],
            CipDataTypes.Uint => EncodeUInt16(Convert.ToUInt16(value)),
            CipDataTypes.Udint => EncodeUInt32(Convert.ToUInt32(value)),
            CipDataTypes.Ulint => EncodeUInt64(Convert.ToUInt64(value)),
            CipDataTypes.Real => EncodeSingle(Convert.ToSingle(value)),
            CipDataTypes.Lreal => EncodeDouble(Convert.ToDouble(value)),
            CipDataTypes.String => EncodeString(value.ToString() ?? string.Empty),
            _ when value is byte[] raw => raw,
            _ => throw new ArgumentException(
                $"Cannot encode value of type {value.GetType().Name} for CIP type 0x{typeCode:X4}"),
        };
    }

    /// <summary>
    /// Encode an array of values to CIP bytes.
    /// </summary>
    public static byte[] EncodeArray(IReadOnlyList<object> values, ushort typeCode)
    {
        var elementSize = CipDataTypes.GetAtomicSize(typeCode);
        if (elementSize == 0)
            throw new ArgumentException($"Cannot determine element size for type 0x{typeCode:X4}");

        var result = new byte[values.Count * elementSize];
        for (var i = 0; i < values.Count; i++)
        {
            var encoded = EncodeValue(values[i], typeCode);
            encoded.CopyTo(result, i * elementSize);
        }
        return result;
    }

    /// <summary>
    /// Encode a Logix STRING to CIP bytes.
    /// Format: 4-byte DINT length + up to 82 ASCII chars + padding to 88 bytes total.
    /// </summary>
    public static byte[] EncodeString(string value)
    {
        var result = new byte[LogixStringTotalSize];
        var charCount = Math.Min(value.Length, LogixStringMaxChars);

        BinaryPrimitives.WriteInt32LittleEndian(result, charCount);
        Encoding.ASCII.GetBytes(value.AsSpan(0, charCount), result.AsSpan(4));

        return result;
    }

    /// <summary>
    /// Map a CIP type code to a PlcDataType enum.
    /// </summary>
    public static PlcDataType ToPlcDataType(ushort typeCode)
    {
        if (CipDataTypes.IsStructure(typeCode))
            return PlcDataType.Structure;

        return typeCode switch
        {
            CipDataTypes.Bool => PlcDataType.Bool,
            CipDataTypes.Sint => PlcDataType.Sint,
            CipDataTypes.Int => PlcDataType.Int,
            CipDataTypes.Dint => PlcDataType.Dint,
            CipDataTypes.Lint => PlcDataType.Lint,
            CipDataTypes.Usint => PlcDataType.Usint,
            CipDataTypes.Uint => PlcDataType.Uint,
            CipDataTypes.Udint => PlcDataType.Udint,
            CipDataTypes.Ulint => PlcDataType.Ulint,
            CipDataTypes.Real => PlcDataType.Real,
            CipDataTypes.Lreal => PlcDataType.Lreal,
            CipDataTypes.String => PlcDataType.String,
            _ => PlcDataType.Unknown,
        };
    }

    // --- Primitive Encoders ---

    private static byte[] EncodeInt16(short value)
    {
        var buf = new byte[2];
        BinaryPrimitives.WriteInt16LittleEndian(buf, value);
        return buf;
    }

    private static byte[] EncodeUInt16(ushort value)
    {
        var buf = new byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(buf, value);
        return buf;
    }

    private static byte[] EncodeInt32(int value)
    {
        var buf = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(buf, value);
        return buf;
    }

    private static byte[] EncodeUInt32(uint value)
    {
        var buf = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(buf, value);
        return buf;
    }

    private static byte[] EncodeInt64(long value)
    {
        var buf = new byte[8];
        BinaryPrimitives.WriteInt64LittleEndian(buf, value);
        return buf;
    }

    private static byte[] EncodeUInt64(ulong value)
    {
        var buf = new byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(buf, value);
        return buf;
    }

    private static byte[] EncodeSingle(float value)
    {
        var buf = new byte[4];
        BinaryPrimitives.WriteSingleLittleEndian(buf, value);
        return buf;
    }

    private static byte[] EncodeDouble(double value)
    {
        var buf = new byte[8];
        BinaryPrimitives.WriteDoubleLittleEndian(buf, value);
        return buf;
    }
}
