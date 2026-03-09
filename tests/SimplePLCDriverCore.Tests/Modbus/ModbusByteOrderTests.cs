using SimplePLCDriverCore.Protocols.Modbus;

namespace SimplePLCDriverCore.Tests.Modbus;

public class ModbusByteOrderTests
{
    // ==========================================================================
    // Reorder4 - All 4 byte orders
    // ==========================================================================

    [Fact]
    public void Reorder4_ABCD_NoChange()
    {
        byte[] bytes = { 0x40, 0x48, 0xF5, 0xC3 };
        ModbusByteOrderHelper.Reorder4(bytes, ModbusByteOrder.ABCD);
        Assert.Equal(new byte[] { 0x40, 0x48, 0xF5, 0xC3 }, bytes);
    }

    [Fact]
    public void Reorder4_DCBA_FullReversal()
    {
        // Wire bytes are in DCBA order: D=0xC3, C=0xF5, B=0x48, A=0x40
        byte[] bytes = { 0xC3, 0xF5, 0x48, 0x40 };
        ModbusByteOrderHelper.Reorder4(bytes, ModbusByteOrder.DCBA);
        Assert.Equal(new byte[] { 0x40, 0x48, 0xF5, 0xC3 }, bytes);
    }

    [Fact]
    public void Reorder4_BADC_ByteSwapWithinWords()
    {
        // Wire bytes are in BADC order: B=0x48, A=0x40, D=0xC3, C=0xF5
        byte[] bytes = { 0x48, 0x40, 0xC3, 0xF5 };
        ModbusByteOrderHelper.Reorder4(bytes, ModbusByteOrder.BADC);
        Assert.Equal(new byte[] { 0x40, 0x48, 0xF5, 0xC3 }, bytes);
    }

    [Fact]
    public void Reorder4_CDAB_WordSwap()
    {
        // Wire bytes are in CDAB order: C=0xF5, D=0xC3, A=0x40, B=0x48
        byte[] bytes = { 0xF5, 0xC3, 0x40, 0x48 };
        ModbusByteOrderHelper.Reorder4(bytes, ModbusByteOrder.CDAB);
        Assert.Equal(new byte[] { 0x40, 0x48, 0xF5, 0xC3 }, bytes);
    }

    [Fact]
    public void Reorder4_ShortBuffer_DoesNothing()
    {
        byte[] bytes = { 0x01, 0x02, 0x03 };
        ModbusByteOrderHelper.Reorder4(bytes, ModbusByteOrder.DCBA);
        Assert.Equal(new byte[] { 0x01, 0x02, 0x03 }, bytes);
    }

    // ==========================================================================
    // Reorder8 - All 4 byte orders
    // ==========================================================================

    [Fact]
    public void Reorder8_ABCD_NoChange()
    {
        byte[] bytes = { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08 };
        ModbusByteOrderHelper.Reorder8(bytes, ModbusByteOrder.ABCD);
        Assert.Equal(new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08 }, bytes);
    }

    [Fact]
    public void Reorder8_DCBA_FullReversal()
    {
        byte[] bytes = { 0x08, 0x07, 0x06, 0x05, 0x04, 0x03, 0x02, 0x01 };
        ModbusByteOrderHelper.Reorder8(bytes, ModbusByteOrder.DCBA);
        Assert.Equal(new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08 }, bytes);
    }

    [Fact]
    public void Reorder8_BADC_ByteSwapWithinWords()
    {
        // BADC: swap bytes within each 16-bit word
        byte[] bytes = { 0x02, 0x01, 0x04, 0x03, 0x06, 0x05, 0x08, 0x07 };
        ModbusByteOrderHelper.Reorder8(bytes, ModbusByteOrder.BADC);
        Assert.Equal(new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08 }, bytes);
    }

    [Fact]
    public void Reorder8_CDAB_WordSwap()
    {
        // CDAB per 32-bit chunk: [CD][AB] -> [AB][CD]
        // Input: 0x03,0x04, 0x01,0x02, 0x07,0x08, 0x05,0x06
        byte[] bytes = { 0x03, 0x04, 0x01, 0x02, 0x07, 0x08, 0x05, 0x06 };
        ModbusByteOrderHelper.Reorder8(bytes, ModbusByteOrder.CDAB);
        Assert.Equal(new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08 }, bytes);
    }

    [Fact]
    public void Reorder8_ShortBuffer_DoesNothing()
    {
        byte[] bytes = { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07 };
        var original = (byte[])bytes.Clone();
        ModbusByteOrderHelper.Reorder8(bytes, ModbusByteOrder.DCBA);
        Assert.Equal(original, bytes);
    }

    // ==========================================================================
    // ToWire4 - All 4 byte orders
    // ==========================================================================

    [Fact]
    public void ToWire4_ABCD_NoChange()
    {
        byte[] bytes = { 0x40, 0x48, 0xF5, 0xC3 };
        ModbusByteOrderHelper.ToWire4(bytes, ModbusByteOrder.ABCD);
        Assert.Equal(new byte[] { 0x40, 0x48, 0xF5, 0xC3 }, bytes);
    }

    [Fact]
    public void ToWire4_DCBA_FullReversal()
    {
        byte[] bytes = { 0x40, 0x48, 0xF5, 0xC3 };
        ModbusByteOrderHelper.ToWire4(bytes, ModbusByteOrder.DCBA);
        Assert.Equal(new byte[] { 0xC3, 0xF5, 0x48, 0x40 }, bytes);
    }

    [Fact]
    public void ToWire4_BADC_ByteSwapWithinWords()
    {
        byte[] bytes = { 0x40, 0x48, 0xF5, 0xC3 };
        ModbusByteOrderHelper.ToWire4(bytes, ModbusByteOrder.BADC);
        Assert.Equal(new byte[] { 0x48, 0x40, 0xC3, 0xF5 }, bytes);
    }

    [Fact]
    public void ToWire4_CDAB_WordSwap()
    {
        byte[] bytes = { 0x40, 0x48, 0xF5, 0xC3 };
        ModbusByteOrderHelper.ToWire4(bytes, ModbusByteOrder.CDAB);
        Assert.Equal(new byte[] { 0xF5, 0xC3, 0x40, 0x48 }, bytes);
    }

    // ==========================================================================
    // ToWire8 - All 4 byte orders
    // ==========================================================================

    [Fact]
    public void ToWire8_ABCD_NoChange()
    {
        byte[] bytes = { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08 };
        ModbusByteOrderHelper.ToWire8(bytes, ModbusByteOrder.ABCD);
        Assert.Equal(new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08 }, bytes);
    }

    [Fact]
    public void ToWire8_DCBA_FullReversal()
    {
        byte[] bytes = { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08 };
        ModbusByteOrderHelper.ToWire8(bytes, ModbusByteOrder.DCBA);
        Assert.Equal(new byte[] { 0x08, 0x07, 0x06, 0x05, 0x04, 0x03, 0x02, 0x01 }, bytes);
    }

    [Fact]
    public void ToWire8_BADC_ByteSwapWithinWords()
    {
        byte[] bytes = { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08 };
        ModbusByteOrderHelper.ToWire8(bytes, ModbusByteOrder.BADC);
        Assert.Equal(new byte[] { 0x02, 0x01, 0x04, 0x03, 0x06, 0x05, 0x08, 0x07 }, bytes);
    }

    [Fact]
    public void ToWire8_CDAB_WordSwap()
    {
        byte[] bytes = { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08 };
        ModbusByteOrderHelper.ToWire8(bytes, ModbusByteOrder.CDAB);
        Assert.Equal(new byte[] { 0x03, 0x04, 0x01, 0x02, 0x07, 0x08, 0x05, 0x06 }, bytes);
    }

    // ==========================================================================
    // Round-trip: Reorder4 then ToWire4
    // ==========================================================================

    [Fact]
    public void RoundTrip4_Reorder_Then_ToWire_DCBA()
    {
        byte[] original = { 0xDE, 0xAD, 0xBE, 0xEF };
        byte[] bytes = (byte[])original.Clone();
        ModbusByteOrderHelper.Reorder4(bytes, ModbusByteOrder.DCBA);
        ModbusByteOrderHelper.ToWire4(bytes, ModbusByteOrder.DCBA);
        Assert.Equal(original, bytes);
    }

    [Fact]
    public void RoundTrip4_Reorder_Then_ToWire_BADC()
    {
        byte[] original = { 0xDE, 0xAD, 0xBE, 0xEF };
        byte[] bytes = (byte[])original.Clone();
        ModbusByteOrderHelper.Reorder4(bytes, ModbusByteOrder.BADC);
        ModbusByteOrderHelper.ToWire4(bytes, ModbusByteOrder.BADC);
        Assert.Equal(original, bytes);
    }

    [Fact]
    public void RoundTrip4_Reorder_Then_ToWire_CDAB()
    {
        byte[] original = { 0xDE, 0xAD, 0xBE, 0xEF };
        byte[] bytes = (byte[])original.Clone();
        ModbusByteOrderHelper.Reorder4(bytes, ModbusByteOrder.CDAB);
        ModbusByteOrderHelper.ToWire4(bytes, ModbusByteOrder.CDAB);
        Assert.Equal(original, bytes);
    }

    [Fact]
    public void RoundTrip4_ToWire_Then_Reorder_DCBA()
    {
        byte[] original = { 0xCA, 0xFE, 0xBA, 0xBE };
        byte[] bytes = (byte[])original.Clone();
        ModbusByteOrderHelper.ToWire4(bytes, ModbusByteOrder.DCBA);
        ModbusByteOrderHelper.Reorder4(bytes, ModbusByteOrder.DCBA);
        Assert.Equal(original, bytes);
    }

    // ==========================================================================
    // Round-trip: Reorder8 then ToWire8
    // ==========================================================================

    [Fact]
    public void RoundTrip8_Reorder_Then_ToWire_DCBA()
    {
        byte[] original = { 0x01, 0x23, 0x45, 0x67, 0x89, 0xAB, 0xCD, 0xEF };
        byte[] bytes = (byte[])original.Clone();
        ModbusByteOrderHelper.Reorder8(bytes, ModbusByteOrder.DCBA);
        ModbusByteOrderHelper.ToWire8(bytes, ModbusByteOrder.DCBA);
        Assert.Equal(original, bytes);
    }

    [Fact]
    public void RoundTrip8_Reorder_Then_ToWire_BADC()
    {
        byte[] original = { 0x01, 0x23, 0x45, 0x67, 0x89, 0xAB, 0xCD, 0xEF };
        byte[] bytes = (byte[])original.Clone();
        ModbusByteOrderHelper.Reorder8(bytes, ModbusByteOrder.BADC);
        ModbusByteOrderHelper.ToWire8(bytes, ModbusByteOrder.BADC);
        Assert.Equal(original, bytes);
    }

    [Fact]
    public void RoundTrip8_Reorder_Then_ToWire_CDAB()
    {
        byte[] original = { 0x01, 0x23, 0x45, 0x67, 0x89, 0xAB, 0xCD, 0xEF };
        byte[] bytes = (byte[])original.Clone();
        ModbusByteOrderHelper.Reorder8(bytes, ModbusByteOrder.CDAB);
        ModbusByteOrderHelper.ToWire8(bytes, ModbusByteOrder.CDAB);
        Assert.Equal(original, bytes);
    }

    [Fact]
    public void RoundTrip8_ToWire_Then_Reorder_CDAB()
    {
        byte[] original = { 0xFE, 0xDC, 0xBA, 0x98, 0x76, 0x54, 0x32, 0x10 };
        byte[] bytes = (byte[])original.Clone();
        ModbusByteOrderHelper.ToWire8(bytes, ModbusByteOrder.CDAB);
        ModbusByteOrderHelper.Reorder8(bytes, ModbusByteOrder.CDAB);
        Assert.Equal(original, bytes);
    }
}
