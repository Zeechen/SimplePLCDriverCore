using System.Buffers.Binary;
using SimplePLCDriverCore.Abstractions;
using SimplePLCDriverCore.Protocols.EtherNetIP.Cip;

namespace SimplePLCDriverCore.TypeSystem;

/// <summary>
/// Encodes PlcTagValue dictionaries (or .NET objects) into raw CIP structure bytes
/// for WriteTag operations on UDT tags.
/// </summary>
internal sealed class StructureEncoder
{
    private readonly TagDatabase _tagDatabase;

    public StructureEncoder(TagDatabase tagDatabase)
    {
        _tagDatabase = tagDatabase;
    }

    /// <summary>
    /// Encode a dictionary of member values into raw structure bytes.
    /// </summary>
    /// <param name="members">Dictionary of member name -> value.</param>
    /// <param name="templateInstanceId">Template instance ID to look up the UDT definition.</param>
    /// <returns>Encoded bytes ready for CIP WriteTag.</returns>
    public byte[] Encode(IReadOnlyDictionary<string, object> members, ushort templateInstanceId)
    {
        var udt = _tagDatabase.GetUdtByTemplateId(templateInstanceId)
            ?? throw new InvalidOperationException(
                $"UDT definition not found for template instance {templateInstanceId}");

        return EncodeStructure(members, udt);
    }

    /// <summary>
    /// Encode using a UDT definition directly.
    /// </summary>
    public byte[] EncodeStructure(IReadOnlyDictionary<string, object> members, UdtDefinition udt)
    {
        var result = new byte[udt.ByteSize];

        foreach (var member in udt.Members)
        {
            if (!members.TryGetValue(member.Name, out var value))
                continue;

            if (member.Offset >= result.Length)
                continue;

            if (member.IsStructure && member.Dimensions.Length > 0 && member.Dimensions[0] > 0)
            {
                EncodeStructureArrayMember(result.AsSpan(member.Offset), value, member);
            }
            else if (member.IsStructure)
            {
                EncodeNestedStructure(result.AsSpan(member.Offset), value, member);
            }
            else if (member.Dimensions.Length > 0 && member.Dimensions[0] > 0)
            {
                EncodeArrayMember(result.AsSpan(member.Offset), value, member);
            }
            else
            {
                EncodeAtomicMember(result.AsSpan(member.Offset), value, member);
            }
        }

        return result;
    }

    private void EncodeAtomicMember(Span<byte> target, object value, UdtMember member)
    {
        // Handle BOOL bit members
        if (member.DataType == PlcDataType.Bool && member.BitOffset >= 0)
        {
            var boolVal = Convert.ToBoolean(value);
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

        var cipType = PlcDataTypeToCipType(member.DataType);
        if (cipType == 0)
            return;

        var encoded = CipTypeCodec.EncodeValue(value, cipType);
        if (encoded.Length <= target.Length)
            encoded.CopyTo(target);
    }

    private void EncodeArrayMember(Span<byte> target, object value, UdtMember member)
    {
        var cipType = PlcDataTypeToCipType(member.DataType);
        if (cipType == 0)
            return;

        if (value is Array arr)
        {
            var objList = new object[arr.Length];
            for (var i = 0; i < arr.Length; i++)
                objList[i] = arr.GetValue(i)!;
            var encoded = CipTypeCodec.EncodeArray(objList, cipType);
            if (encoded.Length <= target.Length)
                encoded.CopyTo(target);
        }
        else if (value is IReadOnlyList<object> list)
        {
            var encoded = CipTypeCodec.EncodeArray(list, cipType);
            if (encoded.Length <= target.Length)
                encoded.CopyTo(target);
        }
    }

    private void EncodeStructureArrayMember(Span<byte> target, object value, UdtMember member)
    {
        if (member.TemplateInstanceId == 0)
            return;

        var nestedUdt = _tagDatabase.GetUdtByTemplateId(member.TemplateInstanceId);
        if (nestedUdt == null)
            return;

        var elementSize = nestedUdt.ByteSize;
        var maxElements = member.Dimensions[0];

        if (value is IReadOnlyList<IReadOnlyDictionary<string, object>> dictList)
        {
            for (var i = 0; i < Math.Min(dictList.Count, maxElements); i++)
            {
                var offset = i * elementSize;
                if (offset + elementSize > target.Length) break;
                var encoded = EncodeStructure(dictList[i], nestedUdt);
                encoded.CopyTo(target[offset..]);
            }
        }
        else if (value is Array arr)
        {
            for (var i = 0; i < Math.Min(arr.Length, maxElements); i++)
            {
                if (arr.GetValue(i) is IReadOnlyDictionary<string, object> elemDict)
                {
                    var offset = i * elementSize;
                    if (offset + elementSize > target.Length) break;
                    var encoded = EncodeStructure(elemDict, nestedUdt);
                    encoded.CopyTo(target[offset..]);
                }
            }
        }
    }

    private void EncodeNestedStructure(Span<byte> target, object value, UdtMember member)
    {
        if (member.TemplateInstanceId == 0)
            return;

        if (value is IReadOnlyDictionary<string, object> nestedMembers)
        {
            var nestedUdt = _tagDatabase.GetUdtByTemplateId(member.TemplateInstanceId);
            if (nestedUdt != null)
            {
                var encoded = EncodeStructure(nestedMembers, nestedUdt);
                if (encoded.Length <= target.Length)
                    encoded.CopyTo(target);
            }
        }
        else if (value is string strValue && member.TypeName.Contains("STRING", StringComparison.OrdinalIgnoreCase))
        {
            var encoded = CipTypeCodec.EncodeString(strValue);
            if (encoded.Length <= target.Length)
                encoded.CopyTo(target);
        }
    }

    private static ushort PlcDataTypeToCipType(PlcDataType dataType)
    {
        return dataType switch
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
}
