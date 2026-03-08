using SimplePLCDriverCore.Protocols.Modbus;

namespace SimplePLCDriverCore.Tests.Modbus;

public class ModbusAddressTests
{
    // ==========================================================================
    // Prefix Format: HR (Holding Register)
    // ==========================================================================

    [Fact]
    public void Parse_HR100_HoldingRegister()
    {
        var addr = ModbusAddress.Parse("HR100");
        Assert.Equal(ModbusRegisterType.HoldingRegister, addr.RegisterType);
        Assert.Equal(100, addr.Address);
        Assert.False(addr.IsBitRegister);
    }

    [Fact]
    public void Parse_HR0_HoldingRegister()
    {
        var addr = ModbusAddress.Parse("HR0");
        Assert.Equal(ModbusRegisterType.HoldingRegister, addr.RegisterType);
        Assert.Equal(0, addr.Address);
    }

    // ==========================================================================
    // Prefix Format: IR (Input Register)
    // ==========================================================================

    [Fact]
    public void Parse_IR50_InputRegister()
    {
        var addr = ModbusAddress.Parse("IR50");
        Assert.Equal(ModbusRegisterType.InputRegister, addr.RegisterType);
        Assert.Equal(50, addr.Address);
        Assert.False(addr.IsBitRegister);
    }

    // ==========================================================================
    // Prefix Format: C (Coil)
    // ==========================================================================

    [Fact]
    public void Parse_C0_Coil()
    {
        var addr = ModbusAddress.Parse("C0");
        Assert.Equal(ModbusRegisterType.Coil, addr.RegisterType);
        Assert.Equal(0, addr.Address);
        Assert.True(addr.IsBitRegister);
    }

    [Fact]
    public void Parse_C100_Coil()
    {
        var addr = ModbusAddress.Parse("C100");
        Assert.Equal(ModbusRegisterType.Coil, addr.RegisterType);
        Assert.Equal(100, addr.Address);
    }

    // ==========================================================================
    // Prefix Format: DI (Discrete Input)
    // ==========================================================================

    [Fact]
    public void Parse_DI0_DiscreteInput()
    {
        var addr = ModbusAddress.Parse("DI0");
        Assert.Equal(ModbusRegisterType.DiscreteInput, addr.RegisterType);
        Assert.Equal(0, addr.Address);
        Assert.True(addr.IsBitRegister);
    }

    [Fact]
    public void Parse_DI200_DiscreteInput()
    {
        var addr = ModbusAddress.Parse("DI200");
        Assert.Equal(ModbusRegisterType.DiscreteInput, addr.RegisterType);
        Assert.Equal(200, addr.Address);
    }

    // ==========================================================================
    // Classic Modbus Numeric Format
    // ==========================================================================

    [Fact]
    public void Parse_400001_HoldingRegister0()
    {
        var addr = ModbusAddress.Parse("400001");
        Assert.Equal(ModbusRegisterType.HoldingRegister, addr.RegisterType);
        Assert.Equal(0, addr.Address);
    }

    [Fact]
    public void Parse_400101_HoldingRegister100()
    {
        var addr = ModbusAddress.Parse("400101");
        Assert.Equal(ModbusRegisterType.HoldingRegister, addr.RegisterType);
        Assert.Equal(100, addr.Address);
    }

    [Fact]
    public void Parse_300001_InputRegister0()
    {
        var addr = ModbusAddress.Parse("300001");
        Assert.Equal(ModbusRegisterType.InputRegister, addr.RegisterType);
        Assert.Equal(0, addr.Address);
    }

    [Fact]
    public void Parse_100001_DiscreteInput0()
    {
        var addr = ModbusAddress.Parse("100001");
        Assert.Equal(ModbusRegisterType.DiscreteInput, addr.RegisterType);
        Assert.Equal(0, addr.Address);
    }

    [Fact]
    public void Parse_1_Coil0()
    {
        var addr = ModbusAddress.Parse("1");
        Assert.Equal(ModbusRegisterType.Coil, addr.RegisterType);
        Assert.Equal(0, addr.Address);
    }

    [Fact]
    public void Parse_101_Coil100()
    {
        var addr = ModbusAddress.Parse("101");
        Assert.Equal(ModbusRegisterType.Coil, addr.RegisterType);
        Assert.Equal(100, addr.Address);
    }

    // ==========================================================================
    // Case Insensitivity
    // ==========================================================================

    [Fact]
    public void Parse_Lowercase_hr100()
    {
        var addr = ModbusAddress.Parse("hr100");
        Assert.Equal(ModbusRegisterType.HoldingRegister, addr.RegisterType);
        Assert.Equal(100, addr.Address);
    }

    [Fact]
    public void Parse_MixedCase_Hr100()
    {
        var addr = ModbusAddress.Parse("Hr100");
        Assert.Equal(ModbusRegisterType.HoldingRegister, addr.RegisterType);
    }

    [Fact]
    public void Parse_Lowercase_di50()
    {
        var addr = ModbusAddress.Parse("di50");
        Assert.Equal(ModbusRegisterType.DiscreteInput, addr.RegisterType);
        Assert.Equal(50, addr.Address);
    }

    // ==========================================================================
    // Error Cases
    // ==========================================================================

    [Fact]
    public void Parse_Empty_Throws()
    {
        Assert.Throws<FormatException>(() => ModbusAddress.Parse(""));
    }

    [Fact]
    public void Parse_Whitespace_Throws()
    {
        Assert.Throws<FormatException>(() => ModbusAddress.Parse("   "));
    }

    [Fact]
    public void Parse_Invalid_Throws()
    {
        Assert.Throws<FormatException>(() => ModbusAddress.Parse("INVALID"));
    }

    // ==========================================================================
    // TryParse
    // ==========================================================================

    [Fact]
    public void TryParse_Valid_ReturnsTrue()
    {
        Assert.True(ModbusAddress.TryParse("HR100", out var addr));
        Assert.Equal(ModbusRegisterType.HoldingRegister, addr.RegisterType);
        Assert.Equal(100, addr.Address);
    }

    [Fact]
    public void TryParse_Invalid_ReturnsFalse()
    {
        Assert.False(ModbusAddress.TryParse("INVALID", out _));
    }

    // ==========================================================================
    // ToString
    // ==========================================================================

    [Fact]
    public void ToString_HoldingRegister()
    {
        var addr = ModbusAddress.Parse("HR100");
        Assert.Equal("HR100", addr.ToString());
    }

    [Fact]
    public void ToString_Coil()
    {
        var addr = ModbusAddress.Parse("C50");
        Assert.Equal("C50", addr.ToString());
    }

    [Fact]
    public void ToString_InputRegister()
    {
        var addr = ModbusAddress.Parse("IR200");
        Assert.Equal("IR200", addr.ToString());
    }

    [Fact]
    public void ToString_DiscreteInput()
    {
        var addr = ModbusAddress.Parse("DI10");
        Assert.Equal("DI10", addr.ToString());
    }

    // ==========================================================================
    // IsBitRegister
    // ==========================================================================

    [Fact]
    public void IsBitRegister_Coil_True()
    {
        Assert.True(ModbusAddress.Parse("C0").IsBitRegister);
    }

    [Fact]
    public void IsBitRegister_DiscreteInput_True()
    {
        Assert.True(ModbusAddress.Parse("DI0").IsBitRegister);
    }

    [Fact]
    public void IsBitRegister_HoldingRegister_False()
    {
        Assert.False(ModbusAddress.Parse("HR0").IsBitRegister);
    }

    [Fact]
    public void IsBitRegister_InputRegister_False()
    {
        Assert.False(ModbusAddress.Parse("IR0").IsBitRegister);
    }
}
