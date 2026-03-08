using System.Text.RegularExpressions;

namespace SimplePLCDriverCore.Protocols.Modbus;

/// <summary>
/// Modbus register types.
/// </summary>
internal enum ModbusRegisterType
{
    /// <summary>Coil (read/write, 1-bit). Function codes 01/05/15. Address range 0xxxx.</summary>
    Coil,

    /// <summary>Discrete Input (read-only, 1-bit). Function code 02. Address range 1xxxx.</summary>
    DiscreteInput,

    /// <summary>Input Register (read-only, 16-bit). Function code 04. Address range 3xxxx.</summary>
    InputRegister,

    /// <summary>Holding Register (read/write, 16-bit). Function codes 03/06/16. Address range 4xxxx.</summary>
    HoldingRegister,
}

/// <summary>
/// Parsed Modbus address for read/write operations.
///
/// Supported formats:
///   0xxxx or Cxxxx  - Coil (bit, read/write)
///   1xxxx or DIxxxx - Discrete Input (bit, read-only)
///   3xxxx or IRxxxx - Input Register (word, read-only)
///   4xxxx or HRxxxx - Holding Register (word, read/write)
///
///   Shorthand (without prefix, defaults to Holding Register):
///   100    - Holding Register 100
///
///   With explicit prefix:
///   HR100  - Holding Register 100
///   IR100  - Input Register 100
///   C100   - Coil 100
///   DI100  - Discrete Input 100
/// </summary>
internal readonly struct ModbusAddress
{
    private static readonly Regex PrefixRegex = new(
        @"^(HR|IR|DI|C)(\d+)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex NumericRegex = new(
        @"^(\d+)$",
        RegexOptions.Compiled);

    public ModbusRegisterType RegisterType { get; }
    public int Address { get; }
    public int Count { get; }

    public bool IsBitRegister => RegisterType is ModbusRegisterType.Coil
        or ModbusRegisterType.DiscreteInput;

    public ModbusAddress(ModbusRegisterType registerType, int address, int count = 1)
    {
        RegisterType = registerType;
        Address = address;
        Count = count;
    }

    public static ModbusAddress Parse(string address)
    {
        if (string.IsNullOrWhiteSpace(address))
            throw new FormatException("Address cannot be empty.");

        address = address.Trim();

        // Try prefix format: HR100, IR50, C0, DI0
        var m = PrefixRegex.Match(address);
        if (m.Success)
        {
            var prefix = m.Groups[1].Value.ToUpper();
            var num = int.Parse(m.Groups[2].Value);

            var regType = prefix switch
            {
                "HR" => ModbusRegisterType.HoldingRegister,
                "IR" => ModbusRegisterType.InputRegister,
                "C" => ModbusRegisterType.Coil,
                "DI" => ModbusRegisterType.DiscreteInput,
                _ => throw new FormatException($"Unknown Modbus prefix: '{prefix}'")
            };

            return new ModbusAddress(regType, num);
        }

        // Try numeric format with Modbus convention: 0xxxx, 1xxxx, 3xxxx, 4xxxx
        m = NumericRegex.Match(address);
        if (m.Success)
        {
            var num = int.Parse(m.Groups[1].Value);

            if (num >= 400001 && num <= 465536)
                return new ModbusAddress(ModbusRegisterType.HoldingRegister, num - 400001);
            if (num >= 300001 && num <= 365536)
                return new ModbusAddress(ModbusRegisterType.InputRegister, num - 300001);
            if (num >= 100001 && num <= 165536)
                return new ModbusAddress(ModbusRegisterType.DiscreteInput, num - 100001);
            if (num >= 1 && num <= 65536)
                return new ModbusAddress(ModbusRegisterType.Coil, num - 1);

            // Default: treat as holding register address (0-based)
            return new ModbusAddress(ModbusRegisterType.HoldingRegister, num);
        }

        throw new FormatException($"Invalid Modbus address: '{address}'");
    }

    public static bool TryParse(string address, out ModbusAddress result)
    {
        try
        {
            result = Parse(address);
            return true;
        }
        catch (FormatException)
        {
            result = default;
            return false;
        }
    }

    public override string ToString()
    {
        var prefix = RegisterType switch
        {
            ModbusRegisterType.Coil => "C",
            ModbusRegisterType.DiscreteInput => "DI",
            ModbusRegisterType.InputRegister => "IR",
            ModbusRegisterType.HoldingRegister => "HR",
            _ => "?"
        };
        return $"{prefix}{Address}";
    }
}
