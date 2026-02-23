using System.Buffers.Binary;
using SimplePLCDriverCore.Abstractions;
using SimplePLCDriverCore.Protocols.EtherNetIP.Pccc;

namespace SimplePLCDriverCore.Tests.EtherNetIP.Pccc;

public class PcccTypesTests
{
    // ==========================================================================
    // Element Size
    // ==========================================================================

    [Fact]
    public void GetElementSize_Integer() => Assert.Equal(2, PcccTypes.GetElementSize(PcccFileType.Integer));

    [Fact]
    public void GetElementSize_Float() => Assert.Equal(4, PcccTypes.GetElementSize(PcccFileType.Float));

    [Fact]
    public void GetElementSize_Bit() => Assert.Equal(2, PcccTypes.GetElementSize(PcccFileType.Bit));

    [Fact]
    public void GetElementSize_Timer() => Assert.Equal(6, PcccTypes.GetElementSize(PcccFileType.Timer));

    [Fact]
    public void GetElementSize_Counter() => Assert.Equal(6, PcccTypes.GetElementSize(PcccFileType.Counter));

    [Fact]
    public void GetElementSize_Control() => Assert.Equal(6, PcccTypes.GetElementSize(PcccFileType.Control));

    [Fact]
    public void GetElementSize_Output() => Assert.Equal(2, PcccTypes.GetElementSize(PcccFileType.Output));

    [Fact]
    public void GetElementSize_Input() => Assert.Equal(2, PcccTypes.GetElementSize(PcccFileType.Input));

    [Fact]
    public void GetElementSize_Status() => Assert.Equal(2, PcccTypes.GetElementSize(PcccFileType.Status));

    [Fact]
    public void GetElementSize_String() => Assert.Equal(84, PcccTypes.GetElementSize(PcccFileType.String));

    [Fact]
    public void GetElementSize_Long() => Assert.Equal(4, PcccTypes.GetElementSize(PcccFileType.Long));

    [Fact]
    public void GetElementSize_Ascii() => Assert.Equal(2, PcccTypes.GetElementSize(PcccFileType.Ascii));

    // ==========================================================================
    // Type Name Mapping
    // ==========================================================================

    [Fact]
    public void GetTypeName_Integer() => Assert.Equal("INT", PcccTypes.GetTypeName(PcccFileType.Integer));

    [Fact]
    public void GetTypeName_Float() => Assert.Equal("FLOAT", PcccTypes.GetTypeName(PcccFileType.Float));

    [Fact]
    public void GetTypeName_Bit() => Assert.Equal("BIT", PcccTypes.GetTypeName(PcccFileType.Bit));

    [Fact]
    public void GetTypeName_Timer() => Assert.Equal("TIMER", PcccTypes.GetTypeName(PcccFileType.Timer));

    [Fact]
    public void GetTypeName_Counter() => Assert.Equal("COUNTER", PcccTypes.GetTypeName(PcccFileType.Counter));

    [Fact]
    public void GetTypeName_Control() => Assert.Equal("CONTROL", PcccTypes.GetTypeName(PcccFileType.Control));

    [Fact]
    public void GetTypeName_String() => Assert.Equal("STRING", PcccTypes.GetTypeName(PcccFileType.String));

    [Fact]
    public void GetTypeName_Long() => Assert.Equal("LONG", PcccTypes.GetTypeName(PcccFileType.Long));

    // ==========================================================================
    // PlcDataType Mapping
    // ==========================================================================

    [Fact]
    public void ToPlcDataType_Integer() => Assert.Equal(PlcDataType.Int, PcccTypes.ToPlcDataType(PcccFileType.Integer));

    [Fact]
    public void ToPlcDataType_Float() => Assert.Equal(PlcDataType.Real, PcccTypes.ToPlcDataType(PcccFileType.Float));

    [Fact]
    public void ToPlcDataType_Bit() => Assert.Equal(PlcDataType.Bool, PcccTypes.ToPlcDataType(PcccFileType.Bit));

    [Fact]
    public void ToPlcDataType_Long() => Assert.Equal(PlcDataType.Dint, PcccTypes.ToPlcDataType(PcccFileType.Long));

    [Fact]
    public void ToPlcDataType_String() => Assert.Equal(PlcDataType.String, PcccTypes.ToPlcDataType(PcccFileType.String));

    [Fact]
    public void ToPlcDataType_Timer() => Assert.Equal(PlcDataType.Structure, PcccTypes.ToPlcDataType(PcccFileType.Timer));

    [Fact]
    public void ToPlcDataType_Counter() => Assert.Equal(PlcDataType.Structure, PcccTypes.ToPlcDataType(PcccFileType.Counter));

    // ==========================================================================
    // Decode Integer Values
    // ==========================================================================

    [Fact]
    public void DecodeValue_Integer_Positive()
    {
        var data = new byte[2];
        BinaryPrimitives.WriteInt16LittleEndian(data, 42);
        var addr = PcccAddress.Parse("N7:0");

        var value = PcccTypes.DecodeValue(data, addr);

        Assert.Equal(PlcDataType.Int, value.DataType);
        Assert.Equal(42, value.AsInt32());
    }

    [Fact]
    public void DecodeValue_Integer_Negative()
    {
        var data = new byte[2];
        BinaryPrimitives.WriteInt16LittleEndian(data, -100);
        var addr = PcccAddress.Parse("N7:0");

        var value = PcccTypes.DecodeValue(data, addr);

        Assert.Equal(-100, value.AsInt32());
    }

    [Fact]
    public void DecodeValue_Integer_MaxValue()
    {
        var data = new byte[2];
        BinaryPrimitives.WriteInt16LittleEndian(data, short.MaxValue);
        var addr = PcccAddress.Parse("N7:0");

        var value = PcccTypes.DecodeValue(data, addr);

        Assert.Equal(short.MaxValue, value.AsInt16());
    }

    // ==========================================================================
    // Decode Float Values
    // ==========================================================================

    [Fact]
    public void DecodeValue_Float()
    {
        var data = new byte[4];
        BinaryPrimitives.WriteSingleLittleEndian(data, 3.14f);
        var addr = PcccAddress.Parse("F8:0");

        var value = PcccTypes.DecodeValue(data, addr);

        Assert.Equal(PlcDataType.Real, value.DataType);
        Assert.Equal(3.14f, value.AsSingle(), 0.001f);
    }

    [Fact]
    public void DecodeValue_Float_Zero()
    {
        var data = new byte[4];
        var addr = PcccAddress.Parse("F8:0");

        var value = PcccTypes.DecodeValue(data, addr);

        Assert.Equal(0.0f, value.AsSingle());
    }

    // ==========================================================================
    // Decode Long Values
    // ==========================================================================

    [Fact]
    public void DecodeValue_Long()
    {
        var data = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(data, 100000);
        var addr = PcccAddress.Parse("L10:0");

        var value = PcccTypes.DecodeValue(data, addr);

        Assert.Equal(PlcDataType.Dint, value.DataType);
        Assert.Equal(100000, value.AsInt32());
    }

    // ==========================================================================
    // Decode Bit Values
    // ==========================================================================

    [Fact]
    public void DecodeValue_Bit_Set()
    {
        var data = new byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(data, 0x0020); // bit 5 set
        var addr = PcccAddress.Parse("B3:0/5");

        var value = PcccTypes.DecodeValue(data, addr);

        Assert.Equal(PlcDataType.Bool, value.DataType);
        Assert.True(value.AsBoolean());
    }

    [Fact]
    public void DecodeValue_Bit_Clear()
    {
        var data = new byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(data, 0x0000); // bit 5 clear
        var addr = PcccAddress.Parse("B3:0/5");

        var value = PcccTypes.DecodeValue(data, addr);

        Assert.False(value.AsBoolean());
    }

    [Fact]
    public void DecodeValue_Bit_HighBit()
    {
        var data = new byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(data, 0x8000); // bit 15 set
        var addr = PcccAddress.Parse("B3:0/15");

        var value = PcccTypes.DecodeValue(data, addr);

        Assert.True(value.AsBoolean());
    }

    [Fact]
    public void DecodeValue_Bit_OtherBitsIgnored()
    {
        var data = new byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(data, 0xFFDF); // all bits set except bit 5
        var addr = PcccAddress.Parse("B3:0/5");

        var value = PcccTypes.DecodeValue(data, addr);

        Assert.False(value.AsBoolean());
    }

    // ==========================================================================
    // Decode Timer/Counter/Control
    // ==========================================================================

    [Fact]
    public void DecodeValue_Timer_Full()
    {
        // Timer: CTL(0x6000 = EN+TT), PRE(1000), ACC(500)
        var data = new byte[6];
        BinaryPrimitives.WriteInt16LittleEndian(data, unchecked((short)0xE000)); // EN, TT, DN
        BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(2), 1000);
        BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(4), 500);
        var addr = PcccAddress.Parse("T4:0");

        var value = PcccTypes.DecodeValue(data, addr);

        Assert.Equal(PlcDataType.Structure, value.DataType);
        var members = value.AsStructure();
        Assert.NotNull(members);
        Assert.Equal(1000, members!["PRE"].AsInt32());
        Assert.Equal(500, members["ACC"].AsInt32());
        Assert.True(members["EN"].AsBoolean());
        Assert.True(members["TT"].AsBoolean());
        Assert.True(members["DN"].AsBoolean());
    }

    [Fact]
    public void DecodeValue_Timer_SubElement_ACC()
    {
        var data = new byte[2];
        BinaryPrimitives.WriteInt16LittleEndian(data, 750);
        var addr = PcccAddress.Parse("T4:0.ACC");

        var value = PcccTypes.DecodeValue(data, addr);

        Assert.Equal(PlcDataType.Int, value.DataType);
        Assert.Equal(750, value.AsInt32());
    }

    [Fact]
    public void DecodeValue_Counter_Full()
    {
        var data = new byte[6];
        BinaryPrimitives.WriteInt16LittleEndian(data, unchecked((short)0xA000)); // CU + DN
        BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(2), 500);
        BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(4), 123);
        var addr = PcccAddress.Parse("C5:0");

        var value = PcccTypes.DecodeValue(data, addr);

        var members = value.AsStructure()!;
        Assert.Equal(500, members["PRE"].AsInt32());
        Assert.Equal(123, members["ACC"].AsInt32());
        Assert.True(members["CU"].AsBoolean());
        Assert.True(members["DN"].AsBoolean());
        Assert.False(members["CD"].AsBoolean());
    }

    [Fact]
    public void DecodeValue_Control_Full()
    {
        var data = new byte[6];
        BinaryPrimitives.WriteInt16LittleEndian(data, unchecked((short)0xE000)); // EN + EU + DN
        BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(2), 10); // LEN
        BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(4), 3);  // POS
        var addr = PcccAddress.Parse("R6:0");

        var value = PcccTypes.DecodeValue(data, addr);

        var members = value.AsStructure()!;
        Assert.Equal(10, members["LEN"].AsInt32());
        Assert.Equal(3, members["POS"].AsInt32());
        Assert.True(members["EN"].AsBoolean());
    }

    // ==========================================================================
    // Decode String Values
    // ==========================================================================

    [Fact]
    public void DecodeValue_String()
    {
        var data = new byte[84];
        BinaryPrimitives.WriteInt16LittleEndian(data, 5); // length = 5
        data[2] = (byte)'H';
        data[3] = (byte)'e';
        data[4] = (byte)'l';
        data[5] = (byte)'l';
        data[6] = (byte)'o';
        var addr = PcccAddress.Parse("ST9:0");

        var value = PcccTypes.DecodeValue(data, addr);

        Assert.Equal(PlcDataType.String, value.DataType);
        Assert.Equal("Hello", value.AsString());
    }

    [Fact]
    public void DecodeValue_String_Empty()
    {
        var data = new byte[84]; // length = 0
        var addr = PcccAddress.Parse("ST9:0");

        var value = PcccTypes.DecodeValue(data, addr);

        Assert.Equal("", value.AsString());
    }

    // ==========================================================================
    // Encode Values
    // ==========================================================================

    [Fact]
    public void EncodeValue_Integer()
    {
        var addr = PcccAddress.Parse("N7:0");
        var encoded = PcccTypes.EncodeValue(42, addr);

        Assert.Equal(2, encoded.Length);
        Assert.Equal(42, BinaryPrimitives.ReadInt16LittleEndian(encoded));
    }

    [Fact]
    public void EncodeValue_Float()
    {
        var addr = PcccAddress.Parse("F8:0");
        var encoded = PcccTypes.EncodeValue(3.14f, addr);

        Assert.Equal(4, encoded.Length);
        Assert.Equal(3.14f, BinaryPrimitives.ReadSingleLittleEndian(encoded), 0.001f);
    }

    [Fact]
    public void EncodeValue_Long()
    {
        var addr = PcccAddress.Parse("L10:0");
        var encoded = PcccTypes.EncodeValue(100000, addr);

        Assert.Equal(4, encoded.Length);
        Assert.Equal(100000, BinaryPrimitives.ReadInt32LittleEndian(encoded));
    }

    [Fact]
    public void EncodeValue_String()
    {
        var addr = PcccAddress.Parse("ST9:0");
        var encoded = PcccTypes.EncodeValue("Hello", addr);

        Assert.Equal(84, encoded.Length);
        Assert.Equal(5, BinaryPrimitives.ReadInt16LittleEndian(encoded));
        Assert.Equal((byte)'H', encoded[2]);
    }

    [Fact]
    public void EncodeValue_SubElement_Integer()
    {
        var addr = PcccAddress.Parse("T4:0.ACC");
        var encoded = PcccTypes.EncodeValue((short)750, addr);

        Assert.Equal(2, encoded.Length);
        Assert.Equal(750, BinaryPrimitives.ReadInt16LittleEndian(encoded));
    }

    [Fact]
    public void EncodeValue_Bit()
    {
        var addr = PcccAddress.Parse("B3:0/5");
        var encoded = PcccTypes.EncodeValue(true, addr);

        Assert.Equal(2, encoded.Length);
        var word = BinaryPrimitives.ReadUInt16LittleEndian(encoded);
        Assert.Equal(0x0020, word); // bit 5 set
    }

    // ==========================================================================
    // Bit Mask
    // ==========================================================================

    [Theory]
    [InlineData(0, 0x0001)]
    [InlineData(1, 0x0002)]
    [InlineData(5, 0x0020)]
    [InlineData(15, 0x8000)]
    public void GetBitMask_ReturnsCorrectMask(int bitNumber, int expectedMask)
    {
        Assert.Equal((ushort)expectedMask, PcccTypes.GetBitMask(bitNumber));
    }

    // ==========================================================================
    // Status Messages
    // ==========================================================================

    [Theory]
    [InlineData(0x00, "Success")]
    [InlineData(0x50, "Address problem")]
    [InlineData(0x10, "Illegal command")]
    public void GetStatusMessage_ReturnsExpected(byte status, string expectedContains)
    {
        var message = PcccTypes.GetStatusMessage(status);
        Assert.Contains(expectedContains, message, StringComparison.OrdinalIgnoreCase);
    }

    // ==========================================================================
    // Empty Data
    // ==========================================================================

    [Fact]
    public void DecodeValue_EmptyData_ReturnsNull()
    {
        var addr = PcccAddress.Parse("N7:0");
        var value = PcccTypes.DecodeValue(ReadOnlySpan<byte>.Empty, addr);
        Assert.True(value.IsNull);
    }
}
