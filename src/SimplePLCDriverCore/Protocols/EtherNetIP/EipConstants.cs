namespace SimplePLCDriverCore.Protocols.EtherNetIP;

/// <summary>
/// EtherNet/IP protocol constants.
/// </summary>
internal static class EipConstants
{
    /// <summary>Default EtherNet/IP TCP port.</summary>
    public const int DefaultPort = 44818;

    /// <summary>Size of the EtherNet/IP encapsulation header in bytes.</summary>
    public const int EncapsulationHeaderSize = 24;

    /// <summary>EtherNet/IP protocol version.</summary>
    public const ushort ProtocolVersion = 1;

    /// <summary>Sender context size in bytes.</summary>
    public const int SenderContextSize = 8;
}

/// <summary>
/// EtherNet/IP encapsulation command codes.
/// </summary>
internal enum EipCommand : ushort
{
    Nop = 0x0000,
    ListServices = 0x0004,
    ListIdentity = 0x0063,
    ListInterfaces = 0x0064,
    RegisterSession = 0x0065,
    UnregisterSession = 0x0066,
    SendRRData = 0x006F,
    SendUnitData = 0x0070,
}

/// <summary>
/// EtherNet/IP encapsulation status codes.
/// </summary>
internal enum EipStatus : uint
{
    Success = 0x0000,
    InvalidCommand = 0x0001,
    InsufficientMemory = 0x0002,
    IncorrectData = 0x0003,
    InvalidSessionHandle = 0x0064,
    InvalidLength = 0x0065,
    UnsupportedProtocolVersion = 0x0069,
}

/// <summary>
/// Common Packet Format (CPF) item type IDs.
/// </summary>
internal enum CpfItemTypeId : ushort
{
    NullAddress = 0x0000,
    ConnectedAddress = 0x00A1,
    ConnectedData = 0x00B1,
    UnconnectedData = 0x00B2,
    ListServicesResponse = 0x0100,
    SocketAddressInfoOtoT = 0x8000,
    SocketAddressInfoTtoO = 0x8001,
    SequencedAddress = 0x8002,
}
