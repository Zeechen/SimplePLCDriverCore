using SimplePLCDriverCore.Abstractions;
using SimplePLCDriverCore.Common;
using SimplePLCDriverCore.TypeSystem;

namespace SimplePLCDriverCore.Protocols.EtherNetIP.Cip;

/// <summary>
/// High-level tag read/write operations built on top of CIP messages.
/// Handles:
///   - Single and batch tag reads/writes
///   - Fragmented transfers for large data
///   - Type auto-detection from tag database
///   - Encoding/decoding via CipTypeCodec
///   - Structure (UDT) encoding via StructureEncoder
/// </summary>
internal sealed class TagOperations
{
    private readonly ConnectionManager _connection;
    private readonly TagDatabase _tagDatabase;
    private readonly StructureEncoder? _structureEncoder;

    public TagOperations(ConnectionManager connection, TagDatabase tagDatabase,
        StructureEncoder? structureEncoder = null)
    {
        _connection = connection;
        _tagDatabase = tagDatabase;
        _structureEncoder = structureEncoder;
    }

    /// <summary>
    /// Read a single tag. Type is auto-detected from the tag database.
    /// </summary>
    public async ValueTask<TagResult> ReadAsync(string tagName, CancellationToken ct = default)
    {
        try
        {
            var tagInfo = _tagDatabase.LookupTag(tagName);
            var elementCount = GetElementCount(tagName, tagInfo);

            var request = CipMessage.BuildReadTagRequest(tagName, elementCount);
            var response = await _connection.SendAsync(request, ct).ConfigureAwait(false);

            if (!response.IsSuccess)
                return TagResult.Failure(tagName, response.GetUserFriendlyMessage(), response.GetErrorMessage());

            return DecodeReadResponse(tagName, response.Data, tagInfo, elementCount);
        }
        catch (Exception ex)
        {
            return TagResult.Failure(tagName, ex.Message);
        }
    }

    /// <summary>
    /// Write a single tag. Type is auto-detected from the tag database.
    /// Supports atomic types, arrays, full UDT writes (via dictionary), and UDT member writes (dot notation).
    /// </summary>
    public async ValueTask<TagResult> WriteAsync(
        string tagName, object value, CancellationToken ct = default)
    {
        try
        {
            var tagInfo = _tagDatabase.LookupTag(tagName);
            var typeCode = tagInfo?.RawTypeCode ?? 0;

            // For UDT member paths (e.g., "MyUDT.IntField"), resolve the member's type
            // instead of using the parent UDT's structure type code
            if (typeCode != 0 && CipDataTypes.IsStructure(typeCode) && IsMemberPath(tagName, tagInfo))
            {
                var memberTypeCode = _tagDatabase.ResolveMemberTypeCode(tagName);
                if (memberTypeCode != 0)
                    typeCode = memberTypeCode;
            }

            if (typeCode == 0)
            {
                // No tag info available - try to infer type from the .NET value
                typeCode = InferTypeCode(value);
                if (typeCode == 0)
                    return TagResult.Failure(tagName,
                        "Tag type unknown. Connect and upload tag database first, or use a typed overload.");
            }

            byte[] encodedData;
            ushort elementCount = 1;

            // Handle STRING structure type with a plain string value
            if (CipDataTypes.IsStructure(typeCode) && value is string strValue && IsStringType(typeCode))
            {
                encodedData = EncodeStringStructure(strValue, typeCode);
            }
            // Handle full UDT write via dictionary
            else if (CipDataTypes.IsStructure(typeCode) && value is IDictionary<string, object> dictValue)
            {
                if (_structureEncoder == null)
                    return TagResult.Failure(tagName, "Structure encoder not available. Ensure the driver is connected.");

                var templateId = CipDataTypes.GetTemplateInstanceId(typeCode);
                encodedData = _structureEncoder.Encode(
                    new ReadOnlyDictionaryWrapper(dictValue), templateId);
            }
            else if (CipDataTypes.IsStructure(typeCode) && value is IReadOnlyDictionary<string, object> roDict)
            {
                if (_structureEncoder == null)
                    return TagResult.Failure(tagName, "Structure encoder not available. Ensure the driver is connected.");

                var templateId = CipDataTypes.GetTemplateInstanceId(typeCode);
                encodedData = _structureEncoder.Encode(roDict, templateId);
            }
            else if (value is IReadOnlyList<object> arrayValues)
            {
                encodedData = CipTypeCodec.EncodeArray(arrayValues, typeCode);
                elementCount = (ushort)arrayValues.Count;
            }
            else if (value is Array arr)
            {
                var objList = new object[arr.Length];
                for (var i = 0; i < arr.Length; i++)
                    objList[i] = arr.GetValue(i)!;
                encodedData = CipTypeCodec.EncodeArray(objList, typeCode);
                elementCount = (ushort)arr.Length;
            }
            else
            {
                encodedData = CipTypeCodec.EncodeValue(value, typeCode);
            }

            var request = CipMessage.BuildWriteTagRequest(
                tagName, typeCode, elementCount, encodedData);

            var response = await _connection.SendAsync(request, ct).ConfigureAwait(false);

            if (!response.IsSuccess)
                return TagResult.Failure(tagName, response.GetUserFriendlyMessage(), response.GetErrorMessage());

            var typeName = tagInfo?.TypeName ?? CipDataTypes.GetTypeName(typeCode);
            return TagResult.Success(tagName, PlcTagValue.Null, typeName);
        }
        catch (Exception ex)
        {
            return TagResult.Failure(tagName, ex.Message);
        }
    }

    /// <summary>
    /// Check if the tag path is accessing a member of a UDT (contains dot notation
    /// that goes beyond the base tag name).
    /// </summary>
    private static bool IsMemberPath(string tagName, PlcTagInfo? tagInfo)
    {
        if (tagInfo == null)
            return false;

        // If the tagName is the same as the tag info name, it's not a member path
        if (string.Equals(tagName, tagInfo.Name, StringComparison.OrdinalIgnoreCase))
            return false;

        // Check if there's a dot after the base tag name (ignoring array indices)
        var afterBase = tagName[tagInfo.Name.Length..];
        if (afterBase.StartsWith('['))
        {
            var closeBracket = afterBase.IndexOf(']');
            if (closeBracket >= 0)
                afterBase = afterBase[(closeBracket + 1)..];
        }

        return afterBase.StartsWith('.');
    }

    /// <summary>
    /// Read multiple tags in batch using Multiple Service Packet.
    /// </summary>
    public async ValueTask<TagResult[]> ReadBatchAsync(
        IReadOnlyList<string> tagNames, CancellationToken ct = default)
    {
        if (tagNames.Count == 0)
            return [];

        if (tagNames.Count == 1)
            return [await ReadAsync(tagNames[0], ct).ConfigureAwait(false)];

        // Build individual read requests
        var requests = new byte[tagNames.Count][];
        var tagInfos = new PlcTagInfo?[tagNames.Count];
        var elementCounts = new ushort[tagNames.Count];

        for (var i = 0; i < tagNames.Count; i++)
        {
            tagInfos[i] = _tagDatabase.LookupTag(tagNames[i]);
            elementCounts[i] = GetElementCount(tagNames[i], tagInfos[i]);
            requests[i] = CipMessage.BuildReadTagRequest(tagNames[i], elementCounts[i]);
        }

        // Send as batch
        CipResponse[] responses;
        try
        {
            responses = await _connection.SendBatchAsync(requests, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return tagNames.Select(t => TagResult.Failure(t, ex.Message)).ToArray();
        }

        // Decode responses
        var results = new TagResult[tagNames.Count];
        for (var i = 0; i < tagNames.Count; i++)
        {
            if (!responses[i].IsSuccess)
            {
                results[i] = TagResult.Failure(tagNames[i], responses[i].GetUserFriendlyMessage(), responses[i].GetErrorMessage());
            }
            else
            {
                try
                {
                    results[i] = DecodeReadResponse(
                        tagNames[i], responses[i].Data, tagInfos[i], elementCounts[i]);
                }
                catch (Exception ex)
                {
                    results[i] = TagResult.Failure(tagNames[i], $"Decode error: {ex.Message}");
                }
            }
        }

        return results;
    }

    /// <summary>
    /// Write multiple tags in batch using Multiple Service Packet.
    /// </summary>
    public async ValueTask<TagResult[]> WriteBatchAsync(
        IReadOnlyList<(string TagName, object Value)> tags, CancellationToken ct = default)
    {
        if (tags.Count == 0)
            return [];

        if (tags.Count == 1)
            return [await WriteAsync(tags[0].TagName, tags[0].Value, ct).ConfigureAwait(false)];

        // Build individual write requests
        var requests = new byte[tags.Count][];
        var typeNames = new string[tags.Count];
        var errors = new string?[tags.Count];

        for (var i = 0; i < tags.Count; i++)
        {
            try
            {
                var (tagName, value) = tags[i];
                var tagInfo = _tagDatabase.LookupTag(tagName);
                var typeCode = tagInfo?.RawTypeCode ?? InferTypeCode(value);

                // Resolve member type for UDT member paths
                if (typeCode != 0 && CipDataTypes.IsStructure(typeCode) && IsMemberPath(tagName, tagInfo))
                {
                    var memberTypeCode = _tagDatabase.ResolveMemberTypeCode(tagName);
                    if (memberTypeCode != 0)
                        typeCode = memberTypeCode;
                }

                if (typeCode == 0)
                {
                    errors[i] = "Tag type unknown";
                    requests[i] = []; // placeholder
                    continue;
                }

                typeNames[i] = tagInfo?.TypeName ?? CipDataTypes.GetTypeName(typeCode);

                byte[] encodedData;
                ushort elementCount = 1;

                if (CipDataTypes.IsStructure(typeCode) && value is string strValue && IsStringType(typeCode))
                {
                    encodedData = EncodeStringStructure(strValue, typeCode);
                }
                else if (CipDataTypes.IsStructure(typeCode) && value is IDictionary<string, object> dictValue)
                {
                    if (_structureEncoder == null) { errors[i] = "Structure encoder not available"; requests[i] = []; continue; }
                    var templateId = CipDataTypes.GetTemplateInstanceId(typeCode);
                    encodedData = _structureEncoder.Encode(new ReadOnlyDictionaryWrapper(dictValue), templateId);
                }
                else if (CipDataTypes.IsStructure(typeCode) && value is IReadOnlyDictionary<string, object> roDict)
                {
                    if (_structureEncoder == null) { errors[i] = "Structure encoder not available"; requests[i] = []; continue; }
                    var templateId = CipDataTypes.GetTemplateInstanceId(typeCode);
                    encodedData = _structureEncoder.Encode(roDict, templateId);
                }
                else if (value is Array arr)
                {
                    var objList = new object[arr.Length];
                    for (var j = 0; j < arr.Length; j++)
                        objList[j] = arr.GetValue(j)!;
                    encodedData = CipTypeCodec.EncodeArray(objList, typeCode);
                    elementCount = (ushort)arr.Length;
                }
                else
                {
                    encodedData = CipTypeCodec.EncodeValue(value, typeCode);
                }

                requests[i] = CipMessage.BuildWriteTagRequest(
                    tagName, typeCode, elementCount, encodedData);
            }
            catch (Exception ex)
            {
                errors[i] = ex.Message;
                requests[i] = [];
            }
        }

        // Filter out requests with pre-build errors
        var validIndices = new List<int>();
        var validRequests = new List<byte[]>();
        for (var i = 0; i < requests.Length; i++)
        {
            if (errors[i] == null && requests[i].Length > 0)
            {
                validIndices.Add(i);
                validRequests.Add(requests[i]);
            }
        }

        // Send valid requests as batch
        CipResponse[] responses = [];
        if (validRequests.Count > 0)
        {
            try
            {
                responses = await _connection.SendBatchAsync(validRequests, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                return tags.Select(t => TagResult.Failure(t.TagName, ex.Message)).ToArray();
            }
        }

        // Build results
        var results = new TagResult[tags.Count];
        var responseIdx = 0;

        for (var i = 0; i < tags.Count; i++)
        {
            if (errors[i] != null)
            {
                results[i] = TagResult.Failure(tags[i].TagName, errors[i]!);
            }
            else if (responseIdx < responses.Length)
            {
                var resp = responses[responseIdx++];
                results[i] = resp.IsSuccess
                    ? TagResult.Success(tags[i].TagName, PlcTagValue.Null, typeNames[i])
                    : TagResult.Failure(tags[i].TagName, resp.GetUserFriendlyMessage(), resp.GetErrorMessage());
            }
            else
            {
                results[i] = TagResult.Failure(tags[i].TagName, "No response received");
            }
        }

        return results;
    }

    /// <summary>
    /// Read a large tag value using fragmented reads.
    /// Handles automatic continuation when the PLC returns PartialTransfer status.
    /// </summary>
    public async ValueTask<TagResult> ReadFragmentedAsync(
        string tagName, ushort elementCount, CancellationToken ct = default)
    {
        var tagInfo = _tagDatabase.LookupTag(tagName);
        var allData = new List<byte>();
        uint offset = 0;
        ushort typeCode = 0;

        while (true)
        {
            var request = CipMessage.BuildReadTagFragmentedRequest(tagName, elementCount, offset);
            var response = await _connection.SendAsync(request, ct).ConfigureAwait(false);

            if (response.GeneralStatus != CipGeneralStatus.Success &&
                response.GeneralStatus != CipGeneralStatus.PartialTransfer)
            {
                return TagResult.Failure(tagName, response.GetUserFriendlyMessage(), response.GetErrorMessage());
            }

            var dataArray = response.Data.ToArray();
            if (dataArray.Length >= 2)
            {
                int dataStart = 0;
                // First fragment includes type code (and structure handle for structures)
                if (typeCode == 0)
                {
                    typeCode = System.Buffers.Binary.BinaryPrimitives
                        .ReadUInt16LittleEndian(dataArray.AsSpan());

                    if (typeCode == CipDataTypes.AbbreviatedStructureType && dataArray.Length >= 4)
                        dataStart = 4; // skip type (2) + structure handle (2)
                    else
                        dataStart = 2; // skip type code only
                }

                for (var j = dataStart; j < dataArray.Length; j++)
                    allData.Add(dataArray[j]);
            }

            offset = (uint)allData.Count;

            if (!response.IsPartialTransfer)
                break;
        }

        // Decode the complete data
        try
        {
            var combinedData = allData.ToArray();
            PlcTagValue value;
            var typeName = tagInfo?.TypeName ?? CipDataTypes.GetTypeName(typeCode);

            if (typeCode == CipDataTypes.String || IsStringType(typeCode))
            {
                value = CipTypeCodec.DecodeString(combinedData);
                typeName = "STRING";
            }
            else if (typeCode == CipDataTypes.AbbreviatedStructureType
                     && tagInfo == null
                     && CipDataTypes.IsLikelyStringData(combinedData))
            {
                // Fallback: detect STRING from data pattern when database is empty
                value = CipTypeCodec.DecodeString(combinedData);
                typeName = "STRING";
            }
            else if (elementCount > 1)
            {
                value = CipTypeCodec.DecodeArray(combinedData, typeCode, elementCount);
            }
            else
            {
                value = CipTypeCodec.DecodeAtomicValue(combinedData, typeCode);
            }

            return TagResult.Success(tagName, value, typeName);
        }
        catch (Exception ex)
        {
            return TagResult.Failure(tagName, $"Decode error: {ex.Message}");
        }
    }

    /// <summary>
    /// Read raw CIP response bytes for a tag (type code + data).
    /// Used by JSON APIs that decode directly from bytes.
    /// </summary>
    public async ValueTask<(ushort TypeCode, byte[] Data, string? Error, string? ErrorDetail)> ReadRawAsync(
        string tagName, CancellationToken ct = default)
    {
        try
        {
            var tagInfo = _tagDatabase.LookupTag(tagName);
            var elementCount = GetElementCount(tagName, tagInfo);

            var request = CipMessage.BuildReadTagRequest(tagName, elementCount);
            var response = await _connection.SendAsync(request, ct).ConfigureAwait(false);

            if (!response.IsSuccess)
                return (0, [], response.GetUserFriendlyMessage(), response.GetErrorMessage());

            var dataArray = response.Data.ToArray();
            if (dataArray.Length < 2)
                return (0, [], "Response too short", null);

            var typeCode = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(dataArray);

            // CIP ReadTag response for structures uses abbreviated type 0x02A0
            // followed by a 2-byte structure handle — skip 4 bytes total.
            int dataStart;
            if (typeCode == CipDataTypes.AbbreviatedStructureType)
            {
                if (dataArray.Length < 4)
                    return (0, [], "Structure response too short", null);
                dataStart = 4;
            }
            else
            {
                dataStart = 2;
            }

            var valueData = dataArray[dataStart..];
            return (typeCode, valueData, null, null);
        }
        catch (Exception ex)
        {
            return (0, [], ex.Message, null);
        }
    }

    /// <summary>
    /// Write pre-encoded raw structure bytes to a tag.
    /// Used by JSON/typed write APIs that encode directly to bytes.
    /// </summary>
    public async ValueTask<TagResult> WriteRawStructureAsync(
        string tagName, ushort typeCode, byte[] data, CancellationToken ct = default)
    {
        try
        {
            var request = CipMessage.BuildWriteTagRequest(tagName, typeCode, 1, data);
            var response = await _connection.SendAsync(request, ct).ConfigureAwait(false);

            if (!response.IsSuccess)
                return TagResult.Failure(tagName, response.GetUserFriendlyMessage(), response.GetErrorMessage());

            var tagInfo = _tagDatabase.LookupTag(tagName);
            var typeName = tagInfo?.TypeName ?? CipDataTypes.GetTypeName(typeCode);
            return TagResult.Success(tagName, PlcTagValue.Null, typeName);
        }
        catch (Exception ex)
        {
            return TagResult.Failure(tagName, ex.Message);
        }
    }

    // --- Helpers ---

    private TagResult DecodeReadResponse(
        string tagName, ReadOnlyMemory<byte> responseData,
        PlcTagInfo? tagInfo, int elementCount)
    {
        var (value, typeCode) = CipTypeCodec.DecodeReadResponse(
            responseData.Span, elementCount);

        var isMemberPath = IsMemberPath(tagName, tagInfo);
        var isAbbreviatedStruct = (typeCode == CipDataTypes.AbbreviatedStructureType);

        // The CIP ReadTag response uses abbreviated type 0x02A0 for structures,
        // which doesn't contain the template ID. Resolve from the tag database.
        var resolvedTypeCode = typeCode;

        if (isAbbreviatedStruct && !isMemberPath && tagInfo != null)
        {
            // Direct structure tag: use the tag's own type code from the database
            resolvedTypeCode = tagInfo.RawTypeCode;
        }

        if (isMemberPath)
        {
            // Member path (e.g., "MyUDT.Field"): resolve from UDT definition chain
            var memberTypeCode = _tagDatabase.ResolveMemberTypeCode(tagName);
            if (memberTypeCode != 0)
                resolvedTypeCode = memberTypeCode;
        }

        // Determine type name from the resolved type code
        var typeName = (isMemberPath || isAbbreviatedStruct)
            ? ResolveTypeName(resolvedTypeCode)
            : tagInfo?.TypeName ?? ResolveTypeName(typeCode);

        // Fallback: try resolving from UDT definition chain when type is still unhelpful
        if (typeName is "UNKNOWN" or "STRUCT")
        {
            var memberTypeCode = _tagDatabase.ResolveMemberTypeCode(tagName);
            if (memberTypeCode != 0)
            {
                resolvedTypeCode = memberTypeCode;
                typeName = ResolveTypeName(memberTypeCode);
            }
        }

        // STRING in Logix PLCs is always a structure (0x8xxx), not the atomic 0x00DA.
        // Detect STRING structures and decode to readable text automatically.
        if (value.DataType == PlcDataType.Structure && value.RawValue is byte[] rawBytes
            && IsStringType(resolvedTypeCode))
        {
            value = CipTypeCodec.DecodeString(rawBytes);
            typeName = "STRING";
        }

        // Fallback: detect STRING from data pattern when tag database is empty or
        // the tag was not found. Standard STRING is 88 bytes with a valid LEN prefix.
        if (value.DataType == PlcDataType.Structure && value.RawValue is byte[] fallbackBytes
            && isAbbreviatedStruct && tagInfo == null
            && CipDataTypes.IsLikelyStringData(fallbackBytes))
        {
            value = CipTypeCodec.DecodeString(fallbackBytes);
            typeName = "STRING";
        }

        return TagResult.Success(tagName, value, typeName);
    }

    /// <summary>
    /// Check if a structure type code represents a STRING type by looking up
    /// the template in the tag database.
    /// </summary>
    private bool IsStringType(ushort typeCode)
    {
        if (!CipDataTypes.IsStructure(typeCode))
            return false;

        var templateId = CipDataTypes.GetTemplateInstanceId(typeCode);
        return _tagDatabase.IsStringTemplate(templateId);
    }

    /// <summary>
    /// Resolve a human-readable type name from a CIP type code.
    /// For structure types, looks up the UDT name in the tag database.
    /// </summary>
    private string ResolveTypeName(ushort typeCode)
    {
        if (CipDataTypes.IsStructure(typeCode))
        {
            var templateId = CipDataTypes.GetTemplateInstanceId(typeCode);
            var udt = _tagDatabase.GetUdtByTemplateId(templateId);
            if (udt != null)
                return udt.Name;
        }

        return CipDataTypes.GetTypeName(typeCode);
    }

    /// <summary>
    /// Encode a .NET string into the Logix STRING structure format.
    /// Uses the UDT byte size from the tag database to support custom string types.
    /// </summary>
    private byte[] EncodeStringStructure(string value, ushort typeCode)
    {
        var templateId = CipDataTypes.GetTemplateInstanceId(typeCode);
        var udt = _tagDatabase.GetUdtByTemplateId(templateId);
        var totalSize = udt?.ByteSize ?? 88; // default to standard STRING (88 bytes)
        var maxChars = totalSize - 4; // subtract 4 for the DINT length prefix

        var result = new byte[totalSize];
        var charCount = Math.Min(value.Length, maxChars);

        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(result, charCount);
        System.Text.Encoding.ASCII.GetBytes(value.AsSpan(0, charCount), result.AsSpan(4));

        return result;
    }

    /// <summary>
    /// Determine the element count for a read request.
    /// For array tags with indices, count is 1 (single element).
    /// For array tags without indices, could read the whole array.
    /// For scalars, count is 1.
    /// </summary>
    private static ushort GetElementCount(string tagName, PlcTagInfo? tagInfo)
    {
        // If tag name includes array index (e.g., "MyArray[5]"), read 1 element
        if (tagName.Contains('['))
            return 1;

        // If it's a known array tag, read 1 element (user can request more explicitly)
        return 1;
    }

    /// <summary>
    /// Try to infer the CIP type code from a .NET value when tag metadata is not available.
    /// </summary>
    private static ushort InferTypeCode(object value)
    {
        return value switch
        {
            bool => CipDataTypes.Bool,
            sbyte => CipDataTypes.Sint,
            short => CipDataTypes.Int,
            int => CipDataTypes.Dint,
            long => CipDataTypes.Lint,
            byte => CipDataTypes.Usint,
            ushort => CipDataTypes.Uint,
            uint => CipDataTypes.Udint,
            ulong => CipDataTypes.Ulint,
            float => CipDataTypes.Real,
            double => CipDataTypes.Lreal,
            string => CipDataTypes.String,
            _ => 0,
        };
    }
}

/// <summary>
/// Lightweight wrapper to adapt IDictionary to IReadOnlyDictionary.
/// </summary>
internal sealed class ReadOnlyDictionaryWrapper : IReadOnlyDictionary<string, object>
{
    private readonly IDictionary<string, object> _inner;

    public ReadOnlyDictionaryWrapper(IDictionary<string, object> inner) => _inner = inner;

    public object this[string key] => _inner[key];
    public IEnumerable<string> Keys => _inner.Keys;
    public IEnumerable<object> Values => _inner.Values;
    public int Count => _inner.Count;
    public bool ContainsKey(string key) => _inner.ContainsKey(key);
    public bool TryGetValue(string key, out object value) => _inner.TryGetValue(key, out value!);
    public IEnumerator<KeyValuePair<string, object>> GetEnumerator() => _inner.GetEnumerator();
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => _inner.GetEnumerator();
}
