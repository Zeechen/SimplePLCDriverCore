using System.Buffers.Binary;
using SimplePLCDriverCore.Abstractions;
using SimplePLCDriverCore.Protocols.Fins;

namespace SimplePLCDriverCore.Tests.Fins;

public class FinsTypesTests
{
    // ==========================================================================
    // DecodeWord Tests
    // ==========================================================================

    [Fact]
    public void DecodeWord_Bit_True()
    {
        var addr = FinsAddress.Parse("CIO0.00");
        var data = new byte[] { 0x01 };
        var result = FinsTypes.DecodeWord(data, addr);
        Assert.True(result.AsBoolean());
    }

    [Fact]
    public void DecodeWord_Bit_False()
    {
        var addr = FinsAddress.Parse("CIO0.00");
        var data = new byte[] { 0x00 };
        var result = FinsTypes.DecodeWord(data, addr);
        Assert.False(result.AsBoolean());
    }

    [Fact]
    public void DecodeWord_Word_BigEndian()
    {
        var addr = FinsAddress.Parse("D0");
        var data = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(data, 1000);
        var result = FinsTypes.DecodeWord(data, addr);
        Assert.Equal(1000, result.AsInt32());
    }

    [Fact]
    public void DecodeWord_Word_NegativeValue()
    {
        var addr = FinsAddress.Parse("D0");
        var data = new byte[2];
        BinaryPrimitives.WriteInt16BigEndian(data, -100);
        var result = FinsTypes.DecodeWord(data, addr);
        Assert.Equal(-100, result.AsInt32());
    }

    [Fact]
    public void DecodeWord_BitTooShort_Throws()
    {
        var addr = FinsAddress.Parse("CIO0.00");
        Assert.Throws<InvalidOperationException>(
            () => FinsTypes.DecodeWord(ReadOnlySpan<byte>.Empty, addr));
    }

    [Fact]
    public void DecodeWord_WordTooShort_Throws()
    {
        var addr = FinsAddress.Parse("D0");
        Assert.Throws<InvalidOperationException>(
            () => FinsTypes.DecodeWord(new byte[] { 0x01 }, addr));
    }

    // ==========================================================================
    // DecodeDWord Tests
    // ==========================================================================

    [Fact]
    public void DecodeDWord_BigEndian()
    {
        var data = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(data, 100000);
        var result = FinsTypes.DecodeDWord(data);
        Assert.Equal(100000, result.AsInt32());
    }

    [Fact]
    public void DecodeDWord_TooShort_Throws()
    {
        Assert.Throws<InvalidOperationException>(
            () => FinsTypes.DecodeDWord(new byte[] { 0x01, 0x02 }));
    }

    // ==========================================================================
    // DecodeFloat Tests
    // ==========================================================================

    [Fact]
    public void DecodeFloat_BigEndian()
    {
        var data = new byte[4];
        BinaryPrimitives.WriteSingleBigEndian(data, 3.14f);
        var result = FinsTypes.DecodeFloat(data);
        Assert.Equal(3.14f, result.AsSingle(), 0.001f);
    }

    [Fact]
    public void DecodeFloat_TooShort_Throws()
    {
        Assert.Throws<InvalidOperationException>(
            () => FinsTypes.DecodeFloat(new byte[] { 0x01 }));
    }

    // ==========================================================================
    // EncodeWord Tests
    // ==========================================================================

    [Fact]
    public void EncodeWord_Bit_True()
    {
        var addr = FinsAddress.Parse("CIO0.00");
        var data = FinsTypes.EncodeWord(true, addr);
        Assert.Single(data);
        Assert.Equal(0x01, data[0]);
    }

    [Fact]
    public void EncodeWord_Bit_False()
    {
        var addr = FinsAddress.Parse("CIO0.00");
        var data = FinsTypes.EncodeWord(false, addr);
        Assert.Single(data);
        Assert.Equal(0x00, data[0]);
    }

    [Fact]
    public void EncodeWord_Word_BigEndian()
    {
        var addr = FinsAddress.Parse("D0");
        var data = FinsTypes.EncodeWord((short)1000, addr);
        Assert.Equal(2, data.Length);
        Assert.Equal(1000, BinaryPrimitives.ReadInt16BigEndian(data));
    }

    // ==========================================================================
    // EncodeDWord Tests
    // ==========================================================================

    [Fact]
    public void EncodeDWord_BigEndian()
    {
        var data = FinsTypes.EncodeDWord(100000);
        Assert.Equal(4, data.Length);
        Assert.Equal(100000, BinaryPrimitives.ReadInt32BigEndian(data));
    }

    // ==========================================================================
    // EncodeFloat Tests
    // ==========================================================================

    [Fact]
    public void EncodeFloat_BigEndian()
    {
        var data = FinsTypes.EncodeFloat(3.14f);
        Assert.Equal(4, data.Length);
        Assert.Equal(3.14f, BinaryPrimitives.ReadSingleBigEndian(data), 0.001f);
    }

    // ==========================================================================
    // Type Name Tests
    // ==========================================================================

    [Fact]
    public void GetTypeName_Bit_ReturnsBit()
    {
        Assert.Equal("BIT", FinsTypes.GetTypeName(FinsAddress.Parse("CIO0.00")));
    }

    [Fact]
    public void GetTypeName_DmWord_ReturnsWord()
    {
        Assert.Equal("WORD", FinsTypes.GetTypeName(FinsAddress.Parse("D0")));
    }

    [Fact]
    public void GetTypeName_TimerPv_ReturnsWord()
    {
        Assert.Equal("WORD", FinsTypes.GetTypeName(FinsAddress.Parse("T0")));
    }

    // ==========================================================================
    // PlcDataType Mapping
    // ==========================================================================

    [Fact]
    public void ToPlcDataType_Bit_ReturnsBool()
    {
        Assert.Equal(PlcDataType.Bool, FinsTypes.ToPlcDataType(FinsAddress.Parse("CIO0.00")));
    }

    [Fact]
    public void ToPlcDataType_Word_ReturnsInt()
    {
        Assert.Equal(PlcDataType.Int, FinsTypes.ToPlcDataType(FinsAddress.Parse("D0")));
    }
}
