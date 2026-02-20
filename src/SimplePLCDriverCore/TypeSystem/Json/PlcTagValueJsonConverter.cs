using System.Text;
using System.Text.Json;
using SimplePLCDriverCore.Abstractions;

namespace SimplePLCDriverCore.TypeSystem.Json;

/// <summary>
/// Converts PlcTagValue instances to JSON strings.
/// Used by PlcTagValue.ToJson() for existing PlcTagValue users.
/// </summary>
internal static class PlcTagValueJsonConverter
{
    public static string ToJson(PlcTagValue value, bool indented = false)
    {
        using var stream = new MemoryStream();
        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = indented });
        WritePlcTagValue(writer, value);
        writer.Flush();
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WritePlcTagValue(Utf8JsonWriter writer, PlcTagValue value)
    {
        var dict = value.AsStructure();
        if (dict != null)
        {
            writer.WriteStartObject();
            foreach (var (name, memberValue) in dict)
            {
                writer.WritePropertyName(name);
                WritePlcTagValue(writer, memberValue);
            }
            writer.WriteEndObject();
            return;
        }

        var arr = value.AsArray();
        if (arr != null)
        {
            writer.WriteStartArray();
            foreach (var element in arr)
                WritePlcTagValue(writer, element);
            writer.WriteEndArray();
            return;
        }

        if (value.IsNull)
        {
            writer.WriteNullValue();
            return;
        }

        switch (value.DataType)
        {
            case PlcDataType.Bool:
                writer.WriteBooleanValue(value.AsBoolean());
                break;
            case PlcDataType.Sint:
                writer.WriteNumberValue(value.AsSByte());
                break;
            case PlcDataType.Int:
                writer.WriteNumberValue(value.AsInt16());
                break;
            case PlcDataType.Dint:
                writer.WriteNumberValue(value.AsInt32());
                break;
            case PlcDataType.Lint:
                writer.WriteNumberValue(value.AsInt64());
                break;
            case PlcDataType.Usint:
                writer.WriteNumberValue(value.AsByte());
                break;
            case PlcDataType.Uint:
                writer.WriteNumberValue(value.AsUInt16());
                break;
            case PlcDataType.Udint:
                writer.WriteNumberValue(value.AsUInt32());
                break;
            case PlcDataType.Ulint:
                writer.WriteNumberValue(value.AsUInt64());
                break;
            case PlcDataType.Real:
                writer.WriteNumberValue(value.AsSingle());
                break;
            case PlcDataType.Lreal:
                writer.WriteNumberValue(value.AsDouble());
                break;
            case PlcDataType.String:
                writer.WriteStringValue(value.AsString());
                break;
            default:
                writer.WriteNullValue();
                break;
        }
    }
}
