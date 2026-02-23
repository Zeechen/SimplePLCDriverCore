using System.Buffers.Binary;
using System.Text;
using SimplePLCDriverCore.Abstractions;

namespace SimplePLCDriverCore.Protocols.EtherNetIP.Pccc;

/// <summary>
/// PCCC file types used in SLC 500, MicroLogix, and PLC-5.
/// These map to the data table file types in the PLC.
/// </summary>
internal enum PcccFileType : byte
{
    Output = 0x82,
    Input = 0x83,
    Status = 0x84,
    Bit = 0x85,
    Timer = 0x86,
    Counter = 0x87,
    Control = 0x88,
    Integer = 0x89,
    Float = 0x8A,
    /// <summary>Output (alternate code used in some contexts).</summary>
    OutputAlt = 0x8B,
    /// <summary>Input (alternate code used in some contexts).</summary>
    InputAlt = 0x8C,
    String = 0x8D,
    Ascii = 0x8E,
    Long = 0x91,
}

/// <summary>
/// PCCC protocol constants and data type utilities for SLC/MicroLogix/PLC-5 communication.
/// </summary>
internal static class PcccTypes
{
    // --- PCCC Command and Function Codes ---

    /// <summary>PCCC command byte for typed operations.</summary>
    public const byte TypedCommand = 0x0F;

    /// <summary>Protected Typed Logical Read with 3 Address Fields.</summary>
    public const byte FnProtectedTypedLogicalRead3 = 0xA2;

    /// <summary>Protected Typed Logical Write with 3 Address Fields.</summary>
    public const byte FnProtectedTypedLogicalWrite3 = 0xAA;

    /// <summary>Protected Typed Logical Read with 2 Address Fields (for legacy PLC-5).</summary>
    public const byte FnProtectedTypedLogicalRead2 = 0xA1;

    /// <summary>Protected Typed Logical Write with 2 Address Fields (for legacy PLC-5).</summary>
    public const byte FnProtectedTypedLogicalWrite2 = 0xA9;

    // --- PCCC Status Codes ---

    public const byte StatusSuccess = 0x00;
    public const byte StatusIllegalCommand = 0x10;
    public const byte StatusHostProblem = 0x20;
    public const byte StatusRemoteNodeProblem = 0x30;
    public const byte StatusHardwareFault = 0x40;
    public const byte StatusAddressProblem = 0x50;
    public const byte StatusCommandProtection = 0x60;
    public const byte StatusParameterError = 0x70;
    public const byte StatusAddressProblem2 = 0x80;
    public const byte StatusDataProblem = 0x90;

    /// <summary>
    /// Get the byte size of one element for a PCCC file type.
    /// Timer, Counter, and Control elements are 6 bytes (3 words).
    /// Integer/Bit/Status/Output/Input elements are 2 bytes (1 word).
    /// Float elements are 4 bytes.
    /// Long elements are 4 bytes.
    /// String elements are 84 bytes (2-byte length + 82 chars).
    /// </summary>
    public static int GetElementSize(PcccFileType fileType) => fileType switch
    {
        PcccFileType.Output => 2,
        PcccFileType.Input => 2,
        PcccFileType.Status => 2,
        PcccFileType.Bit => 2,
        PcccFileType.Timer => 6,     // 3 words: control, PRE, ACC
        PcccFileType.Counter => 6,   // 3 words: control, PRE, ACC
        PcccFileType.Control => 6,   // 3 words: control, LEN, POS
        PcccFileType.Integer => 2,
        PcccFileType.Float => 4,
        PcccFileType.String => 84,   // 2-byte length + 82 chars
        PcccFileType.Ascii => 2,     // 2 bytes per element
        PcccFileType.Long => 4,      // 32-bit integer
        PcccFileType.OutputAlt => 2,
        PcccFileType.InputAlt => 2,
        _ => 2,
    };

    /// <summary>
    /// Get a user-friendly error message from a PCCC status code.
    /// </summary>
    public static string GetStatusMessage(byte status) => (status & 0xF0) switch
    {
        0x00 => "Success",
        0x10 => "Illegal command or format",
        0x20 => "Host has a problem and will not communicate",
        0x30 => "Remote node host is missing, disconnected, or shut down",
        0x40 => "Hardware fault",
        0x50 => "Address problem - illegal address or data not available",
        0x60 => "Command protection violation - insufficient privilege",
        0x70 => "Command parameter error",
        0x80 => "Address problem - address doesn't point to data",
        0x90 => "Data conversion or comparison error",
        _ => $"Unknown PCCC status: 0x{status:X2}",
    };

    /// <summary>
    /// Map a PCCC file type to a PlcDataType enum.
    /// </summary>
    public static PlcDataType ToPlcDataType(PcccFileType fileType) => fileType switch
    {
        PcccFileType.Bit => PlcDataType.Bool,
        PcccFileType.Integer => PlcDataType.Int,
        PcccFileType.Float => PlcDataType.Real,
        PcccFileType.Long => PlcDataType.Dint,
        PcccFileType.Timer => PlcDataType.Structure,
        PcccFileType.Counter => PlcDataType.Structure,
        PcccFileType.Control => PlcDataType.Structure,
        PcccFileType.String => PlcDataType.String,
        PcccFileType.Output => PlcDataType.Int,
        PcccFileType.Input => PlcDataType.Int,
        PcccFileType.Status => PlcDataType.Int,
        PcccFileType.Ascii => PlcDataType.Int,
        _ => PlcDataType.Int,
    };

    /// <summary>
    /// Map a PCCC file type to a human-readable type name.
    /// </summary>
    public static string GetTypeName(PcccFileType fileType) => fileType switch
    {
        PcccFileType.Output => "OUTPUT",
        PcccFileType.Input => "INPUT",
        PcccFileType.Status => "STATUS",
        PcccFileType.Bit => "BIT",
        PcccFileType.Timer => "TIMER",
        PcccFileType.Counter => "COUNTER",
        PcccFileType.Control => "CONTROL",
        PcccFileType.Integer => "INT",
        PcccFileType.Float => "FLOAT",
        PcccFileType.String => "STRING",
        PcccFileType.Ascii => "ASCII",
        PcccFileType.Long => "LONG",
        _ => "UNKNOWN",
    };

    /// <summary>
    /// Decode raw PCCC response bytes into a PlcTagValue based on the file type and address.
    /// </summary>
    public static PlcTagValue DecodeValue(ReadOnlySpan<byte> data, PcccAddress address)
    {
        if (data.Length == 0)
            return PlcTagValue.Null;

        // Bit-level access: extract specific bit from the word
        if (address.IsBitAddress)
        {
            if (data.Length < 2)
                return PlcTagValue.Null;

            var word = BinaryPrimitives.ReadUInt16LittleEndian(data);
            var bit = (word >> address.BitNumber) & 1;
            return PlcTagValue.FromBool(bit != 0);
        }

        // Sub-element access for timer/counter/control returns an INT word
        if (address.HasSubElement)
        {
            if (data.Length < 2)
                return PlcTagValue.Null;

            var word = BinaryPrimitives.ReadInt16LittleEndian(data);
            return PlcTagValue.FromInt(word);
        }

        // Full element decode based on file type
        return address.PcccFileType switch
        {
            PcccFileType.Integer or PcccFileType.Output or PcccFileType.Input
                or PcccFileType.Status or PcccFileType.Ascii
                when data.Length >= 2
                => PlcTagValue.FromInt(BinaryPrimitives.ReadInt16LittleEndian(data)),

            PcccFileType.Bit when data.Length >= 2
                => PlcTagValue.FromInt(BinaryPrimitives.ReadInt16LittleEndian(data)),

            PcccFileType.Float when data.Length >= 4
                => PlcTagValue.FromReal(BinaryPrimitives.ReadSingleLittleEndian(data)),

            PcccFileType.Long when data.Length >= 4
                => PlcTagValue.FromDInt(BinaryPrimitives.ReadInt32LittleEndian(data)),

            PcccFileType.Timer or PcccFileType.Counter or PcccFileType.Control
                when data.Length >= 6
                => DecodeTimerCounterControl(data, address.PcccFileType),

            PcccFileType.String when data.Length >= 2
                => DecodeSlcString(data),

            _ => new PlcTagValue(data.ToArray(), PlcDataType.Unknown),
        };
    }

    /// <summary>
    /// Encode a .NET value to PCCC bytes for a write request.
    /// </summary>
    public static byte[] EncodeValue(object value, PcccAddress address)
    {
        // Bit-level write: read-modify-write pattern handled at driver level.
        // Here we encode the value for the element/sub-element.

        if (address.IsBitAddress)
        {
            // For bit writes, we encode a 16-bit word with the bit set/cleared.
            // The driver must perform read-modify-write to preserve other bits.
            // This returns a mask + value pair for the driver to use.
            var bitValue = Convert.ToBoolean(value);
            var word = bitValue ? (ushort)(1 << address.BitNumber) : (ushort)0;
            var buf = new byte[2];
            BinaryPrimitives.WriteUInt16LittleEndian(buf, word);
            return buf;
        }

        if (address.HasSubElement)
        {
            // Sub-elements are word-sized (INT)
            var intVal = Convert.ToInt16(value);
            var buf = new byte[2];
            BinaryPrimitives.WriteInt16LittleEndian(buf, intVal);
            return buf;
        }

        return address.PcccFileType switch
        {
            PcccFileType.Integer or PcccFileType.Output or PcccFileType.Input
                or PcccFileType.Status or PcccFileType.Bit or PcccFileType.Ascii
                => EncodeInt16(Convert.ToInt16(value)),

            PcccFileType.Float => EncodeSingle(Convert.ToSingle(value)),

            PcccFileType.Long => EncodeInt32(Convert.ToInt32(value)),

            PcccFileType.String when value is string strVal => EncodeSlcString(strVal),

            PcccFileType.String => EncodeSlcString(value.ToString() ?? string.Empty),

            _ => throw new ArgumentException(
                $"Cannot encode value for PCCC file type {address.PcccFileType}"),
        };
    }

    /// <summary>Get the bit mask for a specific bit in a 16-bit word.</summary>
    public static ushort GetBitMask(int bitNumber) => (ushort)(1 << bitNumber);

    // --- Timer/Counter/Control decode ---

    private static PlcTagValue DecodeTimerCounterControl(ReadOnlySpan<byte> data, PcccFileType fileType)
    {
        var control = BinaryPrimitives.ReadInt16LittleEndian(data);
        var preset = BinaryPrimitives.ReadInt16LittleEndian(data[2..]);
        var accum = BinaryPrimitives.ReadInt16LittleEndian(data[4..]);

        var members = new Dictionary<string, PlcTagValue>
        {
            ["CTL"] = PlcTagValue.FromInt(control),
            ["PRE"] = PlcTagValue.FromInt(preset),
            ["ACC"] = PlcTagValue.FromInt(accum),
        };

        // Add status bit accessors based on file type
        if (fileType == PcccFileType.Timer)
        {
            members["EN"] = PlcTagValue.FromBool((control & (1 << 15)) != 0);
            members["TT"] = PlcTagValue.FromBool((control & (1 << 14)) != 0);
            members["DN"] = PlcTagValue.FromBool((control & (1 << 13)) != 0);
        }
        else if (fileType == PcccFileType.Counter)
        {
            members["CU"] = PlcTagValue.FromBool((control & (1 << 15)) != 0);
            members["CD"] = PlcTagValue.FromBool((control & (1 << 14)) != 0);
            members["DN"] = PlcTagValue.FromBool((control & (1 << 13)) != 0);
            members["OV"] = PlcTagValue.FromBool((control & (1 << 12)) != 0);
            members["UN"] = PlcTagValue.FromBool((control & (1 << 11)) != 0);
        }
        else if (fileType == PcccFileType.Control)
        {
            members["EN"] = PlcTagValue.FromBool((control & (1 << 15)) != 0);
            members["EU"] = PlcTagValue.FromBool((control & (1 << 14)) != 0);
            members["DN"] = PlcTagValue.FromBool((control & (1 << 13)) != 0);
            members["EM"] = PlcTagValue.FromBool((control & (1 << 12)) != 0);
            members["ER"] = PlcTagValue.FromBool((control & (1 << 11)) != 0);
            members["UL"] = PlcTagValue.FromBool((control & (1 << 10)) != 0);
            members["IN"] = PlcTagValue.FromBool((control & (1 << 9)) != 0);
            members["FD"] = PlcTagValue.FromBool((control & (1 << 8)) != 0);
            members["LEN"] = members["PRE"]; // Alias: Control uses LEN instead of PRE
            members["POS"] = members["ACC"]; // Alias: Control uses POS instead of ACC
        }

        return PlcTagValue.FromStructure(
            new System.Collections.ObjectModel.ReadOnlyDictionary<string, PlcTagValue>(members));
    }

    // --- SLC String ---

    /// <summary>
    /// Decode an SLC STRING from PCCC data.
    /// SLC STRING format: 2-byte INT length (LE) + up to 82 ASCII chars.
    /// Total element size is 84 bytes.
    /// </summary>
    private static PlcTagValue DecodeSlcString(ReadOnlySpan<byte> data)
    {
        if (data.Length < 2)
            return PlcTagValue.FromString(string.Empty);

        var charCount = BinaryPrimitives.ReadInt16LittleEndian(data);
        if (charCount < 0) charCount = 0;
        if (charCount > 82) charCount = 82;
        if (charCount > data.Length - 2) charCount = (short)(data.Length - 2);

        var str = Encoding.ASCII.GetString(data.Slice(2, charCount));
        return PlcTagValue.FromString(str);
    }

    /// <summary>
    /// Encode a .NET string into SLC STRING format.
    /// Format: 2-byte INT length (LE) + up to 82 ASCII chars, padded to 84 bytes total.
    /// </summary>
    private static byte[] EncodeSlcString(string value)
    {
        var result = new byte[84];
        var charCount = Math.Min(value.Length, 82);
        BinaryPrimitives.WriteInt16LittleEndian(result, (short)charCount);
        Encoding.ASCII.GetBytes(value.AsSpan(0, charCount), result.AsSpan(2));
        return result;
    }

    // --- Primitive Encoders ---

    private static byte[] EncodeInt16(short value)
    {
        var buf = new byte[2];
        BinaryPrimitives.WriteInt16LittleEndian(buf, value);
        return buf;
    }

    private static byte[] EncodeSingle(float value)
    {
        var buf = new byte[4];
        BinaryPrimitives.WriteSingleLittleEndian(buf, value);
        return buf;
    }

    private static byte[] EncodeInt32(int value)
    {
        var buf = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(buf, value);
        return buf;
    }
}
