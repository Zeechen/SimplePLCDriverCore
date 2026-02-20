using SimplePLCDriverCore.Abstractions;

namespace SimplePLCDriverCore.Tests.TypeSystem.Json;

public class PlcTagValueJsonConverterTests
{
    [Fact]
    public void ToJson_Bool()
    {
        var value = PlcTagValue.FromBool(true);
        Assert.Equal("true", value.ToJson());
    }

    [Fact]
    public void ToJson_Int()
    {
        var value = PlcTagValue.FromDInt(42);
        Assert.Equal("42", value.ToJson());
    }

    [Fact]
    public void ToJson_Real()
    {
        var value = PlcTagValue.FromReal(3.14f);
        var json = value.ToJson();
        Assert.Contains("3.14", json);
    }

    [Fact]
    public void ToJson_String()
    {
        var value = PlcTagValue.FromString("hello");
        Assert.Equal("\"hello\"", value.ToJson());
    }

    [Fact]
    public void ToJson_Null()
    {
        var value = PlcTagValue.Null;
        Assert.Equal("null", value.ToJson());
    }

    [Fact]
    public void ToJson_Structure()
    {
        var members = new Dictionary<string, PlcTagValue>
        {
            ["IntField"] = PlcTagValue.FromDInt(42),
            ["FloatField"] = PlcTagValue.FromReal(3.14f),
        };
        var value = PlcTagValue.FromStructure(members);
        var json = value.ToJson();

        Assert.Contains("\"IntField\":42", json);
        Assert.Contains("\"FloatField\":", json);
    }

    [Fact]
    public void ToJson_NestedStructure()
    {
        var inner = new Dictionary<string, PlcTagValue>
        {
            ["Value"] = PlcTagValue.FromDInt(99),
        };
        var outer = new Dictionary<string, PlcTagValue>
        {
            ["Nested"] = PlcTagValue.FromStructure(inner),
            ["Count"] = PlcTagValue.FromDInt(1),
        };
        var value = PlcTagValue.FromStructure(outer);
        var json = value.ToJson();

        Assert.Contains("\"Nested\":{\"Value\":99}", json);
        Assert.Contains("\"Count\":1", json);
    }

    [Fact]
    public void ToJson_Array()
    {
        var elements = new PlcTagValue[]
        {
            PlcTagValue.FromDInt(1),
            PlcTagValue.FromDInt(2),
            PlcTagValue.FromDInt(3),
        };
        var value = new PlcTagValue(elements as IReadOnlyList<PlcTagValue>, PlcDataType.Dint);
        var json = value.ToJson();

        Assert.Equal("[1,2,3]", json);
    }

    [Fact]
    public void ToJson_Indented()
    {
        var members = new Dictionary<string, PlcTagValue>
        {
            ["X"] = PlcTagValue.FromDInt(1),
        };
        var value = PlcTagValue.FromStructure(members);
        var json = value.ToJson(indented: true);

        Assert.Contains("\n", json);
        Assert.Contains("\"X\": 1", json);
    }

    [Fact]
    public void ToJson_AllAtomicTypes()
    {
        Assert.Equal("-1", PlcTagValue.FromSInt(-1).ToJson());
        Assert.Equal("100", PlcTagValue.FromInt(100).ToJson());
        Assert.Equal("1000", PlcTagValue.FromDInt(1000).ToJson());
        Assert.Equal("100000", PlcTagValue.FromLInt(100000).ToJson());
        Assert.Equal("200", PlcTagValue.FromUSInt(200).ToJson());
        Assert.Equal("5000", PlcTagValue.FromUInt(5000).ToJson());
        Assert.Equal("50000", PlcTagValue.FromUDInt(50000).ToJson());
        Assert.Equal("500000", PlcTagValue.FromULInt(500000).ToJson());
    }
}
