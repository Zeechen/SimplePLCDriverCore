using SimplePLCDriverCore.Abstractions;

namespace SimplePLCDriverCore.Tests.Abstractions;

public class PlcTagValueTests
{
    [Fact]
    public void FromDInt_StoresValue()
    {
        var v = PlcTagValue.FromDInt(42);

        Assert.Equal(PlcDataType.Dint, v.DataType);
        Assert.Equal(42, v.AsInt32());
        Assert.False(v.IsNull);
    }

    [Fact]
    public void FromReal_StoresFloat()
    {
        var v = PlcTagValue.FromReal(3.14f);

        Assert.Equal(PlcDataType.Real, v.DataType);
        Assert.Equal(3.14f, v.AsSingle());
    }

    [Fact]
    public void FromLReal_StoresDouble()
    {
        var v = PlcTagValue.FromLReal(3.14159265358979);
        Assert.Equal(3.14159265358979, v.AsDouble());
    }

    [Fact]
    public void FromBool_StoresBoolValue()
    {
        var t = PlcTagValue.FromBool(true);
        var f = PlcTagValue.FromBool(false);

        Assert.True(t.AsBoolean());
        Assert.False(f.AsBoolean());
    }

    [Fact]
    public void FromString_StoresString()
    {
        var v = PlcTagValue.FromString("Hello PLC");
        Assert.Equal("Hello PLC", v.AsString());
    }

    [Fact]
    public void Null_IsNullTrue()
    {
        var v = PlcTagValue.Null;
        Assert.True(v.IsNull);
        Assert.Equal(PlcDataType.Unknown, v.DataType);
    }

    [Fact]
    public void ImplicitConversion_ToInt()
    {
        PlcTagValue v = PlcTagValue.FromDInt(42);
        int result = v;
        Assert.Equal(42, result);
    }

    [Fact]
    public void ImplicitConversion_ToFloat()
    {
        PlcTagValue v = PlcTagValue.FromReal(3.14f);
        float result = v;
        Assert.Equal(3.14f, result);
    }

    [Fact]
    public void ImplicitConversion_ToDouble()
    {
        PlcTagValue v = PlcTagValue.FromLReal(2.718);
        double result = v;
        Assert.Equal(2.718, result);
    }

    [Fact]
    public void ImplicitConversion_ToBool()
    {
        PlcTagValue v = PlcTagValue.FromBool(true);
        bool result = v;
        Assert.True(result);
    }

    [Fact]
    public void ImplicitConversion_ToString()
    {
        PlcTagValue v = PlcTagValue.FromString("test");
        string result = v;
        Assert.Equal("test", result);
    }

    [Fact]
    public void CrossTypeConversion_IntToDouble()
    {
        var v = PlcTagValue.FromDInt(42);
        Assert.Equal(42.0, v.AsDouble());
    }

    [Fact]
    public void CrossTypeConversion_FloatToInt()
    {
        var v = PlcTagValue.FromReal(3.7f);
        Assert.Equal(4, v.AsInt32()); // Convert.ToInt32 rounds
    }

    [Fact]
    public void ToString_Atomic()
    {
        Assert.Equal("42", PlcTagValue.FromDInt(42).ToString());
        Assert.Equal("3.14", PlcTagValue.FromReal(3.14f).ToString());
        Assert.Equal("True", PlcTagValue.FromBool(true).ToString());
        Assert.Equal("False", PlcTagValue.FromBool(false).ToString());
        Assert.Equal("Hello", PlcTagValue.FromString("Hello").ToString());
        Assert.Equal("null", PlcTagValue.Null.ToString());
    }

    [Fact]
    public void ToString_Structure()
    {
        var members = new Dictionary<string, PlcTagValue>
        {
            ["X"] = PlcTagValue.FromDInt(1),
            ["Y"] = PlcTagValue.FromReal(2.5f),
        };
        var v = PlcTagValue.FromStructure(members);

        var str = v.ToString();
        Assert.Contains("X=1", str);
        Assert.Contains("Y=2.5", str);
    }

    [Fact]
    public void AsStructure_ReturnsMembers()
    {
        var members = new Dictionary<string, PlcTagValue>
        {
            ["A"] = PlcTagValue.FromDInt(10),
            ["B"] = PlcTagValue.FromString("hi"),
        };
        var v = PlcTagValue.FromStructure(members);

        var dict = v.AsStructure();
        Assert.NotNull(dict);
        Assert.Equal(2, dict!.Count);
        Assert.Equal(10, dict["A"].AsInt32());
        Assert.Equal("hi", dict["B"].AsString());
    }

    [Fact]
    public void AsStructure_ReturnsNull_ForNonStructure()
    {
        var v = PlcTagValue.FromDInt(42);
        Assert.Null(v.AsStructure());
    }

    [Fact]
    public void RawValue_ReturnsUnderlyingValue()
    {
        var v = PlcTagValue.FromDInt(42);
        Assert.Equal(42, v.RawValue);
    }

    [Fact]
    public void Equality_SameValues()
    {
        var a = PlcTagValue.FromDInt(42);
        var b = PlcTagValue.FromDInt(42);
        Assert.Equal(a, b);
        Assert.True(a == b);
    }

    [Fact]
    public void Equality_DifferentValues()
    {
        var a = PlcTagValue.FromDInt(42);
        var b = PlcTagValue.FromDInt(43);
        Assert.NotEqual(a, b);
        Assert.True(a != b);
    }

    [Fact]
    public void Equality_DifferentTypes()
    {
        var a = PlcTagValue.FromDInt(42);
        var b = PlcTagValue.FromReal(42f);
        Assert.NotEqual(a, b); // different PlcDataType
    }

    [Fact]
    public void GenericAs_ConvertsCorrectly()
    {
        var v = PlcTagValue.FromDInt(42);
        Assert.Equal(42L, v.As<long>());
        Assert.Equal(42.0, v.As<double>());
    }

    [Fact]
    public void AllFactoryMethods_SetCorrectDataType()
    {
        Assert.Equal(PlcDataType.Bool, PlcTagValue.FromBool(true).DataType);
        Assert.Equal(PlcDataType.Sint, PlcTagValue.FromSInt(-1).DataType);
        Assert.Equal(PlcDataType.Int, PlcTagValue.FromInt(100).DataType);
        Assert.Equal(PlcDataType.Dint, PlcTagValue.FromDInt(100).DataType);
        Assert.Equal(PlcDataType.Lint, PlcTagValue.FromLInt(100L).DataType);
        Assert.Equal(PlcDataType.Usint, PlcTagValue.FromUSInt(1).DataType);
        Assert.Equal(PlcDataType.Uint, PlcTagValue.FromUInt(1).DataType);
        Assert.Equal(PlcDataType.Udint, PlcTagValue.FromUDInt(1U).DataType);
        Assert.Equal(PlcDataType.Ulint, PlcTagValue.FromULInt(1UL).DataType);
        Assert.Equal(PlcDataType.Real, PlcTagValue.FromReal(1.0f).DataType);
        Assert.Equal(PlcDataType.Lreal, PlcTagValue.FromLReal(1.0).DataType);
        Assert.Equal(PlcDataType.String, PlcTagValue.FromString("x").DataType);
        Assert.Equal(PlcDataType.Structure, PlcTagValue.FromStructure(
            new Dictionary<string, PlcTagValue>()).DataType);
    }
}
