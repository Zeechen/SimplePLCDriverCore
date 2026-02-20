namespace SimplePLCDriverCore.Protocols.EtherNetIP.Cip;

/// <summary>
/// CIP service codes for tag operations, connection management, and object access.
/// </summary>
internal static class CipServices
{
    // --- Object Services ---
    public const byte GetAttributeAll = 0x01;
    public const byte GetAttributeList = 0x03;
    public const byte SetAttributeList = 0x04;

    // --- Connection Manager (Class 0x06) ---
    public const byte ForwardClose = 0x4E;
    public const byte ForwardOpen = 0x54;
    public const byte LargeForwardOpen = 0x5B;

    // --- Tag Services (Logix-specific) ---
    public const byte ReadTag = 0x4C;
    public const byte WriteTag = 0x4D;
    public const byte ReadModifyWriteTag = 0x4E;
    public const byte ReadTagFragmented = 0x52;
    public const byte WriteTagFragmented = 0x53;
    public const byte GetInstanceAttributeList = 0x55;

    // --- Multiple Service ---
    public const byte MultipleServicePacket = 0x0A;

    // --- Reply bit: set in service code of response messages ---
    public const byte ReplyMask = 0x80;
}

/// <summary>
/// CIP object class codes.
/// </summary>
internal static class CipClasses
{
    public const ushort Identity = 0x01;
    public const ushort MessageRouter = 0x02;
    public const ushort ConnectionManager = 0x06;
    public const ushort Symbol = 0x6B;        // Tag browsing
    public const ushort Template = 0x6C;       // UDT definitions
    public const ushort PcccObject = 0x67;     // PCCC for SLC/MicroLogix
}

/// <summary>
/// CIP general status codes.
/// </summary>
internal enum CipGeneralStatus : byte
{
    Success = 0x00,
    ConnectionFailure = 0x01,
    ResourceUnavailable = 0x02,
    InvalidParameterValue = 0x03,
    PathSegmentError = 0x04,
    PathDestinationUnknown = 0x05,
    PartialTransfer = 0x06,
    ConnectionLost = 0x07,
    ServiceNotSupported = 0x08,
    InvalidAttributeValue = 0x09,
    AttributeListError = 0x0A,
    AlreadyInRequestedState = 0x0B,
    ObjectStateConflict = 0x0C,
    ObjectAlreadyExists = 0x0D,
    AttributeNotSettable = 0x0E,
    PrivilegeViolation = 0x0F,
    DeviceStateConflict = 0x10,
    ReplyDataTooLarge = 0x11,
    FragmentationOfPrimitive = 0x12,
    NotEnoughData = 0x13,
    AttributeNotSupported = 0x14,
    TooMuchData = 0x15,
    ObjectDoesNotExist = 0x16,
    NoFragmentation = 0x17,
    DataNotSaved = 0x18,
    DataWriteFailure = 0x19,
    KeyFailureInPath = 0x25,
    InvalidPathSize = 0x26,
    UnexpectedAttribute = 0x27,
    InvalidMemberId = 0x28,
    MemberNotSettable = 0x29,
}

/// <summary>
/// CIP data type codes used in tag definitions and read/write responses.
/// </summary>
internal static class CipDataTypes
{
    public const ushort Bool = 0x00C1;
    public const ushort Sint = 0x00C2;
    public const ushort Int = 0x00C3;
    public const ushort Dint = 0x00C4;
    public const ushort Lint = 0x00C5;
    public const ushort Usint = 0x00C6;
    public const ushort Uint = 0x00C7;
    public const ushort Udint = 0x00C8;
    public const ushort Ulint = 0x00C9;
    public const ushort Real = 0x00CA;
    public const ushort Lreal = 0x00CB;
    public const ushort String = 0x00DA;

    /// <summary>Bit 15 set indicates a structure type. Lower bits are template instance ID.</summary>
    public const ushort StructureMask = 0x8000;

    /// <summary>Bit 13 set indicates a system (predefined) type.</summary>
    public const ushort SystemMask = 0x2000;

    /// <summary>
    /// Abbreviated structure type in CIP ReadTag responses.
    /// When reading a structure tag, the PLC returns 0x02A0 (wire bytes: A0 02)
    /// followed by a 2-byte structure handle (CRC), totaling 4 bytes of type info.
    /// This is different from the full type code (0x8xxx) used in tag list responses.
    /// </summary>
    public const ushort AbbreviatedStructureType = 0x02A0;

    /// <summary>Array dimension flags (bits 13-14 of symbol_type in tag list).</summary>
    public const ushort Dim1Mask = 0x2000;
    public const ushort Dim2Mask = 0x4000;
    public const ushort Dim3Mask = 0x6000;
    public const ushort DimMask = 0x6000;

    /// <summary>Check if a type code represents a structure.</summary>
    public static bool IsStructure(ushort typeCode) => (typeCode & StructureMask) != 0;

    /// <summary>Get the template instance ID from a structure type code.</summary>
    public static ushort GetTemplateInstanceId(ushort typeCode) =>
        (ushort)(typeCode & 0x0FFF);

    /// <summary>Get the byte size of an atomic CIP data type. Returns 0 for unknown/structure types.</summary>
    public static int GetAtomicSize(ushort typeCode) => typeCode switch
    {
        Bool => 1,
        Sint => 1,
        Int => 2,
        Dint => 4,
        Lint => 8,
        Usint => 1,
        Uint => 2,
        Udint => 4,
        Ulint => 8,
        Real => 4,
        Lreal => 8,
        _ => 0,
    };

    /// <summary>
    /// Detect if raw structure data is likely a standard Logix STRING (ASCIISTRING82).
    /// Used as fallback when the tag database is not available or empty.
    /// Standard STRING format: 4-byte DINT length prefix + 82 SINT chars + 2 padding = 88 bytes.
    /// </summary>
    public static bool IsLikelyStringData(ReadOnlySpan<byte> data)
    {
        // Standard STRING (ASCIISTRING82) is exactly 88 bytes
        if (data.Length != 88)
            return false;

        var len = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(data);

        // LEN must be in valid range [0, 82] for standard STRING
        return len >= 0 && len <= 82;
    }

    /// <summary>Get the human-readable name for a CIP type code.</summary>
    public static string GetTypeName(ushort typeCode) => typeCode switch
    {
        Bool => "BOOL",
        Sint => "SINT",
        Int => "INT",
        Dint => "DINT",
        Lint => "LINT",
        Usint => "USINT",
        Uint => "UINT",
        Udint => "UDINT",
        Ulint => "ULINT",
        Real => "REAL",
        Lreal => "LREAL",
        String => "STRING",
        _ when IsStructure(typeCode) => "STRUCT",
        _ => "UNKNOWN",
    };
}
