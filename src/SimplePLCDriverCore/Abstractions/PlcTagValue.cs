using System.Collections.ObjectModel;
using System.Globalization;
using SimplePLCDriverCore.TypeSystem.Json;

namespace SimplePLCDriverCore.Abstractions;

/// <summary>
/// Universal PLC value container that wraps any tag value without requiring
/// the consumer to know the PLC data type upfront.
/// Inspired by pycomm3's approach where tag types are auto-detected.
/// </summary>
public readonly struct PlcTagValue : IEquatable<PlcTagValue>
{
    private readonly object? _value;

    public PlcDataType DataType { get; }

    public bool IsNull => _value is null;

    public PlcTagValue(object? value, PlcDataType dataType)
    {
        _value = value;
        DataType = dataType;
    }

    // --- Display ---

    public override string ToString() =>
        _value switch
        {
            null => "null",
            bool b => b ? "True" : "False",
            float f => f.ToString("G", CultureInfo.InvariantCulture),
            double d => d.ToString("G", CultureInfo.InvariantCulture),
            IReadOnlyDictionary<string, PlcTagValue> dict => FormatStructure(dict),
            IReadOnlyList<PlcTagValue> arr => FormatArray(arr),
            _ => _value.ToString() ?? "null"
        };

    private static string FormatStructure(IReadOnlyDictionary<string, PlcTagValue> dict)
    {
        var members = string.Join(", ", dict.Select(kv => $"{kv.Key}={kv.Value}"));
        return $"{{{members}}}";
    }

    private static string FormatArray(IReadOnlyList<PlcTagValue> arr)
    {
        var elements = string.Join(", ", arr.Select(v => v.ToString()));
        return $"[{elements}]";
    }

    // --- Typed Accessors ---

    public bool AsBoolean() => Convert.ToBoolean(_value);
    public sbyte AsSByte() => Convert.ToSByte(_value);
    public byte AsByte() => Convert.ToByte(_value);
    public short AsInt16() => Convert.ToInt16(_value);
    public ushort AsUInt16() => Convert.ToUInt16(_value);
    public int AsInt32() => Convert.ToInt32(_value);
    public uint AsUInt32() => Convert.ToUInt32(_value);
    public long AsInt64() => Convert.ToInt64(_value);
    public ulong AsUInt64() => Convert.ToUInt64(_value);
    public float AsSingle() => Convert.ToSingle(_value);
    public double AsDouble() => Convert.ToDouble(_value);
    public string AsString() => _value?.ToString() ?? string.Empty;

    /// <summary>Get value as a structure (UDT) with named members.</summary>
    public IReadOnlyDictionary<string, PlcTagValue>? AsStructure() =>
        _value as IReadOnlyDictionary<string, PlcTagValue>;

    /// <summary>Get value as an array of PlcTagValues.</summary>
    public IReadOnlyList<PlcTagValue>? AsArray() =>
        _value as IReadOnlyList<PlcTagValue>;

    /// <summary>Get the raw underlying value.</summary>
    public object? RawValue => _value;

    /// <summary>Generic typed accessor with conversion.</summary>
    public T As<T>() => (T)Convert.ChangeType(_value!, typeof(T));

    // --- Implicit Conversions ---

    public static implicit operator int(PlcTagValue v) => v.AsInt32();
    public static implicit operator uint(PlcTagValue v) => v.AsUInt32();
    public static implicit operator short(PlcTagValue v) => v.AsInt16();
    public static implicit operator ushort(PlcTagValue v) => v.AsUInt16();
    public static implicit operator long(PlcTagValue v) => v.AsInt64();
    public static implicit operator ulong(PlcTagValue v) => v.AsUInt64();
    public static implicit operator float(PlcTagValue v) => v.AsSingle();
    public static implicit operator double(PlcTagValue v) => v.AsDouble();
    public static implicit operator bool(PlcTagValue v) => v.AsBoolean();
    public static implicit operator string(PlcTagValue v) => v.AsString();

    // --- Equality ---

    public bool Equals(PlcTagValue other) =>
        DataType == other.DataType && Equals(_value, other._value);

    public override bool Equals(object? obj) =>
        obj is PlcTagValue other && Equals(other);

    public override int GetHashCode() =>
        HashCode.Combine(DataType, _value);

    public static bool operator ==(PlcTagValue left, PlcTagValue right) => left.Equals(right);
    public static bool operator !=(PlcTagValue left, PlcTagValue right) => !left.Equals(right);

    // --- JSON ---

    /// <summary>Convert this PlcTagValue to a JSON string.</summary>
    public string ToJson(bool indented = false) =>
        PlcTagValueJsonConverter.ToJson(this, indented);

    // --- Factory Methods ---

    public static PlcTagValue FromBool(bool value) => new(value, PlcDataType.Bool);
    public static PlcTagValue FromSInt(sbyte value) => new(value, PlcDataType.Sint);
    public static PlcTagValue FromInt(short value) => new(value, PlcDataType.Int);
    public static PlcTagValue FromDInt(int value) => new(value, PlcDataType.Dint);
    public static PlcTagValue FromLInt(long value) => new(value, PlcDataType.Lint);
    public static PlcTagValue FromUSInt(byte value) => new(value, PlcDataType.Usint);
    public static PlcTagValue FromUInt(ushort value) => new(value, PlcDataType.Uint);
    public static PlcTagValue FromUDInt(uint value) => new(value, PlcDataType.Udint);
    public static PlcTagValue FromULInt(ulong value) => new(value, PlcDataType.Ulint);
    public static PlcTagValue FromReal(float value) => new(value, PlcDataType.Real);
    public static PlcTagValue FromLReal(double value) => new(value, PlcDataType.Lreal);
    public static PlcTagValue FromString(string value) => new(value, PlcDataType.String);
    public static PlcTagValue FromStructure(IReadOnlyDictionary<string, PlcTagValue> members) =>
        new(members, PlcDataType.Structure);
    public static PlcTagValue Null => new(null, PlcDataType.Unknown);
}
