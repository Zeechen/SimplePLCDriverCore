using System.Buffers.Binary;
using SimplePLCDriverCore.Abstractions;
using SimplePLCDriverCore.Protocols.EtherNetIP.Cip;

namespace SimplePLCDriverCore.Tests.EtherNetIP;

public class CipTypeCodecTests
{
    // --- Decode Atomic Values ---

    [Fact]
    public void DecodeAtomicValue_Bool_True()
    {
        var data = new byte[] { 0x01 };
        var result = CipTypeCodec.DecodeAtomicValue(data, CipDataTypes.Bool);
        Assert.True(result.AsBoolean());
        Assert.Equal(PlcDataType.Bool, result.DataType);
    }

    [Fact]
    public void DecodeAtomicValue_Bool_False()
    {
        var data = new byte[] { 0x00 };
        var result = CipTypeCodec.DecodeAtomicValue(data, CipDataTypes.Bool);
        Assert.False(result.AsBoolean());
    }

    [Fact]
    public void DecodeAtomicValue_Sint()
    {
        var data = new byte[] { 0xD6 }; // -42
        var result = CipTypeCodec.DecodeAtomicValue(data, CipDataTypes.Sint);
        Assert.Equal((sbyte)-42, result.AsSByte());
    }

    [Fact]
    public void DecodeAtomicValue_Int()
    {
        var data = new byte[2];
        BinaryPrimitives.WriteInt16LittleEndian(data, -1000);
        var result = CipTypeCodec.DecodeAtomicValue(data, CipDataTypes.Int);
        Assert.Equal((short)-1000, result.AsInt16());
    }

    [Fact]
    public void DecodeAtomicValue_Dint()
    {
        var data = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(data, 123456);
        var result = CipTypeCodec.DecodeAtomicValue(data, CipDataTypes.Dint);
        Assert.Equal(123456, result.AsInt32());
    }

    [Fact]
    public void DecodeAtomicValue_Lint()
    {
        var data = new byte[8];
        BinaryPrimitives.WriteInt64LittleEndian(data, 9876543210L);
        var result = CipTypeCodec.DecodeAtomicValue(data, CipDataTypes.Lint);
        Assert.Equal(9876543210L, result.AsInt64());
    }

    [Fact]
    public void DecodeAtomicValue_Usint()
    {
        var data = new byte[] { 0xFF };
        var result = CipTypeCodec.DecodeAtomicValue(data, CipDataTypes.Usint);
        Assert.Equal((byte)255, result.AsByte());
    }

    [Fact]
    public void DecodeAtomicValue_Uint()
    {
        var data = new byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(data, 50000);
        var result = CipTypeCodec.DecodeAtomicValue(data, CipDataTypes.Uint);
        Assert.Equal((ushort)50000, result.AsUInt16());
    }

    [Fact]
    public void DecodeAtomicValue_Udint()
    {
        var data = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(data, 3000000000U);
        var result = CipTypeCodec.DecodeAtomicValue(data, CipDataTypes.Udint);
        Assert.Equal(3000000000U, result.AsUInt32());
    }

    [Fact]
    public void DecodeAtomicValue_Ulint()
    {
        var data = new byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(data, 18000000000000000000UL);
        var result = CipTypeCodec.DecodeAtomicValue(data, CipDataTypes.Ulint);
        Assert.Equal(18000000000000000000UL, result.AsUInt64());
    }

    [Fact]
    public void DecodeAtomicValue_Real()
    {
        var data = new byte[4];
        BinaryPrimitives.WriteSingleLittleEndian(data, 3.14f);
        var result = CipTypeCodec.DecodeAtomicValue(data, CipDataTypes.Real);
        Assert.Equal(3.14f, result.AsSingle());
    }

    [Fact]
    public void DecodeAtomicValue_Lreal()
    {
        var data = new byte[8];
        BinaryPrimitives.WriteDoubleLittleEndian(data, 3.14159265358979);
        var result = CipTypeCodec.DecodeAtomicValue(data, CipDataTypes.Lreal);
        Assert.Equal(3.14159265358979, result.AsDouble());
    }

    // --- Decode String ---

    [Fact]
    public void DecodeString_NormalString()
    {
        // Logix STRING: 4-byte length + ASCII chars
        var data = new byte[88];
        BinaryPrimitives.WriteInt32LittleEndian(data, 5); // 5 chars
        data[4] = (byte)'H';
        data[5] = (byte)'e';
        data[6] = (byte)'l';
        data[7] = (byte)'l';
        data[8] = (byte)'o';

        var result = CipTypeCodec.DecodeString(data);
        Assert.Equal("Hello", result.AsString());
    }

    [Fact]
    public void DecodeString_EmptyString()
    {
        var data = new byte[88];
        BinaryPrimitives.WriteInt32LittleEndian(data, 0);

        var result = CipTypeCodec.DecodeString(data);
        Assert.Equal("", result.AsString());
    }

    [Fact]
    public void DecodeString_TooShort()
    {
        var data = new byte[2]; // less than 4 bytes
        var result = CipTypeCodec.DecodeString(data);
        Assert.Equal("", result.AsString());
    }

    // --- Decode Array ---

    [Fact]
    public void DecodeArray_DintArray()
    {
        var data = new byte[12]; // 3 DINTs
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0), 10);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(4), 20);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(8), 30);

        var result = CipTypeCodec.DecodeArray(data, CipDataTypes.Dint, 3);
        var arr = result.AsArray();

        Assert.NotNull(arr);
        Assert.Equal(3, arr!.Count);
        Assert.Equal(10, arr[0].AsInt32());
        Assert.Equal(20, arr[1].AsInt32());
        Assert.Equal(30, arr[2].AsInt32());
    }

    [Fact]
    public void DecodeArray_RealArray()
    {
        var data = new byte[8]; // 2 REALs
        BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(0), 1.5f);
        BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(4), 2.5f);

        var result = CipTypeCodec.DecodeArray(data, CipDataTypes.Real, 2);
        var arr = result.AsArray();

        Assert.NotNull(arr);
        Assert.Equal(2, arr!.Count);
        Assert.Equal(1.5f, arr[0].AsSingle());
        Assert.Equal(2.5f, arr[1].AsSingle());
    }

    // --- Decode Read Response ---

    [Fact]
    public void DecodeReadResponse_DintScalar()
    {
        // ReadTag response: type code (2 bytes) + value data
        var data = new byte[6];
        BinaryPrimitives.WriteUInt16LittleEndian(data, CipDataTypes.Dint);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(2), 42);

        var (value, typeCode) = CipTypeCodec.DecodeReadResponse(data);
        Assert.Equal(CipDataTypes.Dint, typeCode);
        Assert.Equal(42, value.AsInt32());
    }

    [Fact]
    public void DecodeReadResponse_RealScalar()
    {
        var data = new byte[6];
        BinaryPrimitives.WriteUInt16LittleEndian(data, CipDataTypes.Real);
        BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(2), 3.14f);

        var (value, typeCode) = CipTypeCodec.DecodeReadResponse(data);
        Assert.Equal(CipDataTypes.Real, typeCode);
        Assert.Equal(3.14f, value.AsSingle());
    }

    [Fact]
    public void DecodeReadResponse_BoolScalar()
    {
        var data = new byte[3];
        BinaryPrimitives.WriteUInt16LittleEndian(data, CipDataTypes.Bool);
        data[2] = 1;

        var (value, _) = CipTypeCodec.DecodeReadResponse(data);
        Assert.True(value.AsBoolean());
    }

    [Fact]
    public void DecodeReadResponse_Array()
    {
        // 3 INTs
        var data = new byte[2 + 6];
        BinaryPrimitives.WriteUInt16LittleEndian(data, CipDataTypes.Int);
        BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(2), 100);
        BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(4), 200);
        BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(6), 300);

        var (value, _) = CipTypeCodec.DecodeReadResponse(data, elementCount: 3);
        var arr = value.AsArray();

        Assert.NotNull(arr);
        Assert.Equal(3, arr!.Count);
        Assert.Equal((short)100, arr[0].AsInt16());
        Assert.Equal((short)200, arr[1].AsInt16());
        Assert.Equal((short)300, arr[2].AsInt16());
    }

    [Fact]
    public void DecodeReadResponse_ThrowsOnTooShort()
    {
        var data = new byte[1]; // too short
        Assert.Throws<InvalidDataException>(() =>
            CipTypeCodec.DecodeReadResponse(data));
    }

    // --- Encode Values ---

    [Fact]
    public void EncodeValue_Bool()
    {
        Assert.Equal(new byte[] { 1 }, CipTypeCodec.EncodeValue(true, CipDataTypes.Bool));
        Assert.Equal(new byte[] { 0 }, CipTypeCodec.EncodeValue(false, CipDataTypes.Bool));
    }

    [Fact]
    public void EncodeValue_Sint()
    {
        var encoded = CipTypeCodec.EncodeValue((sbyte)-42, CipDataTypes.Sint);
        Assert.Single(encoded);
        Assert.Equal(unchecked((byte)-42), encoded[0]);
    }

    [Fact]
    public void EncodeValue_Dint()
    {
        var encoded = CipTypeCodec.EncodeValue(42, CipDataTypes.Dint);
        Assert.Equal(4, encoded.Length);
        Assert.Equal(42, BinaryPrimitives.ReadInt32LittleEndian(encoded));
    }

    [Fact]
    public void EncodeValue_Dint_FromLong()
    {
        // Should convert long -> int
        var encoded = CipTypeCodec.EncodeValue(42L, CipDataTypes.Dint);
        Assert.Equal(42, BinaryPrimitives.ReadInt32LittleEndian(encoded));
    }

    [Fact]
    public void EncodeValue_Real()
    {
        var encoded = CipTypeCodec.EncodeValue(3.14f, CipDataTypes.Real);
        Assert.Equal(4, encoded.Length);
        Assert.Equal(3.14f, BinaryPrimitives.ReadSingleLittleEndian(encoded));
    }

    [Fact]
    public void EncodeValue_Lreal()
    {
        var encoded = CipTypeCodec.EncodeValue(3.14159, CipDataTypes.Lreal);
        Assert.Equal(8, encoded.Length);
        Assert.Equal(3.14159, BinaryPrimitives.ReadDoubleLittleEndian(encoded));
    }

    [Fact]
    public void EncodeValue_Real_FromDouble()
    {
        // Double should be narrowed to float for REAL tags
        var encoded = CipTypeCodec.EncodeValue(3.14, CipDataTypes.Real);
        Assert.Equal(4, encoded.Length);
        Assert.Equal(3.14f, BinaryPrimitives.ReadSingleLittleEndian(encoded), 2);
    }

    [Fact]
    public void EncodeValue_String()
    {
        var encoded = CipTypeCodec.EncodeValue("Hello", CipDataTypes.String);
        Assert.Equal(88, encoded.Length); // Logix STRING is 88 bytes

        var charCount = BinaryPrimitives.ReadInt32LittleEndian(encoded);
        Assert.Equal(5, charCount);
        Assert.Equal((byte)'H', encoded[4]);
        Assert.Equal((byte)'o', encoded[8]);
    }

    [Fact]
    public void EncodeString_TruncatesLongString()
    {
        var longStr = new string('A', 100); // longer than 82 max
        var encoded = CipTypeCodec.EncodeString(longStr);

        var charCount = BinaryPrimitives.ReadInt32LittleEndian(encoded);
        Assert.Equal(82, charCount); // truncated to max
    }

    // --- Encode Array ---

    [Fact]
    public void EncodeArray_DintArray()
    {
        var values = new object[] { 10, 20, 30 };
        var encoded = CipTypeCodec.EncodeArray(values, CipDataTypes.Dint);

        Assert.Equal(12, encoded.Length);
        Assert.Equal(10, BinaryPrimitives.ReadInt32LittleEndian(encoded.AsSpan(0)));
        Assert.Equal(20, BinaryPrimitives.ReadInt32LittleEndian(encoded.AsSpan(4)));
        Assert.Equal(30, BinaryPrimitives.ReadInt32LittleEndian(encoded.AsSpan(8)));
    }

    // --- Round-trip ---

    [Theory]
    [InlineData(CipDataTypes.Bool, true)]
    [InlineData(CipDataTypes.Bool, false)]
    public void RoundTrip_Bool(ushort typeCode, bool value)
    {
        var encoded = CipTypeCodec.EncodeValue(value, typeCode);
        var decoded = CipTypeCodec.DecodeAtomicValue(encoded, typeCode);
        Assert.Equal(value, decoded.AsBoolean());
    }

    [Fact]
    public void RoundTrip_Dint()
    {
        var encoded = CipTypeCodec.EncodeValue(-999999, CipDataTypes.Dint);
        var decoded = CipTypeCodec.DecodeAtomicValue(encoded, CipDataTypes.Dint);
        Assert.Equal(-999999, decoded.AsInt32());
    }

    [Fact]
    public void RoundTrip_Real()
    {
        var encoded = CipTypeCodec.EncodeValue(2.718f, CipDataTypes.Real);
        var decoded = CipTypeCodec.DecodeAtomicValue(encoded, CipDataTypes.Real);
        Assert.Equal(2.718f, decoded.AsSingle());
    }

    [Fact]
    public void RoundTrip_Lreal()
    {
        var encoded = CipTypeCodec.EncodeValue(2.718281828, CipDataTypes.Lreal);
        var decoded = CipTypeCodec.DecodeAtomicValue(encoded, CipDataTypes.Lreal);
        Assert.Equal(2.718281828, decoded.AsDouble());
    }

    [Fact]
    public void RoundTrip_String()
    {
        var encoded = CipTypeCodec.EncodeString("Hello PLC");
        var decoded = CipTypeCodec.DecodeString(encoded);
        Assert.Equal("Hello PLC", decoded.AsString());
    }

    [Fact]
    public void RoundTrip_AllIntegerTypes()
    {
        // SINT
        var enc = CipTypeCodec.EncodeValue((sbyte)-100, CipDataTypes.Sint);
        Assert.Equal((sbyte)-100, CipTypeCodec.DecodeAtomicValue(enc, CipDataTypes.Sint).AsSByte());

        // INT
        enc = CipTypeCodec.EncodeValue((short)-30000, CipDataTypes.Int);
        Assert.Equal((short)-30000, CipTypeCodec.DecodeAtomicValue(enc, CipDataTypes.Int).AsInt16());

        // LINT
        enc = CipTypeCodec.EncodeValue(long.MaxValue, CipDataTypes.Lint);
        Assert.Equal(long.MaxValue, CipTypeCodec.DecodeAtomicValue(enc, CipDataTypes.Lint).AsInt64());

        // USINT
        enc = CipTypeCodec.EncodeValue((byte)255, CipDataTypes.Usint);
        Assert.Equal((byte)255, CipTypeCodec.DecodeAtomicValue(enc, CipDataTypes.Usint).AsByte());

        // UINT
        enc = CipTypeCodec.EncodeValue((ushort)65535, CipDataTypes.Uint);
        Assert.Equal((ushort)65535, CipTypeCodec.DecodeAtomicValue(enc, CipDataTypes.Uint).AsUInt16());

        // UDINT
        enc = CipTypeCodec.EncodeValue(uint.MaxValue, CipDataTypes.Udint);
        Assert.Equal(uint.MaxValue, CipTypeCodec.DecodeAtomicValue(enc, CipDataTypes.Udint).AsUInt32());

        // ULINT
        enc = CipTypeCodec.EncodeValue(ulong.MaxValue, CipDataTypes.Ulint);
        Assert.Equal(ulong.MaxValue, CipTypeCodec.DecodeAtomicValue(enc, CipDataTypes.Ulint).AsUInt64());
    }

    // --- ToPlcDataType ---

    [Theory]
    [InlineData(CipDataTypes.Bool, PlcDataType.Bool)]
    [InlineData(CipDataTypes.Sint, PlcDataType.Sint)]
    [InlineData(CipDataTypes.Int, PlcDataType.Int)]
    [InlineData(CipDataTypes.Dint, PlcDataType.Dint)]
    [InlineData(CipDataTypes.Lint, PlcDataType.Lint)]
    [InlineData(CipDataTypes.Usint, PlcDataType.Usint)]
    [InlineData(CipDataTypes.Uint, PlcDataType.Uint)]
    [InlineData(CipDataTypes.Udint, PlcDataType.Udint)]
    [InlineData(CipDataTypes.Ulint, PlcDataType.Ulint)]
    [InlineData(CipDataTypes.Real, PlcDataType.Real)]
    [InlineData(CipDataTypes.Lreal, PlcDataType.Lreal)]
    [InlineData(CipDataTypes.String, PlcDataType.String)]
    public void ToPlcDataType_MapsCorrectly(ushort cipType, PlcDataType expected)
    {
        Assert.Equal(expected, CipTypeCodec.ToPlcDataType(cipType));
    }

    [Fact]
    public void ToPlcDataType_StructureType()
    {
        Assert.Equal(PlcDataType.Structure, CipTypeCodec.ToPlcDataType(0x8FCE));
    }

    [Fact]
    public void ToPlcDataType_Unknown()
    {
        Assert.Equal(PlcDataType.Unknown, CipTypeCodec.ToPlcDataType(0x0001));
    }

    // --- IsLikelyStringData ---

    [Fact]
    public void IsLikelyStringData_StandardString_ReturnsTrue()
    {
        // Standard STRING: 88 bytes, LEN=5
        var data = new byte[88];
        BinaryPrimitives.WriteInt32LittleEndian(data, 5);
        data[4] = (byte)'H';
        data[5] = (byte)'e';
        data[6] = (byte)'l';
        data[7] = (byte)'l';
        data[8] = (byte)'o';

        Assert.True(CipDataTypes.IsLikelyStringData(data));
    }

    [Fact]
    public void IsLikelyStringData_EmptyString_ReturnsTrue()
    {
        // Standard STRING: 88 bytes, LEN=0
        var data = new byte[88];
        Assert.True(CipDataTypes.IsLikelyStringData(data));
    }

    [Fact]
    public void IsLikelyStringData_MaxLength_ReturnsTrue()
    {
        // Standard STRING: 88 bytes, LEN=82 (max)
        var data = new byte[88];
        BinaryPrimitives.WriteInt32LittleEndian(data, 82);
        Assert.True(CipDataTypes.IsLikelyStringData(data));
    }

    [Fact]
    public void IsLikelyStringData_WrongSize_ReturnsFalse()
    {
        // Not 88 bytes
        var data = new byte[100];
        BinaryPrimitives.WriteInt32LittleEndian(data, 5);
        Assert.False(CipDataTypes.IsLikelyStringData(data));
    }

    [Fact]
    public void IsLikelyStringData_NegativeLen_ReturnsFalse()
    {
        var data = new byte[88];
        BinaryPrimitives.WriteInt32LittleEndian(data, -1);
        Assert.False(CipDataTypes.IsLikelyStringData(data));
    }

    [Fact]
    public void IsLikelyStringData_LenTooLarge_ReturnsFalse()
    {
        var data = new byte[88];
        BinaryPrimitives.WriteInt32LittleEndian(data, 83); // > 82
        Assert.False(CipDataTypes.IsLikelyStringData(data));
    }

    [Fact]
    public void IsLikelyStringData_TooShort_ReturnsFalse()
    {
        var data = new byte[3]; // Less than 4 bytes needed for LEN
        Assert.False(CipDataTypes.IsLikelyStringData(data));
    }
}
