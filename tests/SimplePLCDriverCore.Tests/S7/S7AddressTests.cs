using SimplePLCDriverCore.Protocols.S7;

namespace SimplePLCDriverCore.Tests.S7;

public class S7AddressTests
{
    // ==========================================================================
    // DB Addresses
    // ==========================================================================

    [Fact]
    public void Parse_DbBit_Correct()
    {
        var addr = S7Address.Parse("DB1.DBX0.0");
        Assert.Equal(S7Area.DataBlock, addr.Area);
        Assert.Equal(1, addr.DbNumber);
        Assert.Equal(0, addr.ByteOffset);
        Assert.Equal(0, addr.BitNumber);
        Assert.True(addr.IsBitAddress);
        Assert.Equal(S7TransportSize.Bit, addr.TransportSize);
    }

    [Fact]
    public void Parse_DbBit_Bit7()
    {
        var addr = S7Address.Parse("DB10.DBX5.7");
        Assert.Equal(10, addr.DbNumber);
        Assert.Equal(5, addr.ByteOffset);
        Assert.Equal(7, addr.BitNumber);
        Assert.True(addr.IsBitAddress);
    }

    [Fact]
    public void Parse_DbByte_Correct()
    {
        var addr = S7Address.Parse("DB1.DBB0");
        Assert.Equal(S7Area.DataBlock, addr.Area);
        Assert.Equal(1, addr.DbNumber);
        Assert.Equal(0, addr.ByteOffset);
        Assert.False(addr.IsBitAddress);
        Assert.Equal(S7TransportSize.Byte, addr.TransportSize);
        Assert.Equal(1, addr.DataLength);
    }

    [Fact]
    public void Parse_DbWord_Correct()
    {
        var addr = S7Address.Parse("DB1.DBW4");
        Assert.Equal(1, addr.DbNumber);
        Assert.Equal(4, addr.ByteOffset);
        Assert.Equal(S7TransportSize.Word, addr.TransportSize);
        Assert.Equal(2, addr.DataLength);
    }

    [Fact]
    public void Parse_DbDWord_Correct()
    {
        var addr = S7Address.Parse("DB1.DBD0");
        Assert.Equal(1, addr.DbNumber);
        Assert.Equal(0, addr.ByteOffset);
        Assert.Equal(S7TransportSize.DWord, addr.TransportSize);
        Assert.Equal(4, addr.DataLength);
    }

    [Fact]
    public void Parse_DbString_WithMaxLength()
    {
        var addr = S7Address.Parse("DB1.DBS10.20");
        Assert.Equal(1, addr.DbNumber);
        Assert.Equal(10, addr.ByteOffset);
        Assert.True(addr.IsString);
        Assert.Equal(20, addr.StringMaxLength);
        Assert.Equal(22, addr.DataLength); // maxlen + 2 header bytes
    }

    [Fact]
    public void Parse_DbString_NoMaxLength()
    {
        var addr = S7Address.Parse("DB1.DBS0");
        Assert.True(addr.IsString);
        Assert.Equal(254, addr.StringMaxLength);
    }

    // ==========================================================================
    // Direct Area Addresses
    // ==========================================================================

    [Fact]
    public void Parse_InputBit_Correct()
    {
        var addr = S7Address.Parse("I0.0");
        Assert.Equal(S7Area.ProcessInput, addr.Area);
        Assert.Equal(0, addr.ByteOffset);
        Assert.Equal(0, addr.BitNumber);
        Assert.True(addr.IsBitAddress);
    }

    [Fact]
    public void Parse_InputByte_Correct()
    {
        var addr = S7Address.Parse("IB0");
        Assert.Equal(S7Area.ProcessInput, addr.Area);
        Assert.Equal(0, addr.ByteOffset);
        Assert.False(addr.IsBitAddress);
        Assert.Equal(S7TransportSize.Byte, addr.TransportSize);
    }

    [Fact]
    public void Parse_InputWord_Correct()
    {
        var addr = S7Address.Parse("IW0");
        Assert.Equal(S7Area.ProcessInput, addr.Area);
        Assert.Equal(S7TransportSize.Word, addr.TransportSize);
    }

    [Fact]
    public void Parse_InputDWord_Correct()
    {
        var addr = S7Address.Parse("ID0");
        Assert.Equal(S7Area.ProcessInput, addr.Area);
        Assert.Equal(S7TransportSize.DWord, addr.TransportSize);
    }

    [Fact]
    public void Parse_OutputBit_Correct()
    {
        var addr = S7Address.Parse("Q0.5");
        Assert.Equal(S7Area.ProcessOutput, addr.Area);
        Assert.Equal(0, addr.ByteOffset);
        Assert.Equal(5, addr.BitNumber);
        Assert.True(addr.IsBitAddress);
    }

    [Fact]
    public void Parse_OutputWord_Correct()
    {
        var addr = S7Address.Parse("QW4");
        Assert.Equal(S7Area.ProcessOutput, addr.Area);
        Assert.Equal(4, addr.ByteOffset);
        Assert.Equal(S7TransportSize.Word, addr.TransportSize);
    }

    [Fact]
    public void Parse_MerkerBit_Correct()
    {
        var addr = S7Address.Parse("M0.0");
        Assert.Equal(S7Area.Merker, addr.Area);
        Assert.True(addr.IsBitAddress);
    }

    [Fact]
    public void Parse_MerkerByte_Correct()
    {
        var addr = S7Address.Parse("MB10");
        Assert.Equal(S7Area.Merker, addr.Area);
        Assert.Equal(10, addr.ByteOffset);
        Assert.Equal(S7TransportSize.Byte, addr.TransportSize);
    }

    [Fact]
    public void Parse_MerkerWord_Correct()
    {
        var addr = S7Address.Parse("MW100");
        Assert.Equal(S7Area.Merker, addr.Area);
        Assert.Equal(100, addr.ByteOffset);
        Assert.Equal(S7TransportSize.Word, addr.TransportSize);
    }

    [Fact]
    public void Parse_MerkerDWord_Correct()
    {
        var addr = S7Address.Parse("MD0");
        Assert.Equal(S7Area.Merker, addr.Area);
        Assert.Equal(S7TransportSize.DWord, addr.TransportSize);
    }

    // ==========================================================================
    // European Notation (E = Input, A = Output)
    // ==========================================================================

    [Fact]
    public void Parse_EuropeanInput_E()
    {
        var addr = S7Address.Parse("EB0");
        Assert.Equal(S7Area.ProcessInput, addr.Area);
    }

    [Fact]
    public void Parse_EuropeanOutput_A()
    {
        var addr = S7Address.Parse("AW4");
        Assert.Equal(S7Area.ProcessOutput, addr.Area);
    }

    // ==========================================================================
    // Timers and Counters
    // ==========================================================================

    [Fact]
    public void Parse_Timer_Correct()
    {
        var addr = S7Address.Parse("T0");
        Assert.Equal(S7Area.Timer, addr.Area);
        Assert.Equal(0, addr.ByteOffset);
        Assert.Equal(S7TransportSize.Timer, addr.TransportSize);
    }

    [Fact]
    public void Parse_Counter_Correct()
    {
        var addr = S7Address.Parse("C5");
        Assert.Equal(S7Area.Counter, addr.Area);
        Assert.Equal(5, addr.ByteOffset);
        Assert.Equal(S7TransportSize.Counter, addr.TransportSize);
    }

    // ==========================================================================
    // Bit Address Calculation
    // ==========================================================================

    [Fact]
    public void GetBitAddress_ByteAndBit_CalculatedCorrectly()
    {
        var addr = S7Address.Parse("DB1.DBX10.5");
        Assert.Equal(10 * 8 + 5, addr.GetBitAddress()); // 85
    }

    [Fact]
    public void GetBitAddress_WordNobit_ByteOffset()
    {
        var addr = S7Address.Parse("DB1.DBW4");
        Assert.Equal(4 * 8, addr.GetBitAddress()); // 32
    }

    // ==========================================================================
    // Case Insensitivity
    // ==========================================================================

    [Fact]
    public void Parse_CaseInsensitive_Lower()
    {
        var addr = S7Address.Parse("db1.dbw0");
        Assert.Equal(S7Area.DataBlock, addr.Area);
        Assert.Equal(1, addr.DbNumber);
    }

    [Fact]
    public void Parse_CaseInsensitive_Mixed()
    {
        var addr = S7Address.Parse("Db1.Dbx0.0");
        Assert.True(addr.IsBitAddress);
    }

    // ==========================================================================
    // ToString
    // ==========================================================================

    [Fact]
    public void ToString_DbBit() =>
        Assert.Equal("DB1.DBX0.0", S7Address.Parse("DB1.DBX0.0").ToString());

    [Fact]
    public void ToString_DbWord() =>
        Assert.Equal("DB1.DBW4", S7Address.Parse("DB1.DBW4").ToString());

    [Fact]
    public void ToString_DbDWord() =>
        Assert.Equal("DB1.DBD0", S7Address.Parse("DB1.DBD0").ToString());

    [Fact]
    public void ToString_MerkerBit() =>
        Assert.Equal("M0.0", S7Address.Parse("M0.0").ToString());

    [Fact]
    public void ToString_Timer() =>
        Assert.Equal("T0", S7Address.Parse("T0").ToString());

    [Fact]
    public void ToString_Counter() =>
        Assert.Equal("C5", S7Address.Parse("C5").ToString());

    // ==========================================================================
    // Error Cases
    // ==========================================================================

    [Fact]
    public void Parse_Empty_Throws()
    {
        Assert.Throws<FormatException>(() => S7Address.Parse(""));
    }

    [Fact]
    public void Parse_Invalid_Throws()
    {
        Assert.Throws<FormatException>(() => S7Address.Parse("INVALID"));
    }

    [Fact]
    public void TryParse_Invalid_ReturnsFalse()
    {
        Assert.False(S7Address.TryParse("INVALID", out _));
    }

    [Fact]
    public void TryParse_Valid_ReturnsTrue()
    {
        Assert.True(S7Address.TryParse("DB1.DBW0", out var addr));
        Assert.Equal(S7Area.DataBlock, addr.Area);
    }

    // ==========================================================================
    // Additional ToString Coverage
    // ==========================================================================

    [Fact]
    public void ToString_DbByte() =>
        Assert.Equal("DB1.DBB0", S7Address.Parse("DB1.DBB0").ToString());

    [Fact]
    public void ToString_DbString()
    {
        var str = S7Address.Parse("DB1.DBS10.20").ToString();
        Assert.Contains("DB1", str);
    }

    [Fact]
    public void ToString_InputBit() =>
        Assert.Equal("I0.0", S7Address.Parse("I0.0").ToString());

    [Fact]
    public void ToString_OutputWord() =>
        Assert.Equal("Q4.0", S7Address.Parse("Q4.0").ToString());

    [Fact]
    public void ToString_MerkerByte() =>
        Assert.Equal("MB10", S7Address.Parse("MB10").ToString());

    // ==========================================================================
    // Additional Direct Area Parsing
    // ==========================================================================

    [Fact]
    public void Parse_OutputByte()
    {
        var addr = S7Address.Parse("QB0");
        Assert.Equal(S7Area.ProcessOutput, addr.Area);
        Assert.Equal(S7TransportSize.Byte, addr.TransportSize);
    }

    [Fact]
    public void Parse_OutputDWord()
    {
        var addr = S7Address.Parse("QD0");
        Assert.Equal(S7Area.ProcessOutput, addr.Area);
        Assert.Equal(S7TransportSize.DWord, addr.TransportSize);
    }

    [Fact]
    public void Parse_EuropeanInputWord()
    {
        var addr = S7Address.Parse("EW0");
        Assert.Equal(S7Area.ProcessInput, addr.Area);
        Assert.Equal(S7TransportSize.Word, addr.TransportSize);
    }

    [Fact]
    public void Parse_EuropeanInputDWord()
    {
        var addr = S7Address.Parse("ED0");
        Assert.Equal(S7Area.ProcessInput, addr.Area);
        Assert.Equal(S7TransportSize.DWord, addr.TransportSize);
    }

    [Fact]
    public void Parse_EuropeanOutputByte()
    {
        var addr = S7Address.Parse("AB0");
        Assert.Equal(S7Area.ProcessOutput, addr.Area);
        Assert.Equal(S7TransportSize.Byte, addr.TransportSize);
    }

    [Fact]
    public void Parse_EuropeanOutputDWord()
    {
        var addr = S7Address.Parse("AD0");
        Assert.Equal(S7Area.ProcessOutput, addr.Area);
        Assert.Equal(S7TransportSize.DWord, addr.TransportSize);
    }

    [Fact]
    public void Parse_MerkerBit_HighBit()
    {
        var addr = S7Address.Parse("M100.7");
        Assert.Equal(S7Area.Merker, addr.Area);
        Assert.Equal(100, addr.ByteOffset);
        Assert.Equal(7, addr.BitNumber);
    }

    [Fact]
    public void Parse_HighTimerNumber()
    {
        var addr = S7Address.Parse("T255");
        Assert.Equal(S7Area.Timer, addr.Area);
        Assert.Equal(255, addr.ByteOffset);
    }

    [Fact]
    public void Parse_HighCounterNumber()
    {
        var addr = S7Address.Parse("C255");
        Assert.Equal(S7Area.Counter, addr.Area);
        Assert.Equal(255, addr.ByteOffset);
    }
}
