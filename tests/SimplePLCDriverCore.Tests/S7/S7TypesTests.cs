using System.Buffers.Binary;
using SimplePLCDriverCore.Abstractions;
using SimplePLCDriverCore.Protocols.S7;

namespace SimplePLCDriverCore.Tests.S7;

public class S7TypesTests
{
    // ==========================================================================
    // Decode Tests
    // ==========================================================================

    [Fact]
    public void DecodeValue_Bit_True()
    {
        var addr = S7Address.Parse("DB1.DBX0.0");
        var data = new byte[] { 0x01 };
        var result = S7Types.DecodeValue(data, addr);
        Assert.True(result.AsBoolean());
    }

    [Fact]
    public void DecodeValue_Bit_False()
    {
        var addr = S7Address.Parse("DB1.DBX0.0");
        var data = new byte[] { 0x00 };
        var result = S7Types.DecodeValue(data, addr);
        Assert.False(result.AsBoolean());
    }

    [Fact]
    public void DecodeValue_Byte()
    {
        var addr = S7Address.Parse("DB1.DBB0");
        var data = new byte[] { 0x2A };
        var result = S7Types.DecodeValue(data, addr);
        Assert.Equal(42, result.AsInt32());
    }

    [Fact]
    public void DecodeValue_Word_BigEndian()
    {
        var addr = S7Address.Parse("DB1.DBW0");
        var data = new byte[2];
        BinaryPrimitives.WriteInt16BigEndian(data, 1000);
        var result = S7Types.DecodeValue(data, addr);
        Assert.Equal(1000, result.AsInt32());
    }

    [Fact]
    public void DecodeValue_DWord_BigEndian()
    {
        var addr = S7Address.Parse("DB1.DBD0");
        var data = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(data, 100000);
        var result = S7Types.DecodeValue(data, addr);
        Assert.Equal(100000, result.AsInt32());
    }

    [Fact]
    public void DecodeValue_Real_BigEndian()
    {
        var addr = new S7Address(S7Area.DataBlock, 1, 0, 0,
            S7TransportSize.Real, 4, false);
        var data = new byte[4];
        BinaryPrimitives.WriteSingleBigEndian(data, 3.14f);
        var result = S7Types.DecodeValue(data, addr);
        Assert.Equal(3.14f, result.AsSingle(), 0.001f);
    }

    [Fact]
    public void DecodeValue_Timer_BigEndian()
    {
        var addr = S7Address.Parse("T0");
        var data = new byte[2];
        BinaryPrimitives.WriteInt16BigEndian(data, 500);
        var result = S7Types.DecodeValue(data, addr);
        Assert.Equal(500, result.AsInt32());
    }

    [Fact]
    public void DecodeValue_Counter_BigEndian()
    {
        var addr = S7Address.Parse("C0");
        var data = new byte[2];
        BinaryPrimitives.WriteInt16BigEndian(data, 42);
        var result = S7Types.DecodeValue(data, addr);
        Assert.Equal(42, result.AsInt32());
    }

    [Fact]
    public void DecodeString_ValidString()
    {
        var addr = new S7Address(S7Area.DataBlock, 1, 0, 0,
            S7TransportSize.Byte, 22, false, isString: true, stringMaxLength: 20);
        // S7 string: byte 0 = max length, byte 1 = actual length, then chars
        var data = new byte[22];
        data[0] = 20; // max
        data[1] = 5;  // actual
        data[2] = (byte)'H';
        data[3] = (byte)'e';
        data[4] = (byte)'l';
        data[5] = (byte)'l';
        data[6] = (byte)'o';

        var result = S7Types.DecodeValue(data, addr);
        Assert.Equal("Hello", result.AsString());
    }

    [Fact]
    public void DecodeString_EmptyString()
    {
        var data = new byte[] { 20, 0 };
        var result = S7Types.DecodeString(data);
        Assert.Equal("", result.AsString());
    }

    // ==========================================================================
    // Encode Tests
    // ==========================================================================

    [Fact]
    public void EncodeValue_Bit_True()
    {
        var addr = S7Address.Parse("DB1.DBX0.0");
        var data = S7Types.EncodeValue(true, addr);
        Assert.Single(data);
        Assert.Equal(1, data[0]);
    }

    [Fact]
    public void EncodeValue_Bit_False()
    {
        var addr = S7Address.Parse("DB1.DBX0.0");
        var data = S7Types.EncodeValue(false, addr);
        Assert.Equal(0, data[0]);
    }

    [Fact]
    public void EncodeValue_Byte()
    {
        var addr = S7Address.Parse("DB1.DBB0");
        var data = S7Types.EncodeValue((byte)42, addr);
        Assert.Single(data);
        Assert.Equal(42, data[0]);
    }

    [Fact]
    public void EncodeValue_Word_BigEndian()
    {
        var addr = S7Address.Parse("DB1.DBW0");
        var data = S7Types.EncodeValue((short)1000, addr);
        Assert.Equal(2, data.Length);
        Assert.Equal(1000, BinaryPrimitives.ReadInt16BigEndian(data));
    }

    [Fact]
    public void EncodeValue_DWord_BigEndian()
    {
        var addr = S7Address.Parse("DB1.DBD0");
        var data = S7Types.EncodeValue(100000, addr);
        Assert.Equal(4, data.Length);
        Assert.Equal(100000, BinaryPrimitives.ReadInt32BigEndian(data));
    }

    [Fact]
    public void EncodeValue_Real_BigEndian()
    {
        var addr = new S7Address(S7Area.DataBlock, 1, 0, 0,
            S7TransportSize.Real, 4, false);
        var data = S7Types.EncodeValue(3.14f, addr);
        Assert.Equal(4, data.Length);
        Assert.Equal(3.14f, BinaryPrimitives.ReadSingleBigEndian(data), 0.001f);
    }

    [Fact]
    public void EncodeString_Correct()
    {
        var encoded = S7Types.EncodeString("Hello", 20);
        Assert.Equal(22, encoded.Length); // 2 header + 20 data
        Assert.Equal(20, encoded[0]); // max length
        Assert.Equal(5, encoded[1]);  // actual length
        Assert.Equal((byte)'H', encoded[2]);
        Assert.Equal((byte)'o', encoded[6]);
    }

    // ==========================================================================
    // Type Name Tests
    // ==========================================================================

    [Fact]
    public void GetTypeName_Bit_ReturnsBool() =>
        Assert.Equal("BOOL", S7Types.GetTypeName(S7Address.Parse("DB1.DBX0.0")));

    [Fact]
    public void GetTypeName_Byte_ReturnsByte() =>
        Assert.Equal("BYTE", S7Types.GetTypeName(S7Address.Parse("DB1.DBB0")));

    [Fact]
    public void GetTypeName_Word_ReturnsWord() =>
        Assert.Equal("WORD", S7Types.GetTypeName(S7Address.Parse("DB1.DBW0")));

    [Fact]
    public void GetTypeName_DWord_ReturnsDWord() =>
        Assert.Equal("DWORD", S7Types.GetTypeName(S7Address.Parse("DB1.DBD0")));

    [Fact]
    public void GetTypeName_Timer_ReturnsTimer() =>
        Assert.Equal("TIMER", S7Types.GetTypeName(S7Address.Parse("T0")));

    [Fact]
    public void GetTypeName_Counter_ReturnsCounter() =>
        Assert.Equal("COUNTER", S7Types.GetTypeName(S7Address.Parse("C0")));

    [Fact]
    public void GetTypeName_String_ReturnsString()
    {
        var addr = new S7Address(S7Area.DataBlock, 1, 0, 0,
            S7TransportSize.Byte, 22, false, isString: true, stringMaxLength: 20);
        Assert.Equal("STRING", S7Types.GetTypeName(addr));
    }

    // ==========================================================================
    // PlcDataType Mapping
    // ==========================================================================

    [Fact]
    public void ToPlcDataType_Bit_ReturnsBool() =>
        Assert.Equal(PlcDataType.Bool, S7Types.ToPlcDataType(S7Address.Parse("DB1.DBX0.0")));

    [Fact]
    public void ToPlcDataType_Word_ReturnsInt() =>
        Assert.Equal(PlcDataType.Int, S7Types.ToPlcDataType(S7Address.Parse("DB1.DBW0")));

    [Fact]
    public void ToPlcDataType_DWord_ReturnsDint() =>
        Assert.Equal(PlcDataType.Dint, S7Types.ToPlcDataType(S7Address.Parse("DB1.DBD0")));

    [Fact]
    public void ToPlcDataType_String_ReturnsString()
    {
        var addr = new S7Address(S7Area.DataBlock, 1, 0, 0,
            S7TransportSize.Byte, 22, false, isString: true, stringMaxLength: 20);
        Assert.Equal(PlcDataType.String, S7Types.ToPlcDataType(addr));
    }
}
