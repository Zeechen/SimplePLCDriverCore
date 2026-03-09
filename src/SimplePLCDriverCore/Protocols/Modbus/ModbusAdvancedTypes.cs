namespace SimplePLCDriverCore.Protocols.Modbus;

/// <summary>
/// Diagnostic sub-functions for Modbus FC 08.
/// </summary>
public enum ModbusDiagnosticSubFunction : ushort
{
    /// <summary>Echo test — data sent is returned verbatim.</summary>
    ReturnQueryData = 0x0000,

    /// <summary>Reinitialize serial communications.</summary>
    RestartCommunications = 0x0001,

    /// <summary>Read the internal diagnostic register.</summary>
    ReturnDiagnosticRegister = 0x0002,

    /// <summary>Remove device from active bus participation.</summary>
    ForceListenOnlyMode = 0x0004,

    /// <summary>Reset all diagnostic counters.</summary>
    ClearCounters = 0x000A,

    /// <summary>Total bus message count.</summary>
    ReturnBusMessageCount = 0x000B,

    /// <summary>CRC/parity error count.</summary>
    ReturnBusErrorCount = 0x000C,

    /// <summary>Modbus exception response count.</summary>
    ReturnBusExceptionCount = 0x000D,

    /// <summary>Messages addressed to this device.</summary>
    ReturnServerMessageCount = 0x000E,

    /// <summary>Requests with no response.</summary>
    ReturnServerNoResponseCount = 0x000F,

    /// <summary>Negative acknowledgment count.</summary>
    ReturnServerNakCount = 0x0010,

    /// <summary>Device busy count.</summary>
    ReturnServerBusyCount = 0x0011,

    /// <summary>Character overrun count.</summary>
    ReturnBusCharOverrunCount = 0x0012,
}

/// <summary>
/// Device identification conformity level for Modbus FC 43/14.
/// </summary>
public enum ModbusDeviceIdLevel : byte
{
    /// <summary>Basic: VendorName, ProductCode, Revision (mandatory objects 0x00-0x02).</summary>
    Basic = 0x01,

    /// <summary>Regular: adds VendorUrl, ProductName, ModelName, UserAppName (objects 0x03-0x06).</summary>
    Regular = 0x02,

    /// <summary>Extended: vendor-specific objects (0x80-0xFF).</summary>
    Extended = 0x03,
}

/// <summary>
/// Result of a Modbus FC 08 Diagnostics operation.
/// </summary>
public record ModbusDiagnosticResult(
    ModbusDiagnosticSubFunction SubFunction,
    ushort Data,
    bool IsSuccess,
    string? ErrorMessage)
{
    public static ModbusDiagnosticResult Success(ModbusDiagnosticSubFunction subFunction, ushort data) =>
        new(subFunction, data, true, null);

    public static ModbusDiagnosticResult Failure(ModbusDiagnosticSubFunction subFunction, string error) =>
        new(subFunction, 0, false, error);
}

/// <summary>
/// Result of a Modbus FC 23 Read/Write Multiple Registers operation.
/// </summary>
public record ModbusReadWriteResult(
    short[] ReadValues,
    bool IsSuccess,
    string? ErrorMessage)
{
    public static ModbusReadWriteResult Success(short[] values) =>
        new(values, true, null);

    public static ModbusReadWriteResult Failure(string error) =>
        new(Array.Empty<short>(), false, error);
}

/// <summary>
/// Result of a Modbus FC 43/14 Read Device Identification operation.
/// </summary>
public record ModbusDeviceIdentification(
    byte ConformityLevel,
    string? VendorName,
    string? ProductCode,
    string? MajorMinorRevision,
    string? VendorUrl,
    string? ProductName,
    string? ModelName,
    string? UserApplicationName,
    IReadOnlyDictionary<int, string> AllObjects);

/// <summary>
/// Raw Modbus response for SendRawAsync.
/// </summary>
public record ModbusRawResponse(
    byte FunctionCode,
    ReadOnlyMemory<byte> Data,
    bool IsException,
    byte? ExceptionCode,
    string? ErrorMessage)
{
    public static ModbusRawResponse Success(byte functionCode, ReadOnlyMemory<byte> data) =>
        new(functionCode, data, false, null, null);

    public static ModbusRawResponse Failure(byte functionCode, byte exceptionCode, string errorMessage) =>
        new(functionCode, ReadOnlyMemory<byte>.Empty, true, exceptionCode, errorMessage);
}
