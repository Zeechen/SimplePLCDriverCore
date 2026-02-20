using System.Buffers.Binary;
using System.Text;
using SimplePLCDriverCore.Abstractions;
using SimplePLCDriverCore.Common;
using SimplePLCDriverCore.Common.Buffers;

namespace SimplePLCDriverCore.Protocols.EtherNetIP.Cip;

/// <summary>
/// Reads the PLC tag list by iterating over instances of the CIP Symbol Object (Class 0x6B).
///
/// Each symbol instance has these attributes:
///   1 - Symbol Name (string)
///   2 - Symbol Type (UINT) - CIP data type code. Bit 15 = structure, bits 13-14 = array dimensions
///   3 - Symbol Instance ID (UDINT)
///   7 - Array Dimensions (3x UDINT) - dim0, dim1, dim2 sizes
///   8 - External Access (UINT) - read/write permissions
///
/// The upload uses GetInstanceAttributeList (service 0x55) starting from instance 0,
/// iterating until all instances are returned (uses PartialTransfer continuation).
/// </summary>
internal static class SymbolObject
{
    // Symbol attribute IDs we request
    private static readonly ushort[] SymbolAttributes = [1, 2, 7, 8];

    /// <summary>
    /// Build a GetInstanceAttributeList request for the Symbol Object.
    /// This returns a batch of tag definitions starting from the given instance.
    /// </summary>
    public static byte[] BuildGetInstanceAttributeListRequest(uint startInstance)
    {
        var path = CipPath.BuildClassInstancePath(CipClasses.Symbol, startInstance);

        using var writer = new PacketWriter(4 + path.Length + 2 + SymbolAttributes.Length * 2);
        writer.WriteUInt8(CipServices.GetInstanceAttributeList);
        writer.WriteUInt8(CipPath.GetPathSizeInWords(path.Length));
        writer.WriteBytes(path);

        // Attribute count + attribute IDs
        writer.WriteUInt16LE((ushort)SymbolAttributes.Length);
        foreach (var attr in SymbolAttributes)
            writer.WriteUInt16LE(attr);

        return writer.ToArray();
    }

    /// <summary>
    /// Parse the response from GetInstanceAttributeList and populate the tag database.
    /// Returns the last instance ID parsed (for continuation) and whether there are more tags.
    /// </summary>
    public static (uint LastInstanceId, List<PlcTagInfo> Tags) ParseInstanceAttributeListResponse(
        ReadOnlyMemory<byte> data)
    {
        var tags = new List<PlcTagInfo>();
        var span = data.Span;
        var offset = 0;
        uint lastInstanceId = 0;

        while (offset < span.Length)
        {
            // Each tag entry:
            //   Instance ID (4 bytes LE)
            //   Attribute 1 - Name: status(2) + name_len(2) + name(variable)
            //   Attribute 2 - Type: status(2) + type(2)
            //   Attribute 7 - Dimensions: status(2) + dim0(4) + dim1(4) + dim2(4)
            //   Attribute 8 - External Access: status(2) + access(2)
            if (offset + 4 > span.Length)
                break;

            var instanceId = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(offset));
            offset += 4;
            lastInstanceId = instanceId;

            // Attribute 1: Symbol Name
            if (offset + 4 > span.Length)
                break;

            var nameStatus = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(offset));
            offset += 2;

            if (nameStatus != 0)
                break;

            var nameLen = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(offset));
            offset += 2;

            if (offset + nameLen > span.Length)
                break;

            var name = Encoding.ASCII.GetString(span.Slice(offset, nameLen));
            offset += nameLen;

            // Attribute 2: Symbol Type
            if (offset + 4 > span.Length)
                break;

            var typeStatus = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(offset));
            offset += 2;

            if (typeStatus != 0)
                break;

            var symbolType = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(offset));
            offset += 2;

            // Attribute 7: Dimensions
            if (offset + 14 > span.Length)
                break;

            var dimStatus = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(offset));
            offset += 2;

            uint dim0 = 0, dim1 = 0, dim2 = 0;
            if (dimStatus == 0)
            {
                dim0 = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(offset));
                offset += 4;
                dim1 = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(offset));
                offset += 4;
                dim2 = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(offset));
                offset += 4;
            }
            else
            {
                offset += 12; // skip dimension data
            }

            // Attribute 8: External Access
            if (offset + 4 > span.Length)
                break;

            var accessStatus = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(offset));
            offset += 2;

            // Skip external access value (2 bytes)
            if (accessStatus == 0 && offset + 2 <= span.Length)
                offset += 2;

            // Skip internal tags (names starting with __ or containing : for module-defined tags)
            if (name.StartsWith("__", StringComparison.Ordinal))
                continue;

            // Parse dimensions
            var dimensions = BuildDimensions(dim0, dim1, dim2);

            // Determine type info
            var isStructure = CipDataTypes.IsStructure(symbolType);
            var rawTypeCode = (ushort)(symbolType & 0x0FFF);
            ushort templateInstanceId = 0;

            if (isStructure)
            {
                templateInstanceId = CipDataTypes.GetTemplateInstanceId(symbolType);
                rawTypeCode = symbolType;
            }

            // Detect program scope
            var isProgramScoped = false;
            string? programName = null;

            if (name.StartsWith("Program:", StringComparison.OrdinalIgnoreCase))
            {
                isProgramScoped = true;
                var dotIndex = name.IndexOf('.', 8);
                if (dotIndex > 0)
                    programName = name[8..dotIndex];
            }

            var typeName = isStructure ? "STRUCT" : CipDataTypes.GetTypeName(rawTypeCode);

            var tagInfo = new PlcTagInfo
            {
                Name = name,
                DataType = isStructure ? PlcDataType.Structure : CipTypeCodec.ToPlcDataType(rawTypeCode),
                TypeName = typeName,
                Dimensions = dimensions,
                IsStructure = isStructure,
                IsProgramScoped = isProgramScoped,
                ProgramName = programName,
                InstanceId = instanceId,
                RawTypeCode = rawTypeCode,
                TemplateInstanceId = templateInstanceId,
            };

            tags.Add(tagInfo);
        }

        return (lastInstanceId, tags);
    }

    /// <summary>
    /// Upload the full tag list from the PLC using GetInstanceAttributeList with continuation.
    /// </summary>
    public static async ValueTask UploadTagListAsync(
        ConnectionManager connection, TagDatabase database, CancellationToken ct = default)
    {
        uint startInstance = 0;

        while (true)
        {
            var request = BuildGetInstanceAttributeListRequest(startInstance);
            var response = await connection.SendUnconnectedAsync(request, ct).ConfigureAwait(false);

            if (response.GeneralStatus != CipGeneralStatus.Success &&
                response.GeneralStatus != CipGeneralStatus.PartialTransfer)
            {
                // No more tags or error
                break;
            }

            var (lastInstanceId, tags) = ParseInstanceAttributeListResponse(response.Data);

            foreach (var tag in tags)
            {
                database.AddTag(tag);

                // Track program names
                if (tag.IsProgramScoped && tag.ProgramName != null)
                    database.AddProgram(tag.ProgramName);
            }

            if (!response.IsPartialTransfer || tags.Count == 0)
                break;

            // Continue from the next instance
            startInstance = lastInstanceId + 1;
        }
    }

    private static int[] BuildDimensions(uint dim0, uint dim1, uint dim2)
    {
        if (dim0 == 0)
            return [];
        if (dim1 == 0)
            return [(int)dim0];
        if (dim2 == 0)
            return [(int)dim0, (int)dim1];
        return [(int)dim0, (int)dim1, (int)dim2];
    }
}
