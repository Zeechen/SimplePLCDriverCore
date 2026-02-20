using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using SimplePLCDriverCore.Abstractions;
using SimplePLCDriverCore.Protocols.EtherNetIP.Cip;

namespace SimplePLCDriverCore.TypeSystem.Json;

/// <summary>
/// Converts raw CIP structure bytes directly to JSON strings using UDT definitions.
/// No intermediate PlcTagValue or dictionary step.
/// </summary>
internal sealed class UdtJsonDecoder
{
    private readonly TagDatabase _tagDatabase;

    public UdtJsonDecoder(TagDatabase tagDatabase)
    {
        _tagDatabase = tagDatabase;
    }

    /// <summary>
    /// Decode raw structure bytes to a JSON string.
    /// </summary>
    public string ToJson(ReadOnlySpan<byte> data, ushort templateInstanceId, bool indented = false)
    {
        var udt = _tagDatabase.GetUdtByTemplateId(templateInstanceId)
            ?? throw new InvalidOperationException(
                $"UDT definition not found for template instance {templateInstanceId}");

        using var stream = new MemoryStream();
        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = indented });
        WriteStructure(writer, data, udt);
        writer.Flush();
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private void WriteStructure(Utf8JsonWriter writer, ReadOnlySpan<byte> data, UdtDefinition udt)
    {
        writer.WriteStartObject();

        foreach (var member in udt.Members)
        {
            if (member.Offset >= data.Length)
                continue;

            var memberData = data[member.Offset..];

            writer.WritePropertyName(member.Name);

            if (member.IsStructure && member.Dimensions.Length > 0 && member.Dimensions[0] > 0)
            {
                WriteStructureArrayMember(writer, memberData, member);
            }
            else if (member.IsStructure)
            {
                WriteNestedStructure(writer, memberData, member);
            }
            else if (member.Dimensions.Length > 0 && member.Dimensions[0] > 0)
            {
                WriteArrayMember(writer, memberData, member);
            }
            else
            {
                WriteAtomicMember(writer, memberData, member);
            }
        }

        writer.WriteEndObject();
    }

    private void WriteAtomicMember(Utf8JsonWriter writer, ReadOnlySpan<byte> data, UdtMember member)
    {
        // Handle BOOL bit members
        if (member.DataType == PlcDataType.Bool && member.BitOffset >= 0)
        {
            bool bitValue;
            if (data.Length >= 4)
            {
                var word = BinaryPrimitives.ReadUInt32LittleEndian(data);
                bitValue = (word & (1u << member.BitOffset)) != 0;
            }
            else if (data.Length >= 1)
            {
                bitValue = (data[0] & (1 << (member.BitOffset % 8))) != 0;
            }
            else
            {
                bitValue = false;
            }
            writer.WriteBooleanValue(bitValue);
            return;
        }

        switch (member.DataType)
        {
            case PlcDataType.Bool:
                writer.WriteBooleanValue(data.Length > 0 && data[0] != 0);
                break;
            case PlcDataType.Sint:
                writer.WriteNumberValue(data.Length > 0 ? (sbyte)data[0] : 0);
                break;
            case PlcDataType.Int:
                writer.WriteNumberValue(data.Length >= 2 ? BinaryPrimitives.ReadInt16LittleEndian(data) : (short)0);
                break;
            case PlcDataType.Dint:
                writer.WriteNumberValue(data.Length >= 4 ? BinaryPrimitives.ReadInt32LittleEndian(data) : 0);
                break;
            case PlcDataType.Lint:
                writer.WriteNumberValue(data.Length >= 8 ? BinaryPrimitives.ReadInt64LittleEndian(data) : 0L);
                break;
            case PlcDataType.Usint:
                writer.WriteNumberValue(data.Length > 0 ? data[0] : (byte)0);
                break;
            case PlcDataType.Uint:
                writer.WriteNumberValue(data.Length >= 2 ? BinaryPrimitives.ReadUInt16LittleEndian(data) : (ushort)0);
                break;
            case PlcDataType.Udint:
                writer.WriteNumberValue(data.Length >= 4 ? BinaryPrimitives.ReadUInt32LittleEndian(data) : 0u);
                break;
            case PlcDataType.Ulint:
                writer.WriteNumberValue(data.Length >= 8 ? BinaryPrimitives.ReadUInt64LittleEndian(data) : 0ul);
                break;
            case PlcDataType.Real:
                writer.WriteNumberValue(data.Length >= 4 ? BinaryPrimitives.ReadSingleLittleEndian(data) : 0f);
                break;
            case PlcDataType.Lreal:
                writer.WriteNumberValue(data.Length >= 8 ? BinaryPrimitives.ReadDoubleLittleEndian(data) : 0.0);
                break;
            case PlcDataType.String:
                WriteStringValue(writer, data);
                break;
            default:
                writer.WriteNullValue();
                break;
        }
    }

    private void WriteArrayMember(Utf8JsonWriter writer, ReadOnlySpan<byte> data, UdtMember member)
    {
        var cipType = PlcDataTypeToCipType(member.DataType);
        var atomicSize = CipDataTypes.GetAtomicSize(cipType);
        var elementCount = member.Dimensions[0];

        writer.WriteStartArray();
        for (var i = 0; i < elementCount && (i * atomicSize + atomicSize) <= data.Length; i++)
        {
            var elementData = data.Slice(i * atomicSize, atomicSize);
            WriteAtomicMember(writer, elementData, new UdtMember
            {
                Name = string.Empty,
                DataType = member.DataType,
                TypeName = member.TypeName,
                Offset = 0,
                Size = atomicSize,
            });
        }
        writer.WriteEndArray();
    }

    private void WriteNestedStructure(Utf8JsonWriter writer, ReadOnlySpan<byte> data, UdtMember member)
    {
        if (member.TemplateInstanceId == 0)
        {
            if (member.TypeName.Contains("STRING", StringComparison.OrdinalIgnoreCase) && data.Length >= 4)
            {
                WriteStringValue(writer, data);
                return;
            }
            writer.WriteNullValue();
            return;
        }

        var nestedUdt = _tagDatabase.GetUdtByTemplateId(member.TemplateInstanceId);
        if (nestedUdt == null)
        {
            if (member.TypeName.Contains("STRING", StringComparison.OrdinalIgnoreCase) && data.Length >= 4)
            {
                WriteStringValue(writer, data);
                return;
            }
            writer.WriteNullValue();
            return;
        }

        // STRING structures are written as JSON strings, not as objects
        if (TagDatabase.IsStringUdt(nestedUdt))
        {
            var strData = data.Length >= nestedUdt.ByteSize ? data[..nestedUdt.ByteSize] : data;
            WriteStringValue(writer, strData);
            return;
        }

        var nestedData = data.Length >= nestedUdt.ByteSize ? data[..nestedUdt.ByteSize] : data;
        WriteStructure(writer, nestedData, nestedUdt);
    }

    private void WriteStructureArrayMember(Utf8JsonWriter writer, ReadOnlySpan<byte> data, UdtMember member)
    {
        var nestedUdt = _tagDatabase.GetUdtByTemplateId(member.TemplateInstanceId);
        if (nestedUdt == null)
        {
            writer.WriteStartArray();
            writer.WriteEndArray();
            return;
        }

        var isString = TagDatabase.IsStringUdt(nestedUdt);
        var elementCount = member.Dimensions[0];
        var elementSize = nestedUdt.ByteSize;

        writer.WriteStartArray();
        for (var i = 0; i < elementCount && (i * elementSize + elementSize) <= data.Length; i++)
        {
            var elementData = data.Slice(i * elementSize, elementSize);
            if (isString)
                WriteStringValue(writer, elementData);
            else
                WriteStructure(writer, elementData, nestedUdt);
        }
        writer.WriteEndArray();
    }

    private static void WriteStringValue(Utf8JsonWriter writer, ReadOnlySpan<byte> data)
    {
        if (data.Length < 4)
        {
            writer.WriteStringValue(string.Empty);
            return;
        }

        var charCount = BinaryPrimitives.ReadInt32LittleEndian(data);
        if (charCount < 0) charCount = 0;
        if (charCount > 82) charCount = Math.Min(charCount, data.Length - 4);

        var str = Encoding.ASCII.GetString(data.Slice(4, charCount));
        writer.WriteStringValue(str);
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
