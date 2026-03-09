using System.Buffers.Binary;
using SimplePLCDriverCore.Protocols.Modbus;

namespace SimplePLCDriverCore.Tests.Modbus;

public class ModbusMultiRegisterTests
{
    // ==========================================================================
    // DecodeFloat32 - All 4 byte orders
    // ==========================================================================

    [Fact]
    public void DecodeFloat32_ABCD_Pi()
    {
        // 3.14f in IEEE 754 big-endian: 0x40 0x48 0xF5 0xC3
        byte[] data = { 0x40, 0x48, 0xF5, 0xC3 };
        var result = ModbusTypes.DecodeFloat32(data, ModbusByteOrder.ABCD);
        Assert.Equal(3.14f, result, 0.001f);
    }

    [Fact]
    public void DecodeFloat32_DCBA_Pi()
    {
        // DCBA wire order for 3.14f: bytes reversed
        byte[] data = { 0xC3, 0xF5, 0x48, 0x40 };
        var result = ModbusTypes.DecodeFloat32(data, ModbusByteOrder.DCBA);
        Assert.Equal(3.14f, result, 0.001f);
    }

    [Fact]
    public void DecodeFloat32_BADC_Pi()
    {
        // BADC wire order: byte-swapped within words
        byte[] data = { 0x48, 0x40, 0xC3, 0xF5 };
        var result = ModbusTypes.DecodeFloat32(data, ModbusByteOrder.BADC);
        Assert.Equal(3.14f, result, 0.001f);
    }

    [Fact]
    public void DecodeFloat32_CDAB_Pi()
    {
        // CDAB wire order: word-swapped
        byte[] data = { 0xF5, 0xC3, 0x40, 0x48 };
        var result = ModbusTypes.DecodeFloat32(data, ModbusByteOrder.CDAB);
        Assert.Equal(3.14f, result, 0.001f);
    }

    [Fact]
    public void DecodeFloat32_ABCD_Zero()
    {
        byte[] data = { 0x00, 0x00, 0x00, 0x00 };
        var result = ModbusTypes.DecodeFloat32(data, ModbusByteOrder.ABCD);
        Assert.Equal(0.0f, result);
    }

    [Fact]
    public void DecodeFloat32_ABCD_NegativeValue()
    {
        // -1.5f big-endian
        var bytes = new byte[4];
        BinaryPrimitives.WriteSingleBigEndian(bytes, -1.5f);
        var result = ModbusTypes.DecodeFloat32(bytes, ModbusByteOrder.ABCD);
        Assert.Equal(-1.5f, result);
    }

    [Fact]
    public void DecodeFloat32_TooShort_Throws()
    {
        Assert.Throws<InvalidOperationException>(
            () => ModbusTypes.DecodeFloat32(new byte[] { 0x00, 0x00, 0x00 }, ModbusByteOrder.ABCD));
    }

    // ==========================================================================
    // DecodeFloat64 - All 4 byte orders
    // ==========================================================================

    [Fact]
    public void DecodeFloat64_ABCD_Pi()
    {
        var bytes = new byte[8];
        BinaryPrimitives.WriteDoubleBigEndian(bytes, 3.14159265358979);
        var result = ModbusTypes.DecodeFloat64(bytes, ModbusByteOrder.ABCD);
        Assert.Equal(3.14159265358979, result, 10);
    }

    [Fact]
    public void DecodeFloat64_DCBA()
    {
        // Encode as ABCD then reverse for DCBA wire format
        var abcd = new byte[8];
        BinaryPrimitives.WriteDoubleBigEndian(abcd, 2.71828);
        byte[] wire = { abcd[7], abcd[6], abcd[5], abcd[4], abcd[3], abcd[2], abcd[1], abcd[0] };
        var result = ModbusTypes.DecodeFloat64(wire, ModbusByteOrder.DCBA);
        Assert.Equal(2.71828, result, 5);
    }

    [Fact]
    public void DecodeFloat64_BADC()
    {
        var abcd = new byte[8];
        BinaryPrimitives.WriteDoubleBigEndian(abcd, -100.5);
        byte[] wire = { abcd[1], abcd[0], abcd[3], abcd[2], abcd[5], abcd[4], abcd[7], abcd[6] };
        var result = ModbusTypes.DecodeFloat64(wire, ModbusByteOrder.BADC);
        Assert.Equal(-100.5, result, 5);
    }

    [Fact]
    public void DecodeFloat64_CDAB()
    {
        var abcd = new byte[8];
        BinaryPrimitives.WriteDoubleBigEndian(abcd, 999.999);
        byte[] wire = { abcd[2], abcd[3], abcd[0], abcd[1], abcd[6], abcd[7], abcd[4], abcd[5] };
        var result = ModbusTypes.DecodeFloat64(wire, ModbusByteOrder.CDAB);
        Assert.Equal(999.999, result, 3);
    }

    // ==========================================================================
    // DecodeInt32 - All 4 byte orders
    // ==========================================================================

    [Fact]
    public void DecodeInt32_ABCD()
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(bytes, 123456);
        var result = ModbusTypes.DecodeInt32(bytes, ModbusByteOrder.ABCD);
        Assert.Equal(123456, result);
    }

    [Fact]
    public void DecodeInt32_DCBA()
    {
        var abcd = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(abcd, -54321);
        byte[] wire = { abcd[3], abcd[2], abcd[1], abcd[0] };
        var result = ModbusTypes.DecodeInt32(wire, ModbusByteOrder.DCBA);
        Assert.Equal(-54321, result);
    }

    [Fact]
    public void DecodeInt32_BADC()
    {
        var abcd = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(abcd, 100000);
        byte[] wire = { abcd[1], abcd[0], abcd[3], abcd[2] };
        var result = ModbusTypes.DecodeInt32(wire, ModbusByteOrder.BADC);
        Assert.Equal(100000, result);
    }

    [Fact]
    public void DecodeInt32_CDAB()
    {
        var abcd = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(abcd, 70000);
        byte[] wire = { abcd[2], abcd[3], abcd[0], abcd[1] };
        var result = ModbusTypes.DecodeInt32(wire, ModbusByteOrder.CDAB);
        Assert.Equal(70000, result);
    }

    // ==========================================================================
    // DecodeUInt32 - All 4 byte orders
    // ==========================================================================

    [Fact]
    public void DecodeUInt32_ABCD()
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, 3000000000u);
        var result = ModbusTypes.DecodeUInt32(bytes, ModbusByteOrder.ABCD);
        Assert.Equal(3000000000u, result);
    }

    [Fact]
    public void DecodeUInt32_DCBA()
    {
        var abcd = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(abcd, 4000000000u);
        byte[] wire = { abcd[3], abcd[2], abcd[1], abcd[0] };
        var result = ModbusTypes.DecodeUInt32(wire, ModbusByteOrder.DCBA);
        Assert.Equal(4000000000u, result);
    }

    [Fact]
    public void DecodeUInt32_BADC()
    {
        var abcd = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(abcd, 2500000000u);
        byte[] wire = { abcd[1], abcd[0], abcd[3], abcd[2] };
        var result = ModbusTypes.DecodeUInt32(wire, ModbusByteOrder.BADC);
        Assert.Equal(2500000000u, result);
    }

    [Fact]
    public void DecodeUInt32_CDAB()
    {
        var abcd = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(abcd, 1234567890u);
        byte[] wire = { abcd[2], abcd[3], abcd[0], abcd[1] };
        var result = ModbusTypes.DecodeUInt32(wire, ModbusByteOrder.CDAB);
        Assert.Equal(1234567890u, result);
    }

    // ==========================================================================
    // DecodeInt64 / DecodeUInt64 - ABCD at minimum
    // ==========================================================================

    [Fact]
    public void DecodeInt64_ABCD()
    {
        var bytes = new byte[8];
        BinaryPrimitives.WriteInt64BigEndian(bytes, 1234567890123L);
        var result = ModbusTypes.DecodeInt64(bytes, ModbusByteOrder.ABCD);
        Assert.Equal(1234567890123L, result);
    }

    [Fact]
    public void DecodeInt64_DCBA()
    {
        var abcd = new byte[8];
        BinaryPrimitives.WriteInt64BigEndian(abcd, -9876543210L);
        byte[] wire = { abcd[7], abcd[6], abcd[5], abcd[4], abcd[3], abcd[2], abcd[1], abcd[0] };
        var result = ModbusTypes.DecodeInt64(wire, ModbusByteOrder.DCBA);
        Assert.Equal(-9876543210L, result);
    }

    [Fact]
    public void DecodeUInt64_ABCD()
    {
        var bytes = new byte[8];
        BinaryPrimitives.WriteUInt64BigEndian(bytes, 18000000000000000000UL);
        var result = ModbusTypes.DecodeUInt64(bytes, ModbusByteOrder.ABCD);
        Assert.Equal(18000000000000000000UL, result);
    }

    [Fact]
    public void DecodeUInt64_CDAB()
    {
        var abcd = new byte[8];
        BinaryPrimitives.WriteUInt64BigEndian(abcd, 5555555555UL);
        byte[] wire = { abcd[2], abcd[3], abcd[0], abcd[1], abcd[6], abcd[7], abcd[4], abcd[5] };
        var result = ModbusTypes.DecodeUInt64(wire, ModbusByteOrder.CDAB);
        Assert.Equal(5555555555UL, result);
    }

    // ==========================================================================
    // DecodeString
    // ==========================================================================

    [Fact]
    public void DecodeString_NormalAscii()
    {
        // "AB" = 0x41 0x42, "CD" = 0x43 0x44
        byte[] data = { 0x41, 0x42, 0x43, 0x44 };
        var result = ModbusTypes.DecodeString(data, 2);
        Assert.Equal("ABCD", result);
    }

    [Fact]
    public void DecodeString_WithNullTerminator()
    {
        // "Hi" followed by nulls
        byte[] data = { 0x48, 0x69, 0x00, 0x00 };
        var result = ModbusTypes.DecodeString(data, 2);
        Assert.Equal("Hi", result);
    }

    [Fact]
    public void DecodeString_Empty()
    {
        byte[] data = { 0x00, 0x00 };
        var result = ModbusTypes.DecodeString(data, 1);
        Assert.Equal("", result);
    }

    // ==========================================================================
    // EncodeFloat32 - Round-trip with DecodeFloat32
    // ==========================================================================

    [Fact]
    public void EncodeFloat32_RoundTrip_ABCD()
    {
        var regs = ModbusTypes.EncodeFloat32(3.14f, ModbusByteOrder.ABCD);
        Assert.Equal(2, regs.Length);
        var bytes = RegsToBytes(regs);
        var decoded = ModbusTypes.DecodeFloat32(bytes, ModbusByteOrder.ABCD);
        Assert.Equal(3.14f, decoded, 0.001f);
    }

    [Fact]
    public void EncodeFloat32_RoundTrip_DCBA()
    {
        var regs = ModbusTypes.EncodeFloat32(-1.5f, ModbusByteOrder.DCBA);
        var bytes = RegsToBytes(regs);
        var decoded = ModbusTypes.DecodeFloat32(bytes, ModbusByteOrder.DCBA);
        Assert.Equal(-1.5f, decoded);
    }

    [Fact]
    public void EncodeFloat32_RoundTrip_BADC()
    {
        var regs = ModbusTypes.EncodeFloat32(0.0f, ModbusByteOrder.BADC);
        var bytes = RegsToBytes(regs);
        var decoded = ModbusTypes.DecodeFloat32(bytes, ModbusByteOrder.BADC);
        Assert.Equal(0.0f, decoded);
    }

    [Fact]
    public void EncodeFloat32_RoundTrip_CDAB()
    {
        var regs = ModbusTypes.EncodeFloat32(1000.25f, ModbusByteOrder.CDAB);
        var bytes = RegsToBytes(regs);
        var decoded = ModbusTypes.DecodeFloat32(bytes, ModbusByteOrder.CDAB);
        Assert.Equal(1000.25f, decoded);
    }

    // ==========================================================================
    // EncodeFloat64 - Round-trip
    // ==========================================================================

    [Fact]
    public void EncodeFloat64_RoundTrip_ABCD()
    {
        var regs = ModbusTypes.EncodeFloat64(3.14159265358979, ModbusByteOrder.ABCD);
        Assert.Equal(4, regs.Length);
        var bytes = RegsToBytes(regs);
        var decoded = ModbusTypes.DecodeFloat64(bytes, ModbusByteOrder.ABCD);
        Assert.Equal(3.14159265358979, decoded, 10);
    }

    [Fact]
    public void EncodeFloat64_RoundTrip_DCBA()
    {
        var regs = ModbusTypes.EncodeFloat64(-999.123456, ModbusByteOrder.DCBA);
        var bytes = RegsToBytes(regs);
        var decoded = ModbusTypes.DecodeFloat64(bytes, ModbusByteOrder.DCBA);
        Assert.Equal(-999.123456, decoded, 6);
    }

    // ==========================================================================
    // EncodeInt32 - Round-trip
    // ==========================================================================

    [Fact]
    public void EncodeInt32_RoundTrip_ABCD()
    {
        var regs = ModbusTypes.EncodeInt32(123456, ModbusByteOrder.ABCD);
        Assert.Equal(2, regs.Length);
        var bytes = RegsToBytes(regs);
        var decoded = ModbusTypes.DecodeInt32(bytes, ModbusByteOrder.ABCD);
        Assert.Equal(123456, decoded);
    }

    [Fact]
    public void EncodeInt32_RoundTrip_DCBA()
    {
        var regs = ModbusTypes.EncodeInt32(-54321, ModbusByteOrder.DCBA);
        var bytes = RegsToBytes(regs);
        var decoded = ModbusTypes.DecodeInt32(bytes, ModbusByteOrder.DCBA);
        Assert.Equal(-54321, decoded);
    }

    [Fact]
    public void EncodeInt32_RoundTrip_CDAB()
    {
        var regs = ModbusTypes.EncodeInt32(int.MaxValue, ModbusByteOrder.CDAB);
        var bytes = RegsToBytes(regs);
        var decoded = ModbusTypes.DecodeInt32(bytes, ModbusByteOrder.CDAB);
        Assert.Equal(int.MaxValue, decoded);
    }

    // ==========================================================================
    // EncodeInt64 - Round-trip
    // ==========================================================================

    [Fact]
    public void EncodeInt64_RoundTrip_ABCD()
    {
        var regs = ModbusTypes.EncodeInt64(1234567890123L, ModbusByteOrder.ABCD);
        Assert.Equal(4, regs.Length);
        var bytes = RegsToBytes(regs);
        var decoded = ModbusTypes.DecodeInt64(bytes, ModbusByteOrder.ABCD);
        Assert.Equal(1234567890123L, decoded);
    }

    [Fact]
    public void EncodeInt64_RoundTrip_BADC()
    {
        var regs = ModbusTypes.EncodeInt64(-9876543210L, ModbusByteOrder.BADC);
        var bytes = RegsToBytes(regs);
        var decoded = ModbusTypes.DecodeInt64(bytes, ModbusByteOrder.BADC);
        Assert.Equal(-9876543210L, decoded);
    }

    // ==========================================================================
    // EncodeString - Round-trip and padding
    // ==========================================================================

    [Fact]
    public void EncodeString_RoundTrip()
    {
        var regs = ModbusTypes.EncodeString("ABCD", 2);
        Assert.Equal(2, regs.Length);
        var bytes = RegsToBytes(regs);
        var decoded = ModbusTypes.DecodeString(bytes, 2);
        Assert.Equal("ABCD", decoded);
    }

    [Fact]
    public void EncodeString_PaddingWithNulls()
    {
        // "Hi" is 2 bytes, but 2 registers = 4 bytes, so last 2 bytes should be 0x00
        var regs = ModbusTypes.EncodeString("Hi", 2);
        Assert.Equal(2, regs.Length);
        var bytes = RegsToBytes(regs);
        Assert.Equal((byte)'H', bytes[0]);
        Assert.Equal((byte)'i', bytes[1]);
        Assert.Equal(0x00, bytes[2]);
        Assert.Equal(0x00, bytes[3]);
    }

    [Fact]
    public void EncodeString_Empty()
    {
        var regs = ModbusTypes.EncodeString("", 2);
        Assert.Equal(2, regs.Length);
        var bytes = RegsToBytes(regs);
        var decoded = ModbusTypes.DecodeString(bytes, 2);
        Assert.Equal("", decoded);
    }

    [Fact]
    public void EncodeString_RoundTrip_LongerString()
    {
        var regs = ModbusTypes.EncodeString("Hello!", 3);
        Assert.Equal(3, regs.Length);
        var bytes = RegsToBytes(regs);
        var decoded = ModbusTypes.DecodeString(bytes, 3);
        Assert.Equal("Hello!", decoded);
    }

    // ==========================================================================
    // Helper: convert ushort[] registers to byte[] (big-endian per register)
    // ==========================================================================

    private static byte[] RegsToBytes(ushort[] regs)
    {
        var bytes = new byte[regs.Length * 2];
        for (int i = 0; i < regs.Length; i++)
            BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(i * 2, 2), regs[i]);
        return bytes;
    }
}
