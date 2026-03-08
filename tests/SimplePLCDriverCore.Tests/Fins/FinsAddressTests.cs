using SimplePLCDriverCore.Protocols.Fins;

namespace SimplePLCDriverCore.Tests.Fins;

public class FinsAddressTests
{
    // ==========================================================================
    // DM Area
    // ==========================================================================

    [Fact]
    public void Parse_D0_DmWord()
    {
        var addr = FinsAddress.Parse("D0");
        Assert.Equal(FinsArea.DmWord, addr.Area);
        Assert.Equal(0, addr.Address);
        Assert.False(addr.IsBitAddress);
    }

    [Fact]
    public void Parse_D100_DmWord()
    {
        var addr = FinsAddress.Parse("D100");
        Assert.Equal(FinsArea.DmWord, addr.Area);
        Assert.Equal(100, addr.Address);
    }

    [Fact]
    public void Parse_D100_Bit_DmBit()
    {
        var addr = FinsAddress.Parse("D100.5");
        Assert.Equal(FinsArea.DmBit, addr.Area);
        Assert.Equal(100, addr.Address);
        Assert.Equal(5, addr.BitNumber);
        Assert.True(addr.IsBitAddress);
    }

    [Fact]
    public void Parse_DM0_AlternateNotation()
    {
        var addr = FinsAddress.Parse("DM0");
        Assert.Equal(FinsArea.DmWord, addr.Area);
        Assert.Equal(0, addr.Address);
    }

    // ==========================================================================
    // CIO Area
    // ==========================================================================

    [Fact]
    public void Parse_CIO0_Word()
    {
        var addr = FinsAddress.Parse("CIO0");
        Assert.Equal(FinsArea.CioWord, addr.Area);
        Assert.Equal(0, addr.Address);
        Assert.False(addr.IsBitAddress);
    }

    [Fact]
    public void Parse_CIO0_Bit()
    {
        var addr = FinsAddress.Parse("CIO0.00");
        Assert.Equal(FinsArea.CioBit, addr.Area);
        Assert.Equal(0, addr.Address);
        Assert.Equal(0, addr.BitNumber);
        Assert.True(addr.IsBitAddress);
    }

    // ==========================================================================
    // Work Area
    // ==========================================================================

    [Fact]
    public void Parse_W0_WorkWord()
    {
        var addr = FinsAddress.Parse("W0");
        Assert.Equal(FinsArea.WorkWord, addr.Area);
        Assert.Equal(0, addr.Address);
    }

    [Fact]
    public void Parse_W0_Bit()
    {
        var addr = FinsAddress.Parse("W0.05");
        Assert.Equal(FinsArea.WorkBit, addr.Area);
        Assert.Equal(0, addr.Address);
        Assert.Equal(5, addr.BitNumber);
        Assert.True(addr.IsBitAddress);
    }

    // ==========================================================================
    // Holding Area
    // ==========================================================================

    [Fact]
    public void Parse_H0_HoldingWord()
    {
        var addr = FinsAddress.Parse("H0");
        Assert.Equal(FinsArea.HoldingWord, addr.Area);
        Assert.Equal(0, addr.Address);
    }

    // ==========================================================================
    // Auxiliary Area
    // ==========================================================================

    [Fact]
    public void Parse_A0_AuxWord()
    {
        var addr = FinsAddress.Parse("A0");
        Assert.Equal(FinsArea.AuxiliaryWord, addr.Area);
        Assert.Equal(0, addr.Address);
    }

    // ==========================================================================
    // Timer / Counter
    // ==========================================================================

    [Fact]
    public void Parse_T0_TimerPv()
    {
        var addr = FinsAddress.Parse("T0");
        Assert.Equal(FinsArea.TimerCounterPv, addr.Area);
        Assert.Equal(0, addr.Address);
        Assert.False(addr.IsBitAddress);
    }

    [Fact]
    public void Parse_C0_CounterPv()
    {
        var addr = FinsAddress.Parse("C0");
        Assert.Equal(FinsArea.TimerCounterPv, addr.Area);
        Assert.Equal(0, addr.Address);
    }

    // ==========================================================================
    // EM (Extended Memory)
    // ==========================================================================

    [Fact]
    public void Parse_E0_0_EmBank0()
    {
        var addr = FinsAddress.Parse("E0_0");
        Assert.Equal(0, addr.EmBank);
        Assert.Equal(0, addr.Address);
    }

    // ==========================================================================
    // Case Insensitivity
    // ==========================================================================

    [Fact]
    public void Parse_Lowercase_d100()
    {
        var addr = FinsAddress.Parse("d100");
        Assert.Equal(FinsArea.DmWord, addr.Area);
        Assert.Equal(100, addr.Address);
    }

    [Fact]
    public void Parse_Lowercase_cio0()
    {
        var addr = FinsAddress.Parse("cio0");
        Assert.Equal(FinsArea.CioWord, addr.Area);
    }

    // ==========================================================================
    // Error Cases
    // ==========================================================================

    [Fact]
    public void Parse_Empty_Throws()
    {
        Assert.Throws<FormatException>(() => FinsAddress.Parse(""));
    }

    [Fact]
    public void Parse_Null_Throws()
    {
        Assert.Throws<FormatException>(() => FinsAddress.Parse(null!));
    }

    [Fact]
    public void Parse_Invalid_Throws()
    {
        Assert.Throws<FormatException>(() => FinsAddress.Parse("INVALID"));
    }

    // ==========================================================================
    // TryParse
    // ==========================================================================

    [Fact]
    public void TryParse_Valid_ReturnsTrue()
    {
        Assert.True(FinsAddress.TryParse("D100", out var addr));
        Assert.Equal(100, addr.Address);
    }

    [Fact]
    public void TryParse_Invalid_ReturnsFalse()
    {
        Assert.False(FinsAddress.TryParse("INVALID", out _));
    }

    // ==========================================================================
    // ToString
    // ==========================================================================

    [Fact]
    public void ToString_DmWord()
    {
        var addr = FinsAddress.Parse("D100");
        Assert.Equal("D100", addr.ToString());
    }

    [Fact]
    public void ToString_CioBit()
    {
        var addr = FinsAddress.Parse("CIO0.05");
        Assert.Equal("CIO0.05", addr.ToString());
    }

    [Fact]
    public void ToString_WorkWord()
    {
        var addr = FinsAddress.Parse("W10");
        Assert.Equal("W10", addr.ToString());
    }
}
