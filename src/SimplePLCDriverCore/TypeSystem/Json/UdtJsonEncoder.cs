using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using SimplePLCDriverCore.Abstractions;
using SimplePLCDriverCore.Protocols.EtherNetIP.Cip;

namespace SimplePLCDriverCore.TypeSystem.Json;

/// <summary>
/// Converts JSON directly to raw CIP structure bytes using UDT definitions.
/// No intermediate dictionary step. Supports partial writes via baseData.
/// </summary>
internal sealed class UdtJsonEncoder
{
    private readonly TagDatabase _tagDatabase;

    public UdtJsonEncoder(TagDatabase tagDatabase)
    {
        _tagDatabase = tagDatabase;
    }

    /// <summary>
    /// Encode a JSON string to raw structure bytes (full write, starts from zeros).
    /// </summary>
    public byte[] Encode(string json, ushort templateInstanceId)
    {
        var udt = _tagDatabase.GetUdtByTemplateId(templateInstanceId)
            ?? throw new InvalidOperationException(
                $"UDT definition not found for template instance {templateInstanceId}");

        using var doc = JsonDocument.Parse(json);
        var result = new byte[udt.ByteSize];
        EncodeStructure(doc.RootElement, udt, result);
        return result;
    }

    /// <summary>
    /// Encode a JSON string to raw structure bytes (partial write, patches over baseData).
    /// Members not present in the JSON are preserved from baseData.
    /// </summary>
    public byte[] Encode(string json, ushort templateInstanceId, byte[] baseData)
    {
        var udt = _tagDatabase.GetUdtByTemplateId(templateInstanceId)
            ?? throw new InvalidOperationException(
                $"UDT definition not found for template instance {templateInstanceId}");

        if (baseData.Length != udt.ByteSize)
            throw new ArgumentException(
                $"Base data length {baseData.Length} does not match UDT byte size {udt.ByteSize}");

        using var doc = JsonDocument.Parse(json);
        var result = (byte[])baseData.Clone();
        EncodeStructure(doc.RootElement, udt, result);
        return result;
    }

    private void EncodeStructure(JsonElement element, UdtDefinition udt, Span<byte> target)
    {
        if (element.ValueKind != JsonValueKind.Object)
            throw new ArgumentException($"Expected JSON object for UDT '{udt.Name}', got {element.ValueKind}");

        foreach (var member in udt.Members)
        {
            if (!TryGetProperty(element, member.Name, out var prop))
                continue; // Skip members not in JSON (enables partial writes)

            if (member.Offset >= target.Length)
                continue;

            var memberTarget = target[member.Offset..];

            if (member.IsStructure && member.Dimensions.Length > 0 && member.Dimensions[0] > 0)
            {
                EncodeStructureArrayMember(prop, member, memberTarget);
            }
            else if (member.IsStructure)
            {
                EncodeNestedStructure(prop, member, memberTarget);
            }
            else if (member.Dimensions.Length > 0 && member.Dimensions[0] > 0)
            {
                EncodeArrayMember(prop, member, memberTarget);
            }
            else
            {
                EncodeAtomicMember(prop, member, memberTarget);
            }
        }
    }

    private void EncodeAtomicMember(JsonElement element, UdtMember member, Span<byte> target)
    {
        // Handle BOOL bit members
        if (member.DataType == PlcDataType.Bool && member.BitOffset >= 0)
        {
            var boolVal = element.ValueKind == JsonValueKind.True ||
                          (element.ValueKind == JsonValueKind.Number && element.GetInt32() != 0);

            if (target.Length >= 4)
            {
                var word = BinaryPrimitives.ReadUInt32LittleEndian(target);
                if (boolVal)
                    word |= 1u << member.BitOffset;
                else
                    word &= ~(1u << member.BitOffset);
                BinaryPrimitives.WriteUInt32LittleEndian(target, word);
            }
            else if (target.Length >= 1)
            {
                var bit = member.BitOffset % 8;
                if (boolVal)
                    target[0] |= (byte)(1 << bit);
                else
                    target[0] &= (byte)~(1 << bit);
            }
            return;
        }

        switch (member.DataType)
        {
            case PlcDataType.Bool:
                if (target.Length >= 1)
                    target[0] = (byte)(element.ValueKind == JsonValueKind.True ||
                                       (element.ValueKind == JsonValueKind.Number && element.GetInt32() != 0) ? 1 : 0);
                break;
            case PlcDataType.Sint:
                if (target.Length >= 1)
                    target[0] = (byte)(sbyte)element.GetInt32();
                break;
            case PlcDataType.Int:
                if (target.Length >= 2)
                    BinaryPrimitives.WriteInt16LittleEndian(target, (short)element.GetInt32());
                break;
            case PlcDataType.Dint:
                if (target.Length >= 4)
                    BinaryPrimitives.WriteInt32LittleEndian(target, element.GetInt32());
                break;
            case PlcDataType.Lint:
                if (target.Length >= 8)
                    BinaryPrimitives.WriteInt64LittleEndian(target, element.GetInt64());
                break;
            case PlcDataType.Usint:
                if (target.Length >= 1)
                    target[0] = (byte)element.GetInt32();
                break;
            case PlcDataType.Uint:
                if (target.Length >= 2)
                    BinaryPrimitives.WriteUInt16LittleEndian(target, (ushort)element.GetInt32());
                break;
            case PlcDataType.Udint:
                if (target.Length >= 4)
                    BinaryPrimitives.WriteUInt32LittleEndian(target, element.GetUInt32());
                break;
            case PlcDataType.Ulint:
                if (target.Length >= 8)
                    BinaryPrimitives.WriteUInt64LittleEndian(target, element.GetUInt64());
                break;
            case PlcDataType.Real:
                if (target.Length >= 4)
                    BinaryPrimitives.WriteSingleLittleEndian(target, element.GetSingle());
                break;
            case PlcDataType.Lreal:
                if (target.Length >= 8)
                    BinaryPrimitives.WriteDoubleLittleEndian(target, element.GetDouble());
                break;
            case PlcDataType.String:
                EncodeStringValue(element.GetString() ?? string.Empty, target);
                break;
        }
    }

    private void EncodeArrayMember(JsonElement element, UdtMember member, Span<byte> target)
    {
        if (element.ValueKind != JsonValueKind.Array)
            return;

        var cipType = PlcDataTypeToCipType(member.DataType);
        var atomicSize = CipDataTypes.GetAtomicSize(cipType);
        if (atomicSize == 0) return;

        var maxElements = member.Dimensions[0];
        var i = 0;
        foreach (var item in element.EnumerateArray())
        {
            if (i >= maxElements) break;
            var offset = i * atomicSize;
            if (offset + atomicSize > target.Length) break;

            EncodeAtomicMember(item, new UdtMember
            {
                Name = string.Empty,
                DataType = member.DataType,
                TypeName = member.TypeName,
                Offset = 0,
                Size = atomicSize,
            }, target[offset..]);
            i++;
        }
    }

    private void EncodeNestedStructure(JsonElement element, UdtMember member, Span<byte> target)
    {
        // Handle STRING-type structures
        if (member.TypeName.Contains("STRING", StringComparison.OrdinalIgnoreCase) &&
            element.ValueKind == JsonValueKind.String)
        {
            EncodeStringValue(element.GetString() ?? string.Empty, target);
            return;
        }

        if (member.TemplateInstanceId == 0 || element.ValueKind != JsonValueKind.Object)
            return;

        var nestedUdt = _tagDatabase.GetUdtByTemplateId(member.TemplateInstanceId);
        if (nestedUdt == null) return;

        var nestedTarget = target.Length >= nestedUdt.ByteSize ? target[..nestedUdt.ByteSize] : target;
        EncodeStructure(element, nestedUdt, nestedTarget);
    }

    private void EncodeStructureArrayMember(JsonElement element, UdtMember member, Span<byte> target)
    {
        if (element.ValueKind != JsonValueKind.Array || member.TemplateInstanceId == 0)
            return;

        var nestedUdt = _tagDatabase.GetUdtByTemplateId(member.TemplateInstanceId);
        if (nestedUdt == null) return;

        var maxElements = member.Dimensions[0];
        var elementSize = nestedUdt.ByteSize;
        var i = 0;
        foreach (var item in element.EnumerateArray())
        {
            if (i >= maxElements) break;
            var offset = i * elementSize;
            if (offset + elementSize > target.Length) break;

            EncodeStructure(item, nestedUdt, target.Slice(offset, elementSize));
            i++;
        }
    }

    private static void EncodeStringValue(string value, Span<byte> target)
    {
        const int maxChars = 82;
        const int totalSize = 88;

        if (target.Length < totalSize) return;

        // Clear the string area
        target[..totalSize].Clear();

        var charCount = Math.Min(value.Length, maxChars);
        BinaryPrimitives.WriteInt32LittleEndian(target, charCount);
        Encoding.ASCII.GetBytes(value.AsSpan(0, charCount), target[4..]);
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement property)
    {
        // Try exact match first (most common)
        if (element.TryGetProperty(name, out property))
            return true;

        // Case-insensitive fallback
        foreach (var prop in element.EnumerateObject())
        {
            if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                property = prop.Value;
                return true;
            }
        }

        property = default;
        return false;
    }

    private static ushort PlcDataTypeToCipType(PlcDataType dataType) => dataType switch
    {
        PlcDataType.Bool => CipDataTypes.Bool,
        PlcDataType.Sint => CipDataTypes.Sint,
        PlcDataType.Int => CipDataTypes.Int,
        PlcDataType.Dint => CipDataTypes.Dint,
        PlcDataType.Lint => CipDataTypes.Lint,
        PlcDataType.Usint => CipDataTypes.Usint,
        PlcDataType.Uint => CipDataTypes.Uint,
        PlcDataType.Udint => CipDataTypes.Udint,
        PlcDataType.Ulint => CipDataTypes.Ulint,
        PlcDataType.Real => CipDataTypes.Real,
        PlcDataType.Lreal => CipDataTypes.Lreal,
        PlcDataType.String => CipDataTypes.String,
        _ => 0,
    };
}
