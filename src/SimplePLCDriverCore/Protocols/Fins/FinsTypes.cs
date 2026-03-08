using System.Buffers.Binary;
using SimplePLCDriverCore.Abstractions;

namespace SimplePLCDriverCore.Protocols.Fins;

/// <summary>
/// FINS data type encoding and decoding.
/// FINS uses BIG-ENDIAN byte order for protocol fields,
/// but word data is stored as 16-bit big-endian words.
/// </summary>
internal static class FinsTypes
{
    /// <summary>
    /// Decode a word value from FINS response data.
    /// FINS reads return 16-bit big-endian words.
    /// </summary>
    public static PlcTagValue DecodeWord(ReadOnlySpan<byte> data, FinsAddress address)
    {
        if (address.IsBitAddress)
        {
            if (data.Length < 1)
                throw new InvalidOperationException("Bit response data too short.");
            bool bitValue = data[0] != 0;
            return PlcTagValue.FromBool(bitValue);
        }

        if (data.Length < 2)
            throw new InvalidOperationException("Word response data too short.");

        var word = BinaryPrimitives.ReadUInt16BigEndian(data);
        return PlcTagValue.FromInt((short)word);
    }

    /// <summary>
    /// Decode a 32-bit (double word) value from FINS response data (2 consecutive words).
    /// </summary>
    public static PlcTagValue DecodeDWord(ReadOnlySpan<byte> data)
    {
        if (data.Length < 4)
            throw new InvalidOperationException("DWord response data too short.");

        return PlcTagValue.FromDInt(BinaryPrimitives.ReadInt32BigEndian(data));
    }

    /// <summary>
    /// Decode a 32-bit floating point from FINS response data (2 consecutive words).
    /// </summary>
    public static PlcTagValue DecodeFloat(ReadOnlySpan<byte> data)
    {
        if (data.Length < 4)
            throw new InvalidOperationException("Float response data too short.");

        return PlcTagValue.FromReal(BinaryPrimitives.ReadSingleBigEndian(data));
    }

    /// <summary>
    /// Encode a word (16-bit) value for FINS write.
    /// </summary>
    public static byte[] EncodeWord(object value, FinsAddress address)
    {
        if (address.IsBitAddress)
        {
            bool bv = Convert.ToBoolean(value);
            return bv ? [(byte)0x01] : [(byte)0x00];
        }

        var result = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(result, (ushort)Convert.ToInt16(value));
        return result;
    }

    /// <summary>
    /// Encode a 32-bit integer for FINS write (2 words).
    /// </summary>
    public static byte[] EncodeDWord(object value)
    {
        var result = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(result, Convert.ToInt32(value));
        return result;
    }

    /// <summary>
    /// Encode a 32-bit float for FINS write (2 words).
    /// </summary>
    public static byte[] EncodeFloat(object value)
    {
        var result = new byte[4];
        BinaryPrimitives.WriteSingleBigEndian(result, Convert.ToSingle(value));
        return result;
    }

    /// <summary>
    /// Get a human-readable type name for a FINS address.
    /// </summary>
    public static string GetTypeName(FinsAddress address)
    {
        if (address.IsBitAddress)
            return "BIT";

        return address.Area switch
        {
            FinsArea.TimerCounterPv => "WORD",
            FinsArea.TimerCounterStatus => "BIT",
            _ => "WORD"
        };
    }

    /// <summary>
    /// Get the PlcDataType for a FINS address.
    /// </summary>
    public static PlcDataType ToPlcDataType(FinsAddress address)
    {
        if (address.IsBitAddress)
            return PlcDataType.Bool;

        return PlcDataType.Int; // FINS words map to 16-bit int by default
    }
}
