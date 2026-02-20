using System.Buffers.Binary;
using System.Text;
using SimplePLCDriverCore.Abstractions;
using SimplePLCDriverCore.Common;
using SimplePLCDriverCore.Common.Buffers;

namespace SimplePLCDriverCore.Protocols.EtherNetIP.Cip;

/// <summary>
/// Reads UDT (User Defined Type) definitions from the PLC using the CIP Template Object (Class 0x6C).
///
/// Template reading is a two-step process:
/// 1. Read template attributes (name, member count, byte size) via GetAttributeList
/// 2. Read member definitions (name, type, offset) via ReadTag service on the template instance
///
/// Template Attributes:
///   1 - Handle (UINT) - not useful
///   2 - Member Count (UINT)
///   4 - Template Definition Size (UDINT) - size in 32-bit words
///   5 - Template Structure Size (UDINT) - actual structure byte size in PLC memory
/// </summary>
internal static class TemplateObject
{
    // Attributes we request for the template header
    private static readonly ushort[] TemplateAttributes = [2, 4, 5];

    /// <summary>
    /// Build a GetAttributeList request for a Template Object instance.
    /// Returns member count, definition size, and structure byte size.
    /// </summary>
    public static byte[] BuildGetTemplateAttributesRequest(ushort templateInstanceId)
    {
        return CipMessage.BuildGetAttributeListRequest(
            CipClasses.Template, templateInstanceId, TemplateAttributes);
    }

    /// <summary>
    /// Build a ReadTag request to read the full template definition (member info).
    /// Uses ReadTagService on the Template class with byte offset for fragmented reads.
    /// </summary>
    public static byte[] BuildReadTemplateRequest(ushort templateInstanceId, uint offset, ushort byteCount)
    {
        var path = CipPath.BuildClassInstancePath(CipClasses.Template, templateInstanceId);

        using var writer = new PacketWriter(8 + path.Length);
        writer.WriteUInt8(CipServices.ReadTag);
        writer.WriteUInt8(CipPath.GetPathSizeInWords(path.Length));
        writer.WriteBytes(path);
        writer.WriteUInt32LE(offset);       // byte offset
        writer.WriteUInt16LE(byteCount);    // number of bytes to read

        return writer.ToArray();
    }

    /// <summary>
    /// Parse template attributes response.
    /// Returns (memberCount, definitionSizeWords, structureByteSize).
    /// </summary>
    public static (ushort MemberCount, uint DefinitionSizeWords, uint StructureByteSize)
        ParseTemplateAttributes(ReadOnlyMemory<byte> data)
    {
        var span = data.Span;
        var offset = 0;

        // Attribute count (2 bytes) - from GetAttributeList response
        if (span.Length < 2)
            throw new InvalidDataException("Template attributes response too short");

        var attrCount = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(offset));
        offset += 2;

        ushort memberCount = 0;
        uint defSizeWords = 0;
        uint structByteSize = 0;

        for (var i = 0; i < attrCount && offset < span.Length; i++)
        {
            if (offset + 4 > span.Length)
                break;

            var attrId = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(offset));
            offset += 2;
            var attrStatus = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(offset));
            offset += 2;

            if (attrStatus != 0)
                continue;

            switch (attrId)
            {
                case 2: // Member Count (UINT)
                    if (offset + 2 <= span.Length)
                    {
                        memberCount = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(offset));
                        offset += 2;
                    }
                    break;

                case 4: // Definition Size in 32-bit words (UDINT)
                    if (offset + 4 <= span.Length)
                    {
                        defSizeWords = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(offset));
                        offset += 4;
                    }
                    break;

                case 5: // Structure Byte Size (UDINT)
                    if (offset + 4 <= span.Length)
                    {
                        structByteSize = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(offset));
                        offset += 4;
                    }
                    break;
            }
        }

        return (memberCount, defSizeWords, structByteSize);
    }

    /// <summary>
    /// Parse the template definition data (member definitions + name string).
    ///
    /// Template definition format (for each member):
    ///   Member Info (2 bytes) - array size indicator
    ///   Member Type (2 bytes) - CIP type code
    ///   Member Offset (4 bytes LE) - byte offset within the structure
    ///
    /// After all member entries, the remaining data contains:
    ///   Template Name (null-terminated ASCII string)
    ///   Member Names (null-terminated ASCII strings, one per member)
    /// </summary>
    public static UdtDefinition ParseTemplateDefinition(
        ReadOnlySpan<byte> definitionData,
        ushort templateInstanceId,
        ushort memberCount,
        uint structureByteSize)
    {
        // Each member definition is 8 bytes
        var memberDefSize = memberCount * 8;
        if (definitionData.Length < memberDefSize)
            throw new InvalidDataException(
                $"Template definition too short: {definitionData.Length} < {memberDefSize}");

        // Read member definitions (type + offset)
        var memberDefs = new (ushort Info, ushort TypeCode, uint Offset)[memberCount];
        var offset = 0;

        for (var i = 0; i < memberCount; i++)
        {
            memberDefs[i].Info = BinaryPrimitives.ReadUInt16LittleEndian(definitionData.Slice(offset));
            offset += 2;
            memberDefs[i].TypeCode = BinaryPrimitives.ReadUInt16LittleEndian(definitionData.Slice(offset));
            offset += 2;
            memberDefs[i].Offset = BinaryPrimitives.ReadUInt32LittleEndian(definitionData.Slice(offset));
            offset += 4;
        }

        // Parse names section: template name followed by member names
        var namesData = definitionData[offset..];
        var names = ParseNullTerminatedStrings(namesData, memberCount + 1);

        var templateName = names.Count > 0 ? names[0] : $"Template_{templateInstanceId}";

        // Clean up template name (remove trailing ;n;m format Logix sometimes appends)
        var semiPos = templateName.IndexOf(';');
        if (semiPos > 0)
            templateName = templateName[..semiPos];

        // Predefined STRING type has internal name "ASCIISTRING82" — normalize to "STRING"
        if (string.Equals(templateName, "ASCIISTRING82", StringComparison.OrdinalIgnoreCase))
            templateName = "STRING";

        // Build member list
        var members = new List<UdtMember>();
        for (var i = 0; i < memberCount; i++)
        {
            var memberName = (i + 1 < names.Count) ? names[i + 1] : $"Member_{i}";

            // Skip hidden members (names starting with ZZZZZZZZZ... are Logix internal padding)
            if (memberName.StartsWith("ZZZZZZZZZ", StringComparison.Ordinal))
                continue;

            var memberTypeCode = memberDefs[i].TypeCode;
            var memberOffset = memberDefs[i].Offset;
            var memberInfo = memberDefs[i].Info;

            var isStructMember = CipDataTypes.IsStructure(memberTypeCode);
            var memberTemplateId = isStructMember
                ? CipDataTypes.GetTemplateInstanceId(memberTypeCode)
                : (ushort)0;

            // Determine array dimensions from memberInfo
            // memberInfo contains the array size for 1D arrays
            var memberDimensions = Array.Empty<int>();
            if (memberInfo > 0 && memberInfo != 1)
                memberDimensions = [(int)memberInfo];

            var atomicTypeCode = isStructMember ? memberTypeCode : (ushort)(memberTypeCode & 0x00FF);
            var memberTypeName = isStructMember ? "STRUCT" : CipDataTypes.GetTypeName(atomicTypeCode);
            var memberSize = isStructMember ? 0 : CipDataTypes.GetAtomicSize(atomicTypeCode);

            // Detect BOOL bit members
            var bitOffset = -1;
            if (atomicTypeCode == CipDataTypes.Bool && !isStructMember)
            {
                // For BOOLs in structures, the offset may encode bit position
                bitOffset = (int)(memberOffset % 32);
            }

            var memberDataType = isStructMember
                ? PlcDataType.Structure
                : CipTypeCodec.ToPlcDataType(atomicTypeCode);

            members.Add(new UdtMember
            {
                Name = memberName,
                DataType = memberDataType,
                TypeName = memberTypeName,
                Offset = (int)memberOffset,
                Size = memberSize,
                Dimensions = memberDimensions,
                BitOffset = bitOffset,
                IsStructure = isStructMember,
                TemplateInstanceId = memberTemplateId,
            });
        }

        return new UdtDefinition
        {
            Name = templateName,
            ByteSize = (int)structureByteSize,
            TemplateInstanceId = templateInstanceId,
            Members = members,
        };
    }

    /// <summary>
    /// Upload a single UDT definition from the PLC.
    /// </summary>
    public static async ValueTask<UdtDefinition?> UploadTemplateAsync(
        ConnectionManager connection,
        ushort templateInstanceId,
        CancellationToken ct = default)
    {
        // Step 1: Get template attributes (member count, sizes)
        var attrRequest = BuildGetTemplateAttributesRequest(templateInstanceId);
        var attrResponse = await connection.SendUnconnectedAsync(attrRequest, ct).ConfigureAwait(false);

        if (!attrResponse.IsSuccess)
            return null;

        var (memberCount, defSizeWords, structByteSize) =
            ParseTemplateAttributes(attrResponse.Data);

        if (memberCount == 0)
            return null;

        // Step 2: Read the full template definition (may require fragmented reads)
        var definitionByteSize = defSizeWords * 4;
        var allData = new List<byte>();
        uint readOffset = 0;

        while (readOffset < definitionByteSize)
        {
            var remaining = (ushort)Math.Min(definitionByteSize - readOffset, 500);
            var readRequest = BuildReadTemplateRequest(templateInstanceId, readOffset, remaining);
            var readResponse = await connection.SendUnconnectedAsync(readRequest, ct).ConfigureAwait(false);

            if (readResponse.GeneralStatus != CipGeneralStatus.Success &&
                readResponse.GeneralStatus != CipGeneralStatus.PartialTransfer)
            {
                break;
            }

            var responseData = readResponse.Data.ToArray();
            for (var i = 0; i < responseData.Length; i++)
                allData.Add(responseData[i]);

            readOffset += (uint)responseData.Length;

            if (!readResponse.IsPartialTransfer)
                break;
        }

        if (allData.Count == 0)
            return null;

        // Step 3: Parse the template definition
        return ParseTemplateDefinition(
            allData.ToArray(), templateInstanceId, memberCount, structByteSize);
    }

    /// <summary>
    /// Upload all UDT definitions referenced by tags in the database.
    /// </summary>
    public static async ValueTask UploadAllTemplatesAsync(
        ConnectionManager connection,
        TagDatabase database,
        CancellationToken ct = default)
    {
        // Collect all unique template IDs from structure tags
        var templateIds = new HashSet<ushort>();

        foreach (var tag in database.AllTags)
        {
            if (tag.IsStructure && tag.TemplateInstanceId > 0)
                templateIds.Add(tag.TemplateInstanceId);
        }

        // Upload each template and recursively upload all nested structure templates
        foreach (var templateId in templateIds)
        {
            await UploadTemplateRecursiveAsync(connection, database, templateId, ct)
                .ConfigureAwait(false);
        }

        // Update tag type names from loaded UDT definitions
        UpdateTagTypeNames(database);
    }

    /// <summary>
    /// Recursively upload a template and all nested structure templates.
    /// </summary>
    private static async ValueTask UploadTemplateRecursiveAsync(
        ConnectionManager connection,
        TagDatabase database,
        ushort templateId,
        CancellationToken ct)
    {
        if (database.GetUdtByTemplateId(templateId) != null)
            return; // Already loaded

        var definition = await UploadTemplateAsync(connection, templateId, ct).ConfigureAwait(false);
        if (definition == null)
            return;

        database.AddUdtDefinition(definition);

        // Recurse into nested structure members
        foreach (var member in definition.Members)
        {
            if (member.IsStructure && member.TemplateInstanceId > 0)
            {
                await UploadTemplateRecursiveAsync(connection, database, member.TemplateInstanceId, ct)
                    .ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Update tag TypeName and member TypeName from loaded UDT definitions.
    /// </summary>
    private static void UpdateTagTypeNames(TagDatabase database)
    {
        foreach (var tag in database.AllTags)
        {
            if (tag.IsStructure && tag.TemplateInstanceId > 0)
            {
                var udt = database.GetUdtByTemplateId(tag.TemplateInstanceId);
                if (udt != null)
                {
                    // Re-add the tag with updated type name
                    database.AddTag(new PlcTagInfo
                    {
                        Name = tag.Name,
                        DataType = tag.DataType,
                        TypeName = udt.Name,
                        Dimensions = tag.Dimensions,
                        IsStructure = tag.IsStructure,
                        IsProgramScoped = tag.IsProgramScoped,
                        ProgramName = tag.ProgramName,
                        InstanceId = tag.InstanceId,
                        RawTypeCode = tag.RawTypeCode,
                        TemplateInstanceId = tag.TemplateInstanceId,
                    });
                }
            }
        }
    }

    /// <summary>
    /// Parse a sequence of null-terminated ASCII strings.
    /// </summary>
    private static List<string> ParseNullTerminatedStrings(ReadOnlySpan<byte> data, int expectedCount)
    {
        var strings = new List<string>(expectedCount);
        var start = 0;

        for (var i = 0; i < data.Length && strings.Count < expectedCount; i++)
        {
            if (data[i] == 0)
            {
                if (i > start)
                    strings.Add(Encoding.ASCII.GetString(data.Slice(start, i - start)));
                else
                    strings.Add(string.Empty);
                start = i + 1;
            }
        }

        // Handle last string without null terminator
        if (start < data.Length && strings.Count < expectedCount)
            strings.Add(Encoding.ASCII.GetString(data[start..]));

        return strings;
    }
}
