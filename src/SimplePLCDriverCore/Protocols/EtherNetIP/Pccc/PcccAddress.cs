using System.Text.RegularExpressions;

namespace SimplePLCDriverCore.Protocols.EtherNetIP.Pccc;

/// <summary>
/// Parsed SLC/MicroLogix/PLC-5 file-based address.
///
/// SLC addressing format: FileType + FileNumber : Element [. SubElement | /BitNumber]
///
/// Examples:
///   N7:0      - Integer file 7, element 0
///   F8:1      - Float file 8, element 1
///   B3:0/5    - Bit file 3, element 0, bit 5
///   B3/5      - Bit file 3, element 0, bit 5 (short form)
///   T4:0.ACC  - Timer file 4, element 0, accumulator sub-element
///   T4:0.PRE  - Timer file 4, element 0, preset sub-element
///   T4:0.EN   - Timer file 4, element 0, enable bit
///   C5:0.ACC  - Counter file 5, element 0, accumulator
///   C5:0.PRE  - Counter file 5, element 0, preset
///   ST9:0     - String file 9, element 0
///   S:1/5     - Status file, word 1, bit 5
///   O:0/3     - Output file, word 0, bit 3
///   I:0/4     - Input file, word 0, bit 4
///   L9:0      - Long integer file 9, element 0  (SLC 5/05, MicroLogix)
///   R6:0.LEN  - Control file 6, element 0, length sub-element
///   A9:0      - ASCII file (MicroLogix)
/// </summary>
internal readonly struct PcccAddress
{
    /// <summary>File type character (N, F, B, T, C, S, O, I, ST, L, R, A).</summary>
    public string FileType { get; }

    /// <summary>File number (e.g., 7 in N7:0).</summary>
    public int FileNumber { get; }

    /// <summary>Element number (e.g., 0 in N7:0).</summary>
    public int Element { get; }

    /// <summary>Sub-element index (for Timer/Counter: 0=control, 1=PRE, 2=ACC). -1 if none.</summary>
    public int SubElement { get; }

    /// <summary>Bit number for bit-level access. -1 if not bit-addressed.</summary>
    public int BitNumber { get; }

    /// <summary>The PCCC file type code for protocol encoding.</summary>
    public PcccFileType PcccFileType { get; }

    /// <summary>Whether this address includes bit-level access.</summary>
    public bool IsBitAddress => BitNumber >= 0;

    /// <summary>Whether this address accesses a sub-element (timer/counter/control fields).</summary>
    public bool HasSubElement => SubElement >= 0;

    public PcccAddress(string fileType, int fileNumber, int element,
        int subElement, int bitNumber, PcccFileType pcccFileType)
    {
        FileType = fileType;
        FileNumber = fileNumber;
        Element = element;
        SubElement = subElement;
        BitNumber = bitNumber;
        PcccFileType = pcccFileType;
    }

    /// <summary>
    /// Calculate the byte offset within the data table file for this address.
    /// Used to build the PCCC read/write request.
    /// </summary>
    public int GetByteOffset()
    {
        var elementSize = PcccTypes.GetElementSize(PcccFileType);
        var offset = Element * elementSize;

        if (SubElement >= 0)
            offset += SubElement * 2; // Sub-elements are word-sized (2 bytes)

        return offset;
    }

    /// <summary>
    /// Get the number of bytes to read for this address.
    /// </summary>
    public int GetReadSize()
    {
        if (IsBitAddress)
            return 2; // Read the word containing the bit

        if (HasSubElement)
            return 2; // Sub-elements are word-sized

        return PcccTypes.GetElementSize(PcccFileType);
    }

    public override string ToString()
    {
        var result = $"{FileType}{FileNumber}:{Element}";
        if (HasSubElement)
            result += $".{SubElement}";
        if (IsBitAddress)
            result += $"/{BitNumber}";
        return result;
    }

    // --- Address Parsing ---

    // Regex for SLC addresses:
    //   Group 1: file type prefix (O, I, S, B, T, C, R, N, F, A, ST, L)
    //   Group 2: file number (optional for O, I, S - defaults to file 0, 1, 2)
    //   Group 3: element number
    //   Group 4: sub-element or bit access (optional)
    private static readonly Regex AddressRegex = new(
        @"^(ST|[OISBTNFCRAL])(\d*):?(\d+)(?:[./](.+))?$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Parse an SLC/MicroLogix/PLC-5 address string.
    /// </summary>
    /// <exception cref="FormatException">If the address format is invalid.</exception>
    public static PcccAddress Parse(string address)
    {
        if (string.IsNullOrWhiteSpace(address))
            throw new FormatException("Address cannot be empty.");

        var match = AddressRegex.Match(address.Trim());
        if (!match.Success)
            throw new FormatException($"Invalid SLC address format: '{address}'");

        var fileTypeStr = match.Groups[1].Value.ToUpperInvariant();
        var fileNumberStr = match.Groups[2].Value;
        var elementStr = match.Groups[3].Value;
        var suffixStr = match.Groups[4].Success ? match.Groups[4].Value : null;

        // Determine file number (some types have defaults)
        int fileNumber;
        if (string.IsNullOrEmpty(fileNumberStr))
        {
            fileNumber = GetDefaultFileNumber(fileTypeStr);
        }
        else
        {
            fileNumber = int.Parse(fileNumberStr);
        }

        var element = int.Parse(elementStr);
        var pcccFileType = GetPcccFileType(fileTypeStr);

        // Parse suffix: could be bit number (/N), sub-element name (.ACC), or sub-element index (.N)
        int subElement = -1;
        int bitNumber = -1;

        if (suffixStr != null)
        {
            // Check if the original address uses '/' (bit access) vs '.' (sub-element)
            var slashIdx = address.IndexOf('/');
            var dotAfterElement = -1;

            // Find '.' that comes after the element number (not the one separating file:element)
            var colonIdx = address.IndexOf(':');
            if (colonIdx >= 0)
            {
                dotAfterElement = address.IndexOf('.', colonIdx + 1);
            }
            else
            {
                // For addresses like B3/5 (no colon), look for '.' after the element digits
                var firstDigitAfterType = fileTypeStr.Length + fileNumberStr.Length;
                dotAfterElement = address.IndexOf('.', firstDigitAfterType);
            }

            if (slashIdx >= 0)
            {
                // Bit access: B3:0/5
                bitNumber = int.Parse(suffixStr);
            }
            else if (dotAfterElement >= 0)
            {
                // Sub-element: T4:0.ACC, T4:0.PRE, T4:0.1
                (subElement, bitNumber) = ParseSubElement(suffixStr, pcccFileType);
            }
        }

        return new PcccAddress(fileTypeStr, fileNumber, element, subElement, bitNumber, pcccFileType);
    }

    /// <summary>
    /// Try to parse an address, returning false if the format is invalid.
    /// </summary>
    public static bool TryParse(string address, out PcccAddress result)
    {
        try
        {
            result = Parse(address);
            return true;
        }
        catch
        {
            result = default;
            return false;
        }
    }

    private static int GetDefaultFileNumber(string fileType) => fileType switch
    {
        "O" => 0,
        "I" => 1,
        "S" => 2,
        "B" => 3,
        "T" => 4,
        "C" => 5,
        "R" => 6,
        "N" => 7,
        "F" => 8,
        _ => throw new FormatException($"File type '{fileType}' requires an explicit file number."),
    };

    private static PcccFileType GetPcccFileType(string fileType) => fileType switch
    {
        "O" => PcccFileType.Output,
        "I" => PcccFileType.Input,
        "S" => PcccFileType.Status,
        "B" => PcccFileType.Bit,
        "T" => PcccFileType.Timer,
        "C" => PcccFileType.Counter,
        "R" => PcccFileType.Control,
        "N" => PcccFileType.Integer,
        "F" => PcccFileType.Float,
        "ST" => PcccFileType.String,
        "A" => PcccFileType.Ascii,
        "L" => PcccFileType.Long,
        _ => throw new FormatException($"Unknown SLC file type: '{fileType}'"),
    };

    /// <summary>
    /// Parse a sub-element suffix (e.g., "ACC", "PRE", "EN", "DN", "1", "LEN", "POS").
    /// Returns (subElementIndex, bitNumber).
    /// Named sub-elements for Timer/Counter/Control are mapped to their word offset.
    /// Bit-level sub-elements (EN, DN, TT, etc.) return the appropriate bit number.
    /// </summary>
    private static (int SubElement, int BitNumber) ParseSubElement(
        string suffix, PcccFileType fileType)
    {
        var upper = suffix.ToUpperInvariant();

        // Timer sub-elements (T4:0.xxx)
        if (fileType == PcccFileType.Timer)
        {
            return upper switch
            {
                "PRE" => (1, -1),  // Preset value (word 1)
                "ACC" => (2, -1),  // Accumulated value (word 2)
                "EN" => (0, 15),   // Enable bit (word 0, bit 15)
                "TT" => (0, 14),   // Timer timing bit (word 0, bit 14)
                "DN" => (0, 13),   // Done bit (word 0, bit 13)
                _ when int.TryParse(suffix, out var idx) => (idx, -1),
                _ => throw new FormatException($"Unknown timer sub-element: '{suffix}'"),
            };
        }

        // Counter sub-elements (C5:0.xxx)
        if (fileType == PcccFileType.Counter)
        {
            return upper switch
            {
                "PRE" => (1, -1),  // Preset value (word 1)
                "ACC" => (2, -1),  // Accumulated value (word 2)
                "CU" => (0, 15),   // Count up enable (word 0, bit 15)
                "CD" => (0, 14),   // Count down enable (word 0, bit 14)
                "DN" => (0, 13),   // Done bit (word 0, bit 13)
                "OV" => (0, 12),   // Overflow (word 0, bit 12)
                "UN" => (0, 11),   // Underflow (word 0, bit 11)
                "UA" => (0, 10),   // Update accumulator (word 0, bit 10)
                _ when int.TryParse(suffix, out var idx) => (idx, -1),
                _ => throw new FormatException($"Unknown counter sub-element: '{suffix}'"),
            };
        }

        // Control sub-elements (R6:0.xxx)
        if (fileType == PcccFileType.Control)
        {
            return upper switch
            {
                "LEN" => (1, -1),  // Length (word 1)
                "POS" => (2, -1),  // Position (word 2)
                "EN" => (0, 15),   // Enable bit (word 0, bit 15)
                "EU" => (0, 14),   // Enable unload (word 0, bit 14)
                "DN" => (0, 13),   // Done bit (word 0, bit 13)
                "EM" => (0, 12),   // Empty (word 0, bit 12)
                "ER" => (0, 11),   // Error (word 0, bit 11)
                "UL" => (0, 10),   // Unload (word 0, bit 10)
                "IN" => (0, 9),    // Inhibit (word 0, bit 9)
                "FD" => (0, 8),    // Found (word 0, bit 8)
                _ when int.TryParse(suffix, out var idx) => (idx, -1),
                _ => throw new FormatException($"Unknown control sub-element: '{suffix}'"),
            };
        }

        // Generic numeric sub-element
        if (int.TryParse(suffix, out var numericIndex))
            return (numericIndex, -1);

        throw new FormatException($"Unknown sub-element '{suffix}' for file type {fileType}");
    }
}
