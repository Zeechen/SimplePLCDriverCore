using System.Buffers.Binary;
using SimplePLCDriverCore.Abstractions;
using SimplePLCDriverCore.Protocols.EtherNetIP.Cip;

namespace SimplePLCDriverCore.TypeSystem;

/// <summary>
/// Decodes raw CIP structure bytes into PlcTagValue dictionaries using UDT definitions.
/// Handles nested structures, arrays within structures, and bit-packed BOOLs.
/// </summary>
internal sealed class StructureDecoder
{
    private readonly TagDatabase _tagDatabase;

    public StructureDecoder(TagDatabase tagDatabase)
    {
        _tagDatabase = tagDatabase;
    }

    /// <summary>
    /// Decode raw structure bytes into a PlcTagValue dictionary using the UDT definition.
    /// STRING structures are automatically decoded to text.
    /// </summary>
    /// <param name="data">Raw bytes of the structure (from a ReadTag response, after type code).</param>
    /// <param name="templateInstanceId">Template instance ID to look up the UDT definition.</param>
    /// <returns>PlcTagValue containing a dictionary of member name -> value, or a string for STRING types.</returns>
    public PlcTagValue Decode(ReadOnlySpan<byte> data, ushort templateInstanceId)
    {
        var udt = _tagDatabase.GetUdtByTemplateId(templateInstanceId);
        if (udt == null)
            return new PlcTagValue(data.ToArray(), PlcDataType.Structure);

        // STRING structures are decoded as text, not as UDT dictionaries
        if (TagDatabase.IsStringUdt(udt))
            return CipTypeCodec.DecodeString(data);

        return DecodeStructure(data, udt);
    }

    /// <summary>
    /// Decode a structure using the given UDT definition.
    /// </summary>
    public PlcTagValue DecodeStructure(ReadOnlySpan<byte> data, UdtDefinition udt)
    {
        var members = new Dictionary<string, PlcTagValue>(StringComparer.OrdinalIgnoreCase);

        foreach (var member in udt.Members)
        {
            if (member.Offset >= data.Length)
                continue;

            var memberData = data[member.Offset..];

            PlcTagValue value;
            if (member.IsStructure && member.Dimensions.Length > 0 && member.Dimensions[0] > 0)
            {
                value = DecodeStructureArrayMember(memberData, member);
            }
            else if (member.IsStructure)
            {
                value = DecodeNestedStructure(memberData, member);
            }
            else if (member.Dimensions.Length > 0 && member.Dimensions[0] > 0)
            {
                value = DecodeArrayMember(memberData, member);
            }
            else
            {
                value = DecodeAtomicMember(memberData, member);
            }

            members[member.Name] = value;
        }

        return PlcTagValue.FromStructure(members);
    }

    private PlcTagValue DecodeAtomicMember(ReadOnlySpan<byte> data, UdtMember member)
    {
        // Handle BOOL bit members
        if (member.DataType == PlcDataType.Bool && member.BitOffset >= 0)
        {
            if (data.Length >= 4)
            {
                var word = BinaryPrimitives.ReadUInt32LittleEndian(data);
                var bitValue = (word & (1u << member.BitOffset)) != 0;
                return PlcTagValue.FromBool(bitValue);
            }
            if (data.Length >= 1)
                return PlcTagValue.FromBool((data[0] & (1 << (member.BitOffset % 8))) != 0);
            return PlcTagValue.FromBool(false);
        }

        // Map PlcDataType to CIP type code for decoding
        var cipType = PlcDataTypeToCipType(member.DataType);
        if (cipType == 0)
            return new PlcTagValue(data.Length > 0 ? data.ToArray() : [], PlcDataType.Unknown);

        var atomicSize = CipDataTypes.GetAtomicSize(cipType);
        if (atomicSize == 0 || data.Length < atomicSize)
            return new PlcTagValue(data.Length > 0 ? data.ToArray() : [], PlcDataType.Unknown);

        if (cipType == CipDataTypes.String)
            return CipTypeCodec.DecodeString(data);

        return CipTypeCodec.DecodeAtomicValue(data[..atomicSize], cipType);
    }

    private PlcTagValue DecodeArrayMember(ReadOnlySpan<byte> data, UdtMember member)
    {
        var elementCount = member.Dimensions[0];
        var cipType = PlcDataTypeToCipType(member.DataType);

        if (cipType == 0 || elementCount == 0)
            return new PlcTagValue(data.ToArray(), PlcDataType.Unknown);

        var atomicSize = CipDataTypes.GetAtomicSize(cipType);
        if (atomicSize == 0)
            return new PlcTagValue(data.ToArray(), PlcDataType.Unknown);

        return CipTypeCodec.DecodeArray(data, cipType, elementCount);
    }

    private PlcTagValue DecodeStructureArrayMember(ReadOnlySpan<byte> data, UdtMember member)
    {
        var nestedUdt = _tagDatabase.GetUdtByTemplateId(member.TemplateInstanceId);
        if (nestedUdt == null)
            return new PlcTagValue(data.ToArray(), PlcDataType.Structure);

        var isString = TagDatabase.IsStringUdt(nestedUdt);
        var elementCount = member.Dimensions[0];
        var elementSize = nestedUdt.ByteSize;
        var elements = new PlcTagValue[elementCount];

        for (var i = 0; i < elementCount && (i * elementSize + elementSize) <= data.Length; i++)
        {
            var elementData = data.Slice(i * elementSize, elementSize);
            elements[i] = isString
                ? CipTypeCodec.DecodeString(elementData)
                : DecodeStructure(elementData, nestedUdt);
        }

        return new PlcTagValue(elements, isString ? PlcDataType.String : PlcDataType.Structure);
    }

    private PlcTagValue DecodeNestedStructure(ReadOnlySpan<byte> data, UdtMember member)
    {
        if (member.TemplateInstanceId == 0)
        {
            // Fallback: STRING-like structures without template ID
            if (member.TypeName.Contains("STRING", StringComparison.OrdinalIgnoreCase) && data.Length >= 4)
                return CipTypeCodec.DecodeString(data);
            return new PlcTagValue(data.ToArray(), PlcDataType.Structure);
        }

        var nestedUdt = _tagDatabase.GetUdtByTemplateId(member.TemplateInstanceId);
        if (nestedUdt == null)
        {
            if (member.TypeName.Contains("STRING", StringComparison.OrdinalIgnoreCase) && data.Length >= 4)
                return CipTypeCodec.DecodeString(data);
            return new PlcTagValue(data.ToArray(), PlcDataType.Structure);
        }

        // STRING structures are decoded as text, not as UDT dictionaries
        if (TagDatabase.IsStringUdt(nestedUdt))
        {
            var strData = data.Length >= nestedUdt.ByteSize ? data[..nestedUdt.ByteSize] : data;
            return CipTypeCodec.DecodeString(strData);
        }

        var nestedData = data.Length >= nestedUdt.ByteSize
            ? data[..nestedUdt.ByteSize]
            : data;

        return DecodeStructure(nestedData, nestedUdt);
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
