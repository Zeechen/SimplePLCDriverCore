using SimplePLCDriverCore.Protocols.EtherNetIP.Pccc;

namespace SimplePLCDriverCore.Tests.EtherNetIP.Pccc;

public class PcccAddressTests
{
    // ==========================================================================
    // Basic Address Parsing
    // ==========================================================================

    [Fact]
    public void Parse_IntegerFile_N7_0()
    {
        var addr = PcccAddress.Parse("N7:0");

        Assert.Equal("N", addr.FileType);
        Assert.Equal(7, addr.FileNumber);
        Assert.Equal(0, addr.Element);
        Assert.Equal(-1, addr.SubElement);
        Assert.Equal(-1, addr.BitNumber);
        Assert.Equal(PcccFileType.Integer, addr.PcccFileType);
        Assert.False(addr.IsBitAddress);
        Assert.False(addr.HasSubElement);
    }

    [Fact]
    public void Parse_IntegerFile_N7_42()
    {
        var addr = PcccAddress.Parse("N7:42");

        Assert.Equal("N", addr.FileType);
        Assert.Equal(7, addr.FileNumber);
        Assert.Equal(42, addr.Element);
        Assert.Equal(PcccFileType.Integer, addr.PcccFileType);
    }

    [Fact]
    public void Parse_FloatFile_F8_1()
    {
        var addr = PcccAddress.Parse("F8:1");

        Assert.Equal("F", addr.FileType);
        Assert.Equal(8, addr.FileNumber);
        Assert.Equal(1, addr.Element);
        Assert.Equal(PcccFileType.Float, addr.PcccFileType);
    }

    [Fact]
    public void Parse_BitFile_B3_0_Bit5()
    {
        var addr = PcccAddress.Parse("B3:0/5");

        Assert.Equal("B", addr.FileType);
        Assert.Equal(3, addr.FileNumber);
        Assert.Equal(0, addr.Element);
        Assert.Equal(-1, addr.SubElement);
        Assert.Equal(5, addr.BitNumber);
        Assert.Equal(PcccFileType.Bit, addr.PcccFileType);
        Assert.True(addr.IsBitAddress);
    }

    [Fact]
    public void Parse_OutputFile_O0_3()
    {
        var addr = PcccAddress.Parse("O:0/3");

        Assert.Equal("O", addr.FileType);
        Assert.Equal(0, addr.FileNumber);
        Assert.Equal(0, addr.Element);
        Assert.Equal(3, addr.BitNumber);
        Assert.Equal(PcccFileType.Output, addr.PcccFileType);
        Assert.True(addr.IsBitAddress);
    }

    [Fact]
    public void Parse_InputFile_I1_4()
    {
        var addr = PcccAddress.Parse("I:0/4");

        Assert.Equal("I", addr.FileType);
        Assert.Equal(1, addr.FileNumber);
        Assert.Equal(0, addr.Element);
        Assert.Equal(4, addr.BitNumber);
        Assert.Equal(PcccFileType.Input, addr.PcccFileType);
    }

    [Fact]
    public void Parse_StatusFile()
    {
        var addr = PcccAddress.Parse("S:1/5");

        Assert.Equal("S", addr.FileType);
        Assert.Equal(2, addr.FileNumber); // default file number for S
        Assert.Equal(1, addr.Element);
        Assert.Equal(5, addr.BitNumber);
        Assert.Equal(PcccFileType.Status, addr.PcccFileType);
    }

    // ==========================================================================
    // Timer/Counter/Control Sub-Elements
    // ==========================================================================

    [Fact]
    public void Parse_Timer_Accumulator()
    {
        var addr = PcccAddress.Parse("T4:0.ACC");

        Assert.Equal("T", addr.FileType);
        Assert.Equal(4, addr.FileNumber);
        Assert.Equal(0, addr.Element);
        Assert.Equal(2, addr.SubElement); // ACC is word 2
        Assert.Equal(-1, addr.BitNumber);
        Assert.Equal(PcccFileType.Timer, addr.PcccFileType);
        Assert.True(addr.HasSubElement);
    }

    [Fact]
    public void Parse_Timer_Preset()
    {
        var addr = PcccAddress.Parse("T4:0.PRE");

        Assert.Equal(1, addr.SubElement); // PRE is word 1
    }

    [Fact]
    public void Parse_Timer_EnableBit()
    {
        var addr = PcccAddress.Parse("T4:0.EN");

        Assert.Equal(0, addr.SubElement);  // Control word 0
        Assert.Equal(15, addr.BitNumber);   // EN is bit 15
        Assert.True(addr.IsBitAddress);
    }

    [Fact]
    public void Parse_Timer_DoneBit()
    {
        var addr = PcccAddress.Parse("T4:0.DN");

        Assert.Equal(0, addr.SubElement);
        Assert.Equal(13, addr.BitNumber); // DN is bit 13
    }

    [Fact]
    public void Parse_Timer_TimingBit()
    {
        var addr = PcccAddress.Parse("T4:0.TT");

        Assert.Equal(0, addr.SubElement);
        Assert.Equal(14, addr.BitNumber); // TT is bit 14
    }

    [Fact]
    public void Parse_Counter_Accumulator()
    {
        var addr = PcccAddress.Parse("C5:0.ACC");

        Assert.Equal("C", addr.FileType);
        Assert.Equal(5, addr.FileNumber);
        Assert.Equal(0, addr.Element);
        Assert.Equal(2, addr.SubElement);
        Assert.Equal(PcccFileType.Counter, addr.PcccFileType);
    }

    [Fact]
    public void Parse_Counter_DoneBit()
    {
        var addr = PcccAddress.Parse("C5:2.DN");

        Assert.Equal(2, addr.Element);
        Assert.Equal(0, addr.SubElement);
        Assert.Equal(13, addr.BitNumber);
    }

    [Fact]
    public void Parse_Counter_CountUpBit()
    {
        var addr = PcccAddress.Parse("C5:0.CU");

        Assert.Equal(0, addr.SubElement);
        Assert.Equal(15, addr.BitNumber);
    }

    [Fact]
    public void Parse_Counter_OverflowBit()
    {
        var addr = PcccAddress.Parse("C5:0.OV");

        Assert.Equal(0, addr.SubElement);
        Assert.Equal(12, addr.BitNumber);
    }

    [Fact]
    public void Parse_Control_Length()
    {
        var addr = PcccAddress.Parse("R6:0.LEN");

        Assert.Equal("R", addr.FileType);
        Assert.Equal(6, addr.FileNumber);
        Assert.Equal(0, addr.Element);
        Assert.Equal(1, addr.SubElement); // LEN is word 1
        Assert.Equal(PcccFileType.Control, addr.PcccFileType);
    }

    [Fact]
    public void Parse_Control_Position()
    {
        var addr = PcccAddress.Parse("R6:0.POS");

        Assert.Equal(2, addr.SubElement); // POS is word 2
    }

    [Fact]
    public void Parse_Control_DoneBit()
    {
        var addr = PcccAddress.Parse("R6:0.DN");

        Assert.Equal(0, addr.SubElement);
        Assert.Equal(13, addr.BitNumber);
    }

    // ==========================================================================
    // String and Long Types
    // ==========================================================================

    [Fact]
    public void Parse_StringFile()
    {
        var addr = PcccAddress.Parse("ST9:0");

        Assert.Equal("ST", addr.FileType);
        Assert.Equal(9, addr.FileNumber);
        Assert.Equal(0, addr.Element);
        Assert.Equal(PcccFileType.String, addr.PcccFileType);
    }

    [Fact]
    public void Parse_LongFile()
    {
        var addr = PcccAddress.Parse("L10:5");

        Assert.Equal("L", addr.FileType);
        Assert.Equal(10, addr.FileNumber);
        Assert.Equal(5, addr.Element);
        Assert.Equal(PcccFileType.Long, addr.PcccFileType);
    }

    [Fact]
    public void Parse_AsciiFile()
    {
        var addr = PcccAddress.Parse("A9:0");

        Assert.Equal("A", addr.FileType);
        Assert.Equal(9, addr.FileNumber);
        Assert.Equal(0, addr.Element);
        Assert.Equal(PcccFileType.Ascii, addr.PcccFileType);
    }

    // ==========================================================================
    // Default File Numbers
    // ==========================================================================

    [Theory]
    [InlineData("O:0", 0)]
    [InlineData("I:0", 1)]
    [InlineData("S:0", 2)]
    [InlineData("B:0", 3)]
    [InlineData("T:0", 4)]
    [InlineData("C:0", 5)]
    [InlineData("R:0", 6)]
    [InlineData("N:0", 7)]
    [InlineData("F:0", 8)]
    public void Parse_DefaultFileNumbers(string address, int expectedFileNumber)
    {
        var addr = PcccAddress.Parse(address);
        Assert.Equal(expectedFileNumber, addr.FileNumber);
    }

    // ==========================================================================
    // Case Insensitivity
    // ==========================================================================

    [Fact]
    public void Parse_CaseInsensitive()
    {
        var lower = PcccAddress.Parse("n7:0");
        var upper = PcccAddress.Parse("N7:0");

        Assert.Equal(upper.FileType, lower.FileType);
        Assert.Equal(upper.FileNumber, lower.FileNumber);
        Assert.Equal(upper.Element, lower.Element);
        Assert.Equal(upper.PcccFileType, lower.PcccFileType);
    }

    // ==========================================================================
    // Byte Offset and Read Size Calculations
    // ==========================================================================

    [Fact]
    public void GetByteOffset_Integer_Element0()
    {
        var addr = PcccAddress.Parse("N7:0");
        Assert.Equal(0, addr.GetByteOffset());
    }

    [Fact]
    public void GetByteOffset_Integer_Element5()
    {
        var addr = PcccAddress.Parse("N7:5");
        Assert.Equal(10, addr.GetByteOffset()); // 5 * 2 bytes
    }

    [Fact]
    public void GetByteOffset_Float_Element3()
    {
        var addr = PcccAddress.Parse("F8:3");
        Assert.Equal(12, addr.GetByteOffset()); // 3 * 4 bytes
    }

    [Fact]
    public void GetByteOffset_Timer_ACC()
    {
        var addr = PcccAddress.Parse("T4:0.ACC");
        // Timer element 0: offset = 0 * 6 = 0
        // Sub-element ACC (word 2): offset += 2 * 2 = 4
        Assert.Equal(4, addr.GetByteOffset());
    }

    [Fact]
    public void GetByteOffset_Timer_Element1_PRE()
    {
        var addr = PcccAddress.Parse("T4:1.PRE");
        // Timer element 1: offset = 1 * 6 = 6
        // Sub-element PRE (word 1): offset += 1 * 2 = 2
        Assert.Equal(8, addr.GetByteOffset());
    }

    [Fact]
    public void GetReadSize_Integer()
    {
        var addr = PcccAddress.Parse("N7:0");
        Assert.Equal(2, addr.GetReadSize());
    }

    [Fact]
    public void GetReadSize_Float()
    {
        var addr = PcccAddress.Parse("F8:0");
        Assert.Equal(4, addr.GetReadSize());
    }

    [Fact]
    public void GetReadSize_Timer_Full()
    {
        var addr = PcccAddress.Parse("T4:0");
        Assert.Equal(6, addr.GetReadSize()); // Full timer element
    }

    [Fact]
    public void GetReadSize_Timer_SubElement()
    {
        var addr = PcccAddress.Parse("T4:0.ACC");
        Assert.Equal(2, addr.GetReadSize()); // Sub-element is word-sized
    }

    [Fact]
    public void GetReadSize_BitAddress()
    {
        var addr = PcccAddress.Parse("B3:0/5");
        Assert.Equal(2, addr.GetReadSize()); // Read the word containing the bit
    }

    [Fact]
    public void GetReadSize_String()
    {
        var addr = PcccAddress.Parse("ST9:0");
        Assert.Equal(84, addr.GetReadSize());
    }

    // ==========================================================================
    // Error Cases
    // ==========================================================================

    [Fact]
    public void Parse_EmptyString_ThrowsFormatException()
    {
        Assert.Throws<FormatException>(() => PcccAddress.Parse(""));
    }

    [Fact]
    public void Parse_InvalidPrefix_ThrowsFormatException()
    {
        Assert.Throws<FormatException>(() => PcccAddress.Parse("X7:0"));
    }

    [Fact]
    public void Parse_NoColonForSTWithoutNumber_ThrowsFormatException()
    {
        Assert.Throws<FormatException>(() => PcccAddress.Parse("ST:0"));
    }

    // ==========================================================================
    // TryParse
    // ==========================================================================

    [Fact]
    public void TryParse_ValidAddress_ReturnsTrue()
    {
        Assert.True(PcccAddress.TryParse("N7:0", out var addr));
        Assert.Equal(7, addr.FileNumber);
    }

    [Fact]
    public void TryParse_InvalidAddress_ReturnsFalse()
    {
        Assert.False(PcccAddress.TryParse("INVALID", out _));
    }

    // ==========================================================================
    // ToString
    // ==========================================================================

    [Fact]
    public void ToString_IntegerAddress()
    {
        var addr = PcccAddress.Parse("N7:0");
        Assert.Equal("N7:0", addr.ToString());
    }

    [Fact]
    public void ToString_BitAddress()
    {
        var addr = PcccAddress.Parse("B3:0/5");
        Assert.Equal("B3:0/5", addr.ToString());
    }

    [Fact]
    public void ToString_SubElement()
    {
        var addr = PcccAddress.Parse("T4:0.ACC");
        Assert.Equal("T4:0.2", addr.ToString()); // ACC is sub-element 2
    }
}
