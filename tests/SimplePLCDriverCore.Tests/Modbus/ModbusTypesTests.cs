using System.Buffers.Binary;
using SimplePLCDriverCore.Abstractions;
using SimplePLCDriverCore.Protocols.Modbus;

namespace SimplePLCDriverCore.Tests.Modbus;

public class ModbusTypesTests
{
    // ==========================================================================
    // DecodeValue - Coil / Discrete Input (bit registers)
    // ==========================================================================

    [Fact]
    public void DecodeValue_Coil_True()
    {
        var addr = new ModbusAddress(ModbusRegisterType.Coil, 0);
        // FC01 response: byte count=1, coil data=0x01
        var data = new byte[] { 0x01, 0x01 };
        var result = ModbusTypes.DecodeValue(data, addr);
        Assert.True(result.AsBoolean());
    }

    [Fact]
    public void DecodeValue_Coil_False()
    {
        var addr = new ModbusAddress(ModbusRegisterType.Coil, 0);
        var data = new byte[] { 0x01, 0x00 };
        var result = ModbusTypes.DecodeValue(data, addr);
        Assert.False(result.AsBoolean());
    }

    [Fact]
    public void DecodeValue_DiscreteInput_True()
    {
        var addr = new ModbusAddress(ModbusRegisterType.DiscreteInput, 0);
        var data = new byte[] { 0x01, 0x01 };
        var result = ModbusTypes.DecodeValue(data, addr);
        Assert.True(result.AsBoolean());
    }

    [Fact]
    public void DecodeValue_Coil_DataTooShort_Throws()
    {
        var addr = new ModbusAddress(ModbusRegisterType.Coil, 0);
        Assert.Throws<InvalidOperationException>(
            () => ModbusTypes.DecodeValue(new byte[] { 0x01 }, addr));
    }

    // ==========================================================================
    // DecodeValue - Holding/Input Register (word registers)
    // ==========================================================================

    [Fact]
    public void DecodeValue_HoldingRegister_Positive()
    {
        var addr = new ModbusAddress(ModbusRegisterType.HoldingRegister, 0);
        // FC03 response: byte count=2, value=42 (big-endian)
        var data = new byte[] { 0x02, 0x00, 0x2A };
        var result = ModbusTypes.DecodeValue(data, addr);
        Assert.Equal(42, result.AsInt32());
    }

    [Fact]
    public void DecodeValue_HoldingRegister_Negative()
    {
        var addr = new ModbusAddress(ModbusRegisterType.HoldingRegister, 0);
        var data = new byte[3];
        data[0] = 0x02;
        BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(1), -100);
        var result = ModbusTypes.DecodeValue(data, addr);
        Assert.Equal(-100, result.AsInt32());
    }

    [Fact]
    public void DecodeValue_InputRegister()
    {
        var addr = new ModbusAddress(ModbusRegisterType.InputRegister, 0);
        var data = new byte[] { 0x02, 0x03, 0xE8 }; // byte count=2, value=1000
        var result = ModbusTypes.DecodeValue(data, addr);
        Assert.Equal(1000, result.AsInt32());
    }

    [Fact]
    public void DecodeValue_Register_DataTooShort_Throws()
    {
        var addr = new ModbusAddress(ModbusRegisterType.HoldingRegister, 0);
        Assert.Throws<InvalidOperationException>(
            () => ModbusTypes.DecodeValue(new byte[] { 0x02, 0x00 }, addr));
    }

    // ==========================================================================
    // DecodeCoils
    // ==========================================================================

    [Fact]
    public void DecodeCoils_MultipleValues()
    {
        // 8 coils: byte count=1, data=0b10100101 = 0xA5
        var data = new byte[] { 0x01, 0xA5 };
        var result = ModbusTypes.DecodeCoils(data, 8);

        Assert.Equal(8, result.Length);
        Assert.True(result[0]);  // bit 0
        Assert.False(result[1]); // bit 1
        Assert.True(result[2]);  // bit 2
        Assert.False(result[3]); // bit 3
        Assert.False(result[4]); // bit 4
        Assert.True(result[5]);  // bit 5
        Assert.False(result[6]); // bit 6
        Assert.True(result[7]);  // bit 7
    }

    [Fact]
    public void DecodeCoils_TooShort_Throws()
    {
        Assert.Throws<InvalidOperationException>(
            () => ModbusTypes.DecodeCoils(new byte[] { 0x01 }, 8));
    }

    // ==========================================================================
    // DecodeRegisters
    // ==========================================================================

    [Fact]
    public void DecodeRegisters_MultipleValues()
    {
        // 2 registers: byte count=4, reg1=100, reg2=200
        var data = new byte[5];
        data[0] = 0x04;
        BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(1), 100);
        BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(3), 200);

        var result = ModbusTypes.DecodeRegisters(data, 2);
        Assert.Equal(2, result.Length);
        Assert.Equal(100, result[0]);
        Assert.Equal(200, result[1]);
    }

    [Fact]
    public void DecodeRegisters_TooShort_Throws()
    {
        Assert.Throws<InvalidOperationException>(
            () => ModbusTypes.DecodeRegisters(Array.Empty<byte>(), 1));
    }

    // ==========================================================================
    // DecodeDWord
    // ==========================================================================

    [Fact]
    public void DecodeDWord_BigEndian()
    {
        var data = new byte[5];
        data[0] = 0x04; // byte count
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(1), 100000);
        var result = ModbusTypes.DecodeDWord(data);
        Assert.Equal(100000, result.AsInt32());
    }

    [Fact]
    public void DecodeDWord_TooShort_Throws()
    {
        Assert.Throws<InvalidOperationException>(
            () => ModbusTypes.DecodeDWord(new byte[] { 0x02, 0x00, 0x00 }));
    }

    // ==========================================================================
    // DecodeFloat
    // ==========================================================================

    [Fact]
    public void DecodeFloat_BigEndian()
    {
        var data = new byte[5];
        data[0] = 0x04;
        BinaryPrimitives.WriteSingleBigEndian(data.AsSpan(1), 3.14f);
        var result = ModbusTypes.DecodeFloat(data);
        Assert.Equal(3.14f, result.AsSingle(), 0.001f);
    }

    [Fact]
    public void DecodeFloat_TooShort_Throws()
    {
        Assert.Throws<InvalidOperationException>(
            () => ModbusTypes.DecodeFloat(new byte[] { 0x02, 0x00, 0x00 }));
    }

    // ==========================================================================
    // EncodeRegister
    // ==========================================================================

    [Fact]
    public void EncodeRegister_Short()
    {
        Assert.Equal((ushort)42, ModbusTypes.EncodeRegister((short)42));
    }

    [Fact]
    public void EncodeRegister_UShort()
    {
        Assert.Equal((ushort)1000, ModbusTypes.EncodeRegister((ushort)1000));
    }

    [Fact]
    public void EncodeRegister_Int()
    {
        Assert.Equal((ushort)100, ModbusTypes.EncodeRegister(100));
    }

    [Fact]
    public void EncodeRegister_Byte()
    {
        Assert.Equal((ushort)255, ModbusTypes.EncodeRegister((byte)255));
    }

    [Fact]
    public void EncodeRegister_Bool_True()
    {
        Assert.Equal((ushort)0xFF00, ModbusTypes.EncodeRegister(true));
    }

    [Fact]
    public void EncodeRegister_Bool_False()
    {
        Assert.Equal((ushort)0x0000, ModbusTypes.EncodeRegister(false));
    }

    // ==========================================================================
    // EncodeDWord
    // ==========================================================================

    [Fact]
    public void EncodeDWord_TwoRegisters()
    {
        var regs = ModbusTypes.EncodeDWord(100000);
        Assert.Equal(2, regs.Length);
        // Reconstruct the int from two big-endian registers
        var bytes = new byte[4];
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(0), regs[0]);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(2), regs[1]);
        Assert.Equal(100000, BinaryPrimitives.ReadInt32BigEndian(bytes));
    }

    // ==========================================================================
    // EncodeFloat
    // ==========================================================================

    [Fact]
    public void EncodeFloat_TwoRegisters()
    {
        var regs = ModbusTypes.EncodeFloat(3.14f);
        Assert.Equal(2, regs.Length);
        var bytes = new byte[4];
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(0), regs[0]);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(2), regs[1]);
        Assert.Equal(3.14f, BinaryPrimitives.ReadSingleBigEndian(bytes), 0.001f);
    }

    // ==========================================================================
    // Type Names
    // ==========================================================================

    [Fact]
    public void GetTypeName_Coil()
    {
        var addr = new ModbusAddress(ModbusRegisterType.Coil, 0);
        Assert.Equal("COIL", ModbusTypes.GetTypeName(addr));
    }

    [Fact]
    public void GetTypeName_DiscreteInput()
    {
        var addr = new ModbusAddress(ModbusRegisterType.DiscreteInput, 0);
        Assert.Equal("DISCRETE_INPUT", ModbusTypes.GetTypeName(addr));
    }

    [Fact]
    public void GetTypeName_HoldingRegister()
    {
        var addr = new ModbusAddress(ModbusRegisterType.HoldingRegister, 0);
        Assert.Equal("HOLDING_REGISTER", ModbusTypes.GetTypeName(addr));
    }

    [Fact]
    public void GetTypeName_InputRegister()
    {
        var addr = new ModbusAddress(ModbusRegisterType.InputRegister, 0);
        Assert.Equal("INPUT_REGISTER", ModbusTypes.GetTypeName(addr));
    }

    // ==========================================================================
    // PlcDataType Mapping
    // ==========================================================================

    [Fact]
    public void ToPlcDataType_Coil_ReturnsBool()
    {
        var addr = new ModbusAddress(ModbusRegisterType.Coil, 0);
        Assert.Equal(PlcDataType.Bool, ModbusTypes.ToPlcDataType(addr));
    }

    [Fact]
    public void ToPlcDataType_HoldingRegister_ReturnsInt()
    {
        var addr = new ModbusAddress(ModbusRegisterType.HoldingRegister, 0);
        Assert.Equal(PlcDataType.Int, ModbusTypes.ToPlcDataType(addr));
    }
}
