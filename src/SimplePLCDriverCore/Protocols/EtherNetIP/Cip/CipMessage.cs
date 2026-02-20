using SimplePLCDriverCore.Common.Buffers;

namespace SimplePLCDriverCore.Protocols.EtherNetIP.Cip;

/// <summary>
/// CIP message response data parsed from a reply.
///
/// CIP Reply format:
///   Service | Reply (1 byte) - original service code OR'd with 0x80
///   Reserved (1 byte) - always 0
///   General Status (1 byte) - CipGeneralStatus
///   Additional Status Size (1 byte) - size in words of additional status
///   Additional Status (variable)
///   Response Data (variable)
/// </summary>
internal readonly struct CipResponse
{
    /// <summary>Original service code from request (reply bit stripped).</summary>
    public byte Service { get; }

    /// <summary>General status code.</summary>
    public CipGeneralStatus GeneralStatus { get; }

    /// <summary>Additional status words (extended status info).</summary>
    public ushort[] AdditionalStatus { get; }

    /// <summary>Response payload data (after status fields).</summary>
    public ReadOnlyMemory<byte> Data { get; }

    /// <summary>Whether the response indicates success.</summary>
    public bool IsSuccess =>
        GeneralStatus == CipGeneralStatus.Success ||
        GeneralStatus == CipGeneralStatus.PartialTransfer;

    /// <summary>Whether this is a partial transfer requiring continuation.</summary>
    public bool IsPartialTransfer => GeneralStatus == CipGeneralStatus.PartialTransfer;

    public CipResponse(byte service, CipGeneralStatus generalStatus,
                       ushort[] additionalStatus, ReadOnlyMemory<byte> data)
    {
        Service = service;
        GeneralStatus = generalStatus;
        AdditionalStatus = additionalStatus;
        Data = data;
    }

    /// <summary>Get a technical diagnostic string with CIP status codes.</summary>
    public string GetErrorMessage()
    {
        var msg = $"CIP error: {GeneralStatus} (0x{(byte)GeneralStatus:X2})";
        if (AdditionalStatus.Length > 0)
            msg += $" [extended: {string.Join(", ", AdditionalStatus.Select(s => $"0x{s:X4}"))}]";
        return msg;
    }

    /// <summary>Get a user-friendly error message suitable for display.</summary>
    public string GetUserFriendlyMessage() => GeneralStatus switch
    {
        CipGeneralStatus.ConnectionFailure => "Connection to PLC failed",
        CipGeneralStatus.ResourceUnavailable => "PLC resource unavailable",
        CipGeneralStatus.InvalidParameterValue => "Invalid parameter value",
        CipGeneralStatus.PathSegmentError => "Tag not found in PLC",
        CipGeneralStatus.PathDestinationUnknown => "Tag path destination unknown",
        CipGeneralStatus.ConnectionLost => "Connection to PLC lost",
        CipGeneralStatus.ServiceNotSupported => "Operation not supported by PLC",
        CipGeneralStatus.InvalidAttributeValue => "Invalid attribute value",
        CipGeneralStatus.PrivilegeViolation => "Access denied - insufficient privileges",
        CipGeneralStatus.DeviceStateConflict => "PLC is in a conflicting state",
        CipGeneralStatus.NotEnoughData => "Not enough data provided for write",
        CipGeneralStatus.TooMuchData => "Too much data provided for write",
        CipGeneralStatus.ObjectDoesNotExist => "Tag does not exist in PLC",
        CipGeneralStatus.DataWriteFailure => "Write operation failed",
        CipGeneralStatus.InvalidPathSize => "Invalid tag path size",
        CipGeneralStatus.InvalidMemberId => "Invalid UDT member",
        CipGeneralStatus.MemberNotSettable => "UDT member is read-only",
        _ => $"PLC returned error code 0x{(byte)GeneralStatus:X2}",
    };
}

/// <summary>
/// Builds and parses CIP messages.
/// </summary>
internal static class CipMessage
{
    /// <summary>
    /// Parse a CIP response from raw bytes.
    /// </summary>
    public static CipResponse ParseResponse(ReadOnlyMemory<byte> data)
    {
        var reader = new PacketReader(data);

        var serviceReply = reader.ReadUInt8();
        var service = (byte)(serviceReply & ~CipServices.ReplyMask);

        reader.Skip(1); // reserved byte

        var generalStatus = (CipGeneralStatus)reader.ReadUInt8();
        var additionalStatusSize = reader.ReadUInt8(); // size in words

        var additionalStatus = new ushort[additionalStatusSize];
        for (var i = 0; i < additionalStatusSize; i++)
            additionalStatus[i] = reader.ReadUInt16LE();

        var responseData = data.Slice(reader.Position);

        return new CipResponse(service, generalStatus, additionalStatus, responseData);
    }

    /// <summary>
    /// Build an Unconnected Send message that wraps a CIP request.
    /// This is used to route CIP messages through the Connection Manager.
    ///
    /// Format:
    ///   Service: 0x52 (Unconnected Send)
    ///   Path: Connection Manager (class 0x06, instance 1)
    ///   Data:
    ///     Priority/Tick (1 byte)
    ///     Timeout Ticks (1 byte)
    ///     Embedded Message Length (2 bytes LE)
    ///     Embedded CIP Message (variable)
    ///     [Pad byte if odd]
    ///     Route Path Size (1 byte, in words)
    ///     Reserved (1 byte)
    ///     Route Path (variable)
    /// </summary>
    public static byte[] BuildUnconnectedSend(
        ReadOnlySpan<byte> cipRequest,
        byte slot = 0,
        byte priorityTimeTick = 7,
        byte timeoutTicks = 157)
    {
        var routePath = CipPath.EncodeRoutePath(slot);
        var needsPad = cipRequest.Length % 2 != 0;
        var totalSize = 2 + 4 + cipRequest.Length + (needsPad ? 1 : 0) + 2 + routePath.Length
                        + 2 + 1 + 1; // service + path size + path

        using var writer = new PacketWriter(totalSize);

        // Service
        writer.WriteUInt8(0x52); // Unconnected Send

        // Path to Connection Manager: class 0x06, instance 1
        var cmPath = CipPath.BuildClassInstancePath(CipClasses.ConnectionManager, 1);
        writer.WriteUInt8(CipPath.GetPathSizeInWords(cmPath.Length));
        writer.WriteBytes(cmPath);

        // Priority/Tick Time
        writer.WriteUInt8(priorityTimeTick);
        // Timeout Ticks
        writer.WriteUInt8(timeoutTicks);

        // Embedded message length
        writer.WriteUInt16LE((ushort)cipRequest.Length);
        // Embedded CIP message
        writer.WriteBytes(cipRequest);

        // Pad to even if needed
        if (needsPad)
            writer.WriteUInt8(0);

        // Route path
        writer.WriteUInt8(CipPath.GetPathSizeInWords(routePath.Length));
        writer.WriteUInt8(0); // reserved
        writer.WriteBytes(routePath);

        return writer.ToArray();
    }

    /// <summary>
    /// Build a ReadTag request (CIP service 0x4C).
    ///
    /// Format:
    ///   Service: 0x4C
    ///   Path Size (1 byte, in words)
    ///   Symbolic Path (variable)
    ///   Number of Elements (2 bytes LE)
    /// </summary>
    public static byte[] BuildReadTagRequest(string tagName, ushort elementCount = 1)
    {
        var path = CipPath.EncodeSymbolicPath(tagName);

        using var writer = new PacketWriter(4 + path.Length);
        writer.WriteUInt8(CipServices.ReadTag);
        writer.WriteUInt8(CipPath.GetPathSizeInWords(path.Length));
        writer.WriteBytes(path);
        writer.WriteUInt16LE(elementCount);

        return writer.ToArray();
    }

    /// <summary>
    /// Build a WriteTag request (CIP service 0x4D).
    ///
    /// Format:
    ///   Service: 0x4D
    ///   Path Size (1 byte, in words)
    ///   Symbolic Path (variable)
    ///   Data Type (2 bytes LE)
    ///   Number of Elements (2 bytes LE)
    ///   Data (variable)
    /// </summary>
    public static byte[] BuildWriteTagRequest(
        string tagName, ushort dataType, ushort elementCount, ReadOnlySpan<byte> data)
    {
        var path = CipPath.EncodeSymbolicPath(tagName);

        using var writer = new PacketWriter(8 + path.Length + data.Length);
        writer.WriteUInt8(CipServices.WriteTag);
        writer.WriteUInt8(CipPath.GetPathSizeInWords(path.Length));
        writer.WriteBytes(path);
        writer.WriteUInt16LE(dataType);
        writer.WriteUInt16LE(elementCount);
        writer.WriteBytes(data);

        return writer.ToArray();
    }

    /// <summary>
    /// Build a ReadTag Fragmented request (CIP service 0x52) for large data.
    ///
    /// Format:
    ///   Service: 0x52
    ///   Path Size + Path
    ///   Number of Elements (2 bytes LE)
    ///   Offset (4 bytes LE)
    /// </summary>
    public static byte[] BuildReadTagFragmentedRequest(
        string tagName, ushort elementCount, uint offset)
    {
        var path = CipPath.EncodeSymbolicPath(tagName);

        using var writer = new PacketWriter(8 + path.Length);
        writer.WriteUInt8(CipServices.ReadTagFragmented);
        writer.WriteUInt8(CipPath.GetPathSizeInWords(path.Length));
        writer.WriteBytes(path);
        writer.WriteUInt16LE(elementCount);
        writer.WriteUInt32LE(offset);

        return writer.ToArray();
    }

    /// <summary>
    /// Build a WriteTag Fragmented request (CIP service 0x53) for large data.
    ///
    /// Format:
    ///   Service: 0x53
    ///   Path Size + Path
    ///   Data Type (2 bytes LE)
    ///   Number of Elements (2 bytes LE)
    ///   Offset (4 bytes LE)
    ///   Data (variable)
    /// </summary>
    public static byte[] BuildWriteTagFragmentedRequest(
        string tagName, ushort dataType, ushort elementCount, uint offset,
        ReadOnlySpan<byte> data)
    {
        var path = CipPath.EncodeSymbolicPath(tagName);

        using var writer = new PacketWriter(12 + path.Length + data.Length);
        writer.WriteUInt8(CipServices.WriteTagFragmented);
        writer.WriteUInt8(CipPath.GetPathSizeInWords(path.Length));
        writer.WriteBytes(path);
        writer.WriteUInt16LE(dataType);
        writer.WriteUInt16LE(elementCount);
        writer.WriteUInt32LE(offset);
        writer.WriteBytes(data);

        return writer.ToArray();
    }

    /// <summary>
    /// Build a GetAttributeList request for an object instance.
    /// Used for reading PLC identity info, tag attributes, etc.
    /// </summary>
    public static byte[] BuildGetAttributeListRequest(
        ushort classId, uint instanceId, ushort[] attributeIds)
    {
        var path = CipPath.BuildClassInstancePath(classId, instanceId);

        using var writer = new PacketWriter(4 + path.Length + 2 + attributeIds.Length * 2);
        writer.WriteUInt8(CipServices.GetAttributeList);
        writer.WriteUInt8(CipPath.GetPathSizeInWords(path.Length));
        writer.WriteBytes(path);
        writer.WriteUInt16LE((ushort)attributeIds.Length);
        foreach (var attrId in attributeIds)
            writer.WriteUInt16LE(attrId);

        return writer.ToArray();
    }
}
