using System.Text.RegularExpressions;

namespace SimplePLCDriverCore.Protocols.Fins;

/// <summary>
/// FINS memory area codes.
/// </summary>
internal enum FinsArea : byte
{
    /// <summary>CIO (Core I/O) area - bit access.</summary>
    CioBit = 0x30,

    /// <summary>CIO (Core I/O) area - word access.</summary>
    CioWord = 0xB0,

    /// <summary>Work area (W) - bit access.</summary>
    WorkBit = 0x31,

    /// <summary>Work area (W) - word access.</summary>
    WorkWord = 0xB1,

    /// <summary>Holding area (H) - bit access.</summary>
    HoldingBit = 0x32,

    /// <summary>Holding area (H) - word access.</summary>
    HoldingWord = 0xB2,

    /// <summary>Auxiliary area (A) - bit access.</summary>
    AuxiliaryBit = 0x33,

    /// <summary>Auxiliary area (A) - word access.</summary>
    AuxiliaryWord = 0xB3,

    /// <summary>DM (Data Memory) area - bit access.</summary>
    DmBit = 0x02,

    /// <summary>DM (Data Memory) area - word access.</summary>
    DmWord = 0x82,

    /// <summary>Timer/Counter PV (present value) - word access.</summary>
    TimerCounterPv = 0x89,

    /// <summary>Timer/Counter status (completion flag) - bit access.</summary>
    TimerCounterStatus = 0x09,

    /// <summary>EM (Extended Memory) area 0 - word access.</summary>
    EmWord0 = 0x98,
}

/// <summary>
/// Parsed FINS address for read/write operations.
///
/// Supported formats:
///   CIO0       - CIO area, word 0
///   CIO0.00    - CIO area, word 0, bit 0
///   W0         - Work area, word 0
///   W0.05      - Work area, word 0, bit 5
///   H0         - Holding area, word 0
///   D0         - DM area, word 0
///   D100       - DM area, word 100
///   D100.0     - DM area, word 100, bit 0 (bit access)
///   A0         - Auxiliary area, word 0
///   T0         - Timer PV, number 0
///   C0         - Counter PV, number 0
///   E0_0       - EM bank 0, word 0
/// </summary>
internal readonly struct FinsAddress
{
    private static readonly Regex AddressRegex = new(
        @"^(CIO|W|H|D|DM|A|T|C|E(\d+)_?)(\d+)(?:\.(\d+))?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public FinsArea Area { get; }
    public int Address { get; }
    public int BitNumber { get; }
    public bool IsBitAddress { get; }
    public int WordCount { get; }
    public int EmBank { get; }

    public FinsAddress(FinsArea area, int address, int bitNumber,
        bool isBitAddress, int wordCount = 1, int emBank = 0)
    {
        Area = area;
        Address = address;
        BitNumber = bitNumber;
        IsBitAddress = isBitAddress;
        WordCount = wordCount;
        EmBank = emBank;
    }

    public static FinsAddress Parse(string address)
    {
        if (string.IsNullOrWhiteSpace(address))
            throw new FormatException("Address cannot be empty.");

        address = address.Trim();

        var m = AddressRegex.Match(address);
        if (!m.Success)
            throw new FormatException($"Invalid FINS address: '{address}'");

        var areaStr = m.Groups[1].Value.ToUpper();
        var emBankStr = m.Groups[2].Value;
        var wordNum = int.Parse(m.Groups[3].Value);
        var hasBit = m.Groups[4].Success;
        var bitNum = hasBit ? int.Parse(m.Groups[4].Value) : 0;

        // EM bank handling
        if (areaStr.StartsWith("E") && !areaStr.StartsWith("E_"))
        {
            var emBank = emBankStr.Length > 0 ? int.Parse(emBankStr) : 0;
            return new FinsAddress(
                (FinsArea)(0x98 + emBank), wordNum, bitNum,
                hasBit, 1, emBank);
        }

        var (wordArea, bitArea) = areaStr switch
        {
            "CIO" => (FinsArea.CioWord, FinsArea.CioBit),
            "W" => (FinsArea.WorkWord, FinsArea.WorkBit),
            "H" => (FinsArea.HoldingWord, FinsArea.HoldingBit),
            "D" or "DM" => (FinsArea.DmWord, FinsArea.DmBit),
            "A" => (FinsArea.AuxiliaryWord, FinsArea.AuxiliaryBit),
            "T" => (FinsArea.TimerCounterPv, FinsArea.TimerCounterStatus),
            "C" => (FinsArea.TimerCounterPv, FinsArea.TimerCounterStatus),
            _ => throw new FormatException($"Unknown FINS area: '{areaStr}'")
        };

        if (hasBit)
            return new FinsAddress(bitArea, wordNum, bitNum, true);

        return new FinsAddress(wordArea, wordNum, 0, false);
    }

    public static bool TryParse(string address, out FinsAddress result)
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
        var prefix = Area switch
        {
            FinsArea.CioWord or FinsArea.CioBit => "CIO",
            FinsArea.WorkWord or FinsArea.WorkBit => "W",
            FinsArea.HoldingWord or FinsArea.HoldingBit => "H",
            FinsArea.DmWord or FinsArea.DmBit => "D",
            FinsArea.AuxiliaryWord or FinsArea.AuxiliaryBit => "A",
            FinsArea.TimerCounterPv or FinsArea.TimerCounterStatus => "T",
            _ when (byte)Area >= 0x98 => $"E{EmBank}_",
            _ => "?"
        };

        return IsBitAddress
            ? $"{prefix}{Address}.{BitNumber:D2}"
            : $"{prefix}{Address}";
    }
}
