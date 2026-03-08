using System.Text.RegularExpressions;

namespace SimplePLCDriverCore.Protocols.S7;

/// <summary>
/// S7 memory area codes.
/// </summary>
internal enum S7Area : byte
{
    /// <summary>Process inputs (I / PE).</summary>
    ProcessInput = 0x81,

    /// <summary>Process outputs (Q / PA).</summary>
    ProcessOutput = 0x82,

    /// <summary>Bit memory / markers (M).</summary>
    Merker = 0x83,

    /// <summary>Data block (DB).</summary>
    DataBlock = 0x84,

    /// <summary>Counter (C).</summary>
    Counter = 0x1C,

    /// <summary>Timer (T).</summary>
    Timer = 0x1D,
}

/// <summary>
/// S7 transport size codes for read/write requests.
/// </summary>
internal enum S7TransportSize : byte
{
    Bit = 0x01,
    Byte = 0x02,
    Char = 0x03,
    Word = 0x04,
    Int = 0x05,
    DWord = 0x06,
    DInt = 0x07,
    Real = 0x08,
    Counter = 0x1C,
    Timer = 0x1D,
}

/// <summary>
/// S7 return data transport size codes in read/write responses.
/// </summary>
internal enum S7DataTransportSize : byte
{
    Null = 0x00,
    BitAccess = 0x03,
    ByteWordDWord = 0x04,
    Integer = 0x05,
    Real = 0x07,
    OctetString = 0x09,
}

/// <summary>
/// Parsed S7 address for read/write operations.
///
/// Supported formats:
///   DB1.DBX0.0   - Data Block 1, bit at byte 0, bit 0
///   DB1.DBB0     - Data Block 1, byte at byte 0
///   DB1.DBW0     - Data Block 1, word at byte 0
///   DB1.DBD0     - Data Block 1, double word at byte 0
///   DB1.DBS0.10  - Data Block 1, string at byte 0, max length 10
///   I0.0         - Input byte 0, bit 0
///   IB0          - Input byte 0
///   IW0          - Input word at byte 0
///   ID0          - Input double word at byte 0
///   Q0.0         - Output byte 0, bit 0
///   QB0, QW0, QD0 - Output byte/word/dword
///   M0.0         - Merker bit at byte 0, bit 0
///   MB0, MW0, MD0 - Merker byte/word/dword
///   C0           - Counter 0
///   T0           - Timer 0
/// </summary>
internal readonly struct S7Address
{
    // DB pattern: DB{n}.DB{X|B|W|D|S}{offset}[.{bit|maxlen}]
    private static readonly Regex DbRegex = new(
        @"^DB(\d+)\.DB([XBWDS])(\d+)(?:\.(\d+))?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Direct area pattern: {I|Q|M|E|A}{X?}{offset}[.{bit}] or {I|Q|M|E|A}{B|W|D}{offset}
    private static readonly Regex AreaRegex = new(
        @"^([IQMEA])(X?)(\d+)(?:\.(\d+))?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Area with size: {I|Q|M|E|A}{B|W|D}{offset}
    private static readonly Regex AreaSizeRegex = new(
        @"^([IQMEA])([BWD])(\d+)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Timer/Counter: {T|C}{number}
    private static readonly Regex TimerCounterRegex = new(
        @"^([TC])(\d+)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public S7Area Area { get; }
    public int DbNumber { get; }
    public int ByteOffset { get; }
    public int BitNumber { get; }
    public S7TransportSize TransportSize { get; }
    public int DataLength { get; }
    public bool IsBitAddress { get; }
    public bool IsString { get; }
    public int StringMaxLength { get; }

    public S7Address(S7Area area, int dbNumber, int byteOffset, int bitNumber,
        S7TransportSize transportSize, int dataLength, bool isBitAddress,
        bool isString = false, int stringMaxLength = 0)
    {
        Area = area;
        DbNumber = dbNumber;
        ByteOffset = byteOffset;
        BitNumber = bitNumber;
        TransportSize = transportSize;
        DataLength = dataLength;
        IsBitAddress = isBitAddress;
        IsString = isString;
        StringMaxLength = stringMaxLength;
    }

    /// <summary>
    /// Get the bit address for the S7 protocol (byte_offset * 8 + bit_number).
    /// </summary>
    public int GetBitAddress() => ByteOffset * 8 + BitNumber;

    public static S7Address Parse(string address)
    {
        if (string.IsNullOrWhiteSpace(address))
            throw new FormatException("Address cannot be empty.");

        address = address.Trim();

        // Try DB address
        var m = DbRegex.Match(address);
        if (m.Success)
            return ParseDbAddress(m);

        // Try Timer/Counter
        m = TimerCounterRegex.Match(address);
        if (m.Success)
            return ParseTimerCounter(m);

        // Try area with size specifier (IB0, MW4, QD8, etc.)
        m = AreaSizeRegex.Match(address);
        if (m.Success)
            return ParseAreaSizeAddress(m);

        // Try direct area address (I0.0, M0.0, Q0.0, etc.)
        m = AreaRegex.Match(address);
        if (m.Success)
            return ParseAreaAddress(m);

        throw new FormatException($"Invalid S7 address: '{address}'");
    }

    public static bool TryParse(string address, out S7Address result)
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

    private static S7Address ParseDbAddress(Match m)
    {
        var dbNumber = int.Parse(m.Groups[1].Value);
        var sizeChar = char.ToUpper(m.Groups[2].Value[0]);
        var offset = int.Parse(m.Groups[3].Value);
        var extra = m.Groups[4].Success ? int.Parse(m.Groups[4].Value) : -1;

        return sizeChar switch
        {
            'X' => new S7Address(S7Area.DataBlock, dbNumber, offset,
                extra >= 0 ? extra : 0, S7TransportSize.Bit, 1, true),
            'B' => new S7Address(S7Area.DataBlock, dbNumber, offset,
                0, S7TransportSize.Byte, 1, false),
            'W' => new S7Address(S7Area.DataBlock, dbNumber, offset,
                0, S7TransportSize.Word, 2, false),
            'D' => new S7Address(S7Area.DataBlock, dbNumber, offset,
                0, S7TransportSize.DWord, 4, false),
            'S' => new S7Address(S7Area.DataBlock, dbNumber, offset,
                0, S7TransportSize.Byte, extra >= 0 ? extra + 2 : 256,
                false, isString: true, stringMaxLength: extra >= 0 ? extra : 254),
            _ => throw new FormatException($"Invalid DB size specifier: '{sizeChar}'")
        };
    }

    private static S7Address ParseAreaAddress(Match m)
    {
        var area = MapArea(m.Groups[1].Value[0]);
        var hasX = m.Groups[2].Success && m.Groups[2].Value.Length > 0;
        var offset = int.Parse(m.Groups[3].Value);
        var bit = m.Groups[4].Success ? int.Parse(m.Groups[4].Value) : -1;

        if (hasX || bit >= 0)
        {
            // Bit access: I0.0, IX0.0, M0.7
            return new S7Address(area, 0, offset,
                bit >= 0 ? bit : 0, S7TransportSize.Bit, 1, true);
        }

        // Without size specifier and without bit: treat as byte if no dot
        // I0 = byte, I0.0 = bit
        return new S7Address(area, 0, offset, 0, S7TransportSize.Byte, 1, false);
    }

    private static S7Address ParseAreaSizeAddress(Match m)
    {
        var area = MapArea(m.Groups[1].Value[0]);
        var sizeChar = char.ToUpper(m.Groups[2].Value[0]);
        var offset = int.Parse(m.Groups[3].Value);

        return sizeChar switch
        {
            'B' => new S7Address(area, 0, offset, 0, S7TransportSize.Byte, 1, false),
            'W' => new S7Address(area, 0, offset, 0, S7TransportSize.Word, 2, false),
            'D' => new S7Address(area, 0, offset, 0, S7TransportSize.DWord, 4, false),
            _ => throw new FormatException($"Invalid area size specifier: '{sizeChar}'")
        };
    }

    private static S7Address ParseTimerCounter(Match m)
    {
        var type = char.ToUpper(m.Groups[1].Value[0]);
        var number = int.Parse(m.Groups[2].Value);

        return type switch
        {
            'T' => new S7Address(S7Area.Timer, 0, number, 0,
                S7TransportSize.Timer, 2, false),
            'C' => new S7Address(S7Area.Counter, 0, number, 0,
                S7TransportSize.Counter, 2, false),
            _ => throw new FormatException($"Invalid timer/counter type: '{type}'")
        };
    }

    private static S7Area MapArea(char c) => char.ToUpper(c) switch
    {
        'I' or 'E' => S7Area.ProcessInput,
        'Q' or 'A' => S7Area.ProcessOutput,
        'M' => S7Area.Merker,
        _ => throw new FormatException($"Unknown area: '{c}'")
    };

    public override string ToString()
    {
        if (Area == S7Area.Timer)
            return $"T{ByteOffset}";
        if (Area == S7Area.Counter)
            return $"C{ByteOffset}";

        var areaPrefix = Area switch
        {
            S7Area.DataBlock => $"DB{DbNumber}.DB",
            S7Area.ProcessInput => "I",
            S7Area.ProcessOutput => "Q",
            S7Area.Merker => "M",
            _ => "?"
        };

        if (Area == S7Area.DataBlock)
        {
            if (IsString)
                return $"{areaPrefix}S{ByteOffset}.{StringMaxLength}";
            return TransportSize switch
            {
                S7TransportSize.Bit => $"{areaPrefix}X{ByteOffset}.{BitNumber}",
                S7TransportSize.Byte => $"{areaPrefix}B{ByteOffset}",
                S7TransportSize.Word or S7TransportSize.Int => $"{areaPrefix}W{ByteOffset}",
                S7TransportSize.DWord or S7TransportSize.DInt or S7TransportSize.Real => $"{areaPrefix}D{ByteOffset}",
                _ => $"{areaPrefix}B{ByteOffset}"
            };
        }

        if (IsBitAddress)
            return $"{areaPrefix}{ByteOffset}.{BitNumber}";

        return TransportSize switch
        {
            S7TransportSize.Byte => $"{areaPrefix}B{ByteOffset}",
            S7TransportSize.Word or S7TransportSize.Int => $"{areaPrefix}W{ByteOffset}",
            S7TransportSize.DWord or S7TransportSize.DInt or S7TransportSize.Real => $"{areaPrefix}D{ByteOffset}",
            _ => $"{areaPrefix}B{ByteOffset}"
        };
    }
}
