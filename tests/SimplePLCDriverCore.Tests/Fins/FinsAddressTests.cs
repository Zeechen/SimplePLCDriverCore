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

    // =====================================================================
    // Parse - all area types (word access)
    // =====================================================================

    [Theory]
    [InlineData("CIO50", (byte)0xB0, 50)]   // CioWord
    [InlineData("W100", (byte)0xB1, 100)]    // WorkWord
    [InlineData("H200", (byte)0xB2, 200)]    // HoldingWord
    [InlineData("D300", (byte)0x82, 300)]    // DmWord
    [InlineData("DM400", (byte)0x82, 400)]   // DmWord
    [InlineData("A500", (byte)0xB3, 500)]    // AuxiliaryWord
    [InlineData("T10", (byte)0x89, 10)]      // TimerCounterPv
    [InlineData("C20", (byte)0x89, 20)]      // TimerCounterPv
    public void Parse_WordAccess_AllAreas(string address, byte expectedAreaByte, int expectedWord)
    {
        var addr = FinsAddress.Parse(address);
        Assert.Equal((FinsArea)expectedAreaByte, addr.Area);
        Assert.Equal(expectedWord, addr.Address);
        Assert.False(addr.IsBitAddress);
        Assert.Equal(0, addr.BitNumber);
    }

    // =====================================================================
    // Parse - all area types (bit access)
    // =====================================================================

    [Theory]
    [InlineData("CIO10.03", (byte)0x30, 10, 3)]   // CioBit
    [InlineData("W20.07", (byte)0x31, 20, 7)]      // WorkBit
    [InlineData("H30.15", (byte)0x32, 30, 15)]     // HoldingBit
    [InlineData("D40.01", (byte)0x02, 40, 1)]      // DmBit
    [InlineData("DM50.02", (byte)0x02, 50, 2)]     // DmBit
    [InlineData("A60.08", (byte)0x33, 60, 8)]      // AuxiliaryBit
    [InlineData("T70.00", (byte)0x09, 70, 0)]      // TimerCounterStatus
    [InlineData("C80.05", (byte)0x09, 80, 5)]      // TimerCounterStatus
    public void Parse_BitAccess_AllAreas(string address, byte expectedAreaByte, int expectedWord, int expectedBit)
    {
        var addr = FinsAddress.Parse(address);
        Assert.Equal((FinsArea)expectedAreaByte, addr.Area);
        Assert.Equal(expectedWord, addr.Address);
        Assert.Equal(expectedBit, addr.BitNumber);
        Assert.True(addr.IsBitAddress);
    }

    // =====================================================================
    // Parse - EM bank variations
    // =====================================================================

    [Fact]
    public void Parse_E0_100_EmBank0Word100()
    {
        var addr = FinsAddress.Parse("E0_100");
        Assert.Equal(0, addr.EmBank);
        Assert.Equal(100, addr.Address);
        Assert.False(addr.IsBitAddress);
    }

    [Fact]
    public void Parse_E1_50_EmBank1()
    {
        var addr = FinsAddress.Parse("E1_50");
        Assert.Equal(1, addr.EmBank);
        Assert.Equal(50, addr.Address);
        Assert.Equal((FinsArea)(0x98 + 1), addr.Area);
    }

    [Fact]
    public void Parse_E2_0_EmBank2()
    {
        var addr = FinsAddress.Parse("E2_0");
        Assert.Equal(2, addr.EmBank);
        Assert.Equal(0, addr.Address);
    }

    [Fact]
    public void Parse_Em_BitAddress()
    {
        var addr = FinsAddress.Parse("E0_10.3");
        Assert.Equal(0, addr.EmBank);
        Assert.Equal(10, addr.Address);
        Assert.Equal(3, addr.BitNumber);
        Assert.True(addr.IsBitAddress);
    }

    // =====================================================================
    // Parse - whitespace trimming
    // =====================================================================

    [Fact]
    public void Parse_LeadingTrailingWhitespace_Trimmed()
    {
        var addr = FinsAddress.Parse("  D100  ");
        Assert.Equal(FinsArea.DmWord, addr.Area);
        Assert.Equal(100, addr.Address);
    }

    [Fact]
    public void Parse_WhitespaceOnly_Throws()
    {
        Assert.Throws<FormatException>(() => FinsAddress.Parse("   "));
    }

    // =====================================================================
    // Parse - error cases
    // =====================================================================

    [Theory]
    [InlineData("X100")]
    [InlineData("Z0")]
    [InlineData("GARBAGE")]
    [InlineData("123")]
    [InlineData("D")]
    [InlineData(".5")]
    public void Parse_InvalidFormats_ThrowFormatException(string address)
    {
        Assert.Throws<FormatException>(() => FinsAddress.Parse(address));
    }

    // =====================================================================
    // TryParse - additional cases
    // =====================================================================

    [Fact]
    public void TryParse_EmAddress_ReturnsTrue()
    {
        Assert.True(FinsAddress.TryParse("E0_100", out var addr));
        Assert.Equal(0, addr.EmBank);
        Assert.Equal(100, addr.Address);
    }

    [Fact]
    public void TryParse_BitAddress_ReturnsTrue()
    {
        Assert.True(FinsAddress.TryParse("CIO5.10", out var addr));
        Assert.True(addr.IsBitAddress);
        Assert.Equal(10, addr.BitNumber);
    }

    [Fact]
    public void TryParse_Empty_ReturnsFalse()
    {
        Assert.False(FinsAddress.TryParse("", out _));
    }

    [Fact]
    public void TryParse_Null_ReturnsFalse()
    {
        Assert.False(FinsAddress.TryParse(null!, out _));
    }

    // =====================================================================
    // ToString - all area types
    // =====================================================================

    [Theory]
    [InlineData("CIO100", "CIO100")]
    [InlineData("W50", "W50")]
    [InlineData("H200", "H200")]
    [InlineData("D300", "D300")]
    [InlineData("A400", "A400")]
    [InlineData("T10", "T10")]
    [InlineData("C20", "T20")]  // Counter PV uses same area as Timer, ToString outputs "T"
    public void ToString_WordAccess_AllAreas(string input, string expected)
    {
        var addr = FinsAddress.Parse(input);
        Assert.Equal(expected, addr.ToString());
    }

    [Theory]
    [InlineData("CIO10.05", "CIO10.05")]
    [InlineData("W20.03", "W20.03")]
    [InlineData("H30.15", "H30.15")]
    [InlineData("D40.01", "D40.01")]
    [InlineData("A60.08", "A60.08")]
    [InlineData("T70.00", "T70.00")]
    public void ToString_BitAccess_AllAreas(string input, string expected)
    {
        var addr = FinsAddress.Parse(input);
        Assert.Equal(expected, addr.ToString());
    }

    [Fact]
    public void ToString_EmBank_Word()
    {
        var addr = FinsAddress.Parse("E0_100");
        Assert.Equal("E0_100", addr.ToString());
    }

    [Fact]
    public void ToString_EmBank1_Word()
    {
        var addr = FinsAddress.Parse("E1_50");
        Assert.Equal("E1_50", addr.ToString());
    }

    [Fact]
    public void ToString_EmBank_Bit()
    {
        var addr = FinsAddress.Parse("E0_10.03");
        Assert.Equal("E0_10.03", addr.ToString());
    }

    [Fact]
    public void ToString_UnknownArea_ReturnsQuestionMark()
    {
        // Construct a FinsAddress with an area code that doesn't match any known area
        var addr = new FinsAddress((FinsArea)0x50, 10, 0, false);
        Assert.Equal("?10", addr.ToString());
    }

    [Fact]
    public void ToString_UnknownArea_BitAddress_ReturnsQuestionMark()
    {
        var addr = new FinsAddress((FinsArea)0x50, 10, 5, true);
        Assert.Equal("?10.05", addr.ToString());
    }

    // =====================================================================
    // Constructor - WordCount parameter
    // =====================================================================

    [Fact]
    public void Constructor_DefaultWordCount_IsOne()
    {
        var addr = new FinsAddress(FinsArea.DmWord, 100, 0, false);
        Assert.Equal(1, addr.WordCount);
    }

    [Fact]
    public void Constructor_CustomWordCount()
    {
        var addr = new FinsAddress(FinsArea.DmWord, 100, 0, false, wordCount: 10);
        Assert.Equal(10, addr.WordCount);
    }

    [Fact]
    public void Constructor_EmBank_Parameter()
    {
        var addr = new FinsAddress(FinsArea.EmWord0, 50, 0, false, 1, emBank: 3);
        Assert.Equal(3, addr.EmBank);
        Assert.Equal(FinsArea.EmWord0, addr.Area);
    }

    // =====================================================================
    // Parse - DM alternate notation with bit
    // =====================================================================

    [Fact]
    public void Parse_DM100_Bit_DmBit()
    {
        var addr = FinsAddress.Parse("DM100.7");
        Assert.Equal(FinsArea.DmBit, addr.Area);
        Assert.Equal(100, addr.Address);
        Assert.Equal(7, addr.BitNumber);
        Assert.True(addr.IsBitAddress);
    }

    // =====================================================================
    // Parse - mixed case variations
    // =====================================================================

    [Theory]
    [InlineData("cio100")]
    [InlineData("Cio100")]
    [InlineData("CIO100")]
    public void Parse_CIO_CaseVariations(string input)
    {
        var addr = FinsAddress.Parse(input);
        Assert.Equal(FinsArea.CioWord, addr.Area);
        Assert.Equal(100, addr.Address);
    }

    [Theory]
    [InlineData("w50")]
    [InlineData("W50")]
    public void Parse_W_CaseVariations(string input)
    {
        var addr = FinsAddress.Parse(input);
        Assert.Equal(FinsArea.WorkWord, addr.Area);
    }

    [Theory]
    [InlineData("h0")]
    [InlineData("H0")]
    public void Parse_H_CaseVariations(string input)
    {
        var addr = FinsAddress.Parse(input);
        Assert.Equal(FinsArea.HoldingWord, addr.Area);
    }

    [Theory]
    [InlineData("a0")]
    [InlineData("A0")]
    public void Parse_A_CaseVariations(string input)
    {
        var addr = FinsAddress.Parse(input);
        Assert.Equal(FinsArea.AuxiliaryWord, addr.Area);
    }

    [Theory]
    [InlineData("t0")]
    [InlineData("T0")]
    public void Parse_T_CaseVariations(string input)
    {
        var addr = FinsAddress.Parse(input);
        Assert.Equal(FinsArea.TimerCounterPv, addr.Area);
    }

    [Theory]
    [InlineData("c0")]
    [InlineData("C0")]
    public void Parse_C_CaseVariations(string input)
    {
        var addr = FinsAddress.Parse(input);
        Assert.Equal(FinsArea.TimerCounterPv, addr.Area);
    }

    // =====================================================================
    // Parse and ToString roundtrip
    // =====================================================================

    [Theory]
    [InlineData("CIO0")]
    [InlineData("W100")]
    [InlineData("H50")]
    [InlineData("D200")]
    [InlineData("A10")]
    [InlineData("T5")]
    [InlineData("CIO10.05")]
    [InlineData("W20.03")]
    [InlineData("H30.15")]
    [InlineData("D40.01")]
    [InlineData("A60.08")]
    public void Parse_ToString_Roundtrip(string input)
    {
        var addr = FinsAddress.Parse(input);
        Assert.Equal(input, addr.ToString());
    }

    // =====================================================================
    // Large address values
    // =====================================================================

    [Fact]
    public void Parse_LargeWordNumber()
    {
        var addr = FinsAddress.Parse("D9999");
        Assert.Equal(9999, addr.Address);
        Assert.Equal(FinsArea.DmWord, addr.Area);
    }

    [Fact]
    public void Parse_LargeBitNumber()
    {
        var addr = FinsAddress.Parse("D100.15");
        Assert.Equal(15, addr.BitNumber);
        Assert.True(addr.IsBitAddress);
    }
}
