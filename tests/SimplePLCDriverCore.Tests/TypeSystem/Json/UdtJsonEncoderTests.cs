using System.Buffers.Binary;
using System.Text.Json;
using SimplePLCDriverCore.Abstractions;
using SimplePLCDriverCore.Protocols.EtherNetIP.Cip;
using SimplePLCDriverCore.TypeSystem;
using SimplePLCDriverCore.TypeSystem.Json;

namespace SimplePLCDriverCore.Tests.TypeSystem.Json;

public class UdtJsonEncoderTests
{
    private static TagDatabase CreateSimpleUdtDb()
    {
        var db = new TagDatabase();
        db.AddUdtDefinition(new UdtDefinition
        {
            Name = "SimpleUDT",
            ByteSize = 8,
            TemplateInstanceId = 0x0100,
            Members =
            [
                new UdtMember { Name = "IntField", DataType = PlcDataType.Dint, TypeName = "DINT", Offset = 0, Size = 4 },
                new UdtMember { Name = "FloatField", DataType = PlcDataType.Real, TypeName = "REAL", Offset = 4, Size = 4 },
            ],
        });
        return db;
    }

    private static TagDatabase CreateBoolUdtDb()
    {
        var db = new TagDatabase();
        db.AddUdtDefinition(new UdtDefinition
        {
            Name = "BoolUDT",
            ByteSize = 4,
            TemplateInstanceId = 0x0200,
            Members =
            [
                new UdtMember { Name = "Flag0", DataType = PlcDataType.Bool, TypeName = "BOOL", Offset = 0, Size = 1, BitOffset = 0 },
                new UdtMember { Name = "Flag1", DataType = PlcDataType.Bool, TypeName = "BOOL", Offset = 0, Size = 1, BitOffset = 1 },
                new UdtMember { Name = "Flag7", DataType = PlcDataType.Bool, TypeName = "BOOL", Offset = 0, Size = 1, BitOffset = 7 },
            ],
        });
        return db;
    }

    private static TagDatabase CreateNestedUdtDb()
    {
        var db = new TagDatabase();
        db.AddUdtDefinition(new UdtDefinition
        {
            Name = "InnerUDT",
            ByteSize = 4,
            TemplateInstanceId = 0x0300,
            Members =
            [
                new UdtMember { Name = "Value", DataType = PlcDataType.Dint, TypeName = "DINT", Offset = 0, Size = 4 },
            ],
        });
        db.AddUdtDefinition(new UdtDefinition
        {
            Name = "OuterUDT",
            ByteSize = 8,
            TemplateInstanceId = 0x0400,
            Members =
            [
                new UdtMember { Name = "Nested", DataType = PlcDataType.Structure, TypeName = "InnerUDT", Offset = 0, Size = 4, IsStructure = true, TemplateInstanceId = 0x0300 },
                new UdtMember { Name = "Count", DataType = PlcDataType.Dint, TypeName = "DINT", Offset = 4, Size = 4 },
            ],
        });
        return db;
    }

    private static TagDatabase CreateArrayUdtDb()
    {
        var db = new TagDatabase();
        db.AddUdtDefinition(new UdtDefinition
        {
            Name = "ArrayUDT",
            ByteSize = 20,
            TemplateInstanceId = 0x0500,
            Members =
            [
                new UdtMember { Name = "Values", DataType = PlcDataType.Dint, TypeName = "DINT", Offset = 0, Size = 4, Dimensions = [5] },
            ],
        });
        return db;
    }

    private static TagDatabase CreateStructArrayUdtDb()
    {
        var db = new TagDatabase();
        db.AddUdtDefinition(new UdtDefinition
        {
            Name = "InnerUDT",
            ByteSize = 4,
            TemplateInstanceId = 0x0300,
            Members =
            [
                new UdtMember { Name = "Value", DataType = PlcDataType.Dint, TypeName = "DINT", Offset = 0, Size = 4 },
            ],
        });
        db.AddUdtDefinition(new UdtDefinition
        {
            Name = "StructArrayUDT",
            ByteSize = 12,
            TemplateInstanceId = 0x0600,
            Members =
            [
                new UdtMember { Name = "Items", DataType = PlcDataType.Structure, TypeName = "InnerUDT", Offset = 0, Size = 4, IsStructure = true, TemplateInstanceId = 0x0300, Dimensions = [3] },
            ],
        });
        return db;
    }

    [Fact]
    public void Encode_SimpleUdt_FullWrite()
    {
        var db = CreateSimpleUdtDb();
        var encoder = new UdtJsonEncoder(db);

        var json = """{"IntField": 42, "FloatField": 3.14}""";
        var encoded = encoder.Encode(json, 0x0100);

        Assert.Equal(8, encoded.Length);
        Assert.Equal(42, BinaryPrimitives.ReadInt32LittleEndian(encoded));
        Assert.Equal(3.14f, BinaryPrimitives.ReadSingleLittleEndian(encoded.AsSpan(4)));
    }

    [Fact]
    public void Encode_PartialWrite_PreservesBase()
    {
        var db = CreateSimpleUdtDb();
        var encoder = new UdtJsonEncoder(db);

        // Base data: IntField=100, FloatField=2.0
        var baseData = new byte[8];
        BinaryPrimitives.WriteInt32LittleEndian(baseData, 100);
        BinaryPrimitives.WriteSingleLittleEndian(baseData.AsSpan(4), 2.0f);

        // Only update IntField via JSON
        var json = """{"IntField": 42}""";
        var encoded = encoder.Encode(json, 0x0100, baseData);

        Assert.Equal(8, encoded.Length);
        Assert.Equal(42, BinaryPrimitives.ReadInt32LittleEndian(encoded));
        Assert.Equal(2.0f, BinaryPrimitives.ReadSingleLittleEndian(encoded.AsSpan(4))); // Preserved
    }

    [Fact]
    public void Encode_BoolBitMembers()
    {
        var db = CreateBoolUdtDb();
        var encoder = new UdtJsonEncoder(db);

        var json = """{"Flag0": true, "Flag1": false, "Flag7": true}""";
        var encoded = encoder.Encode(json, 0x0200);

        var word = BinaryPrimitives.ReadUInt32LittleEndian(encoded);
        Assert.True((word & 1) != 0);     // bit 0
        Assert.False((word & 2) != 0);    // bit 1
        Assert.True((word & 128) != 0);   // bit 7
    }

    [Fact]
    public void Encode_NestedStructure()
    {
        var db = CreateNestedUdtDb();
        var encoder = new UdtJsonEncoder(db);

        var json = """{"Nested": {"Value": 99}, "Count": 5}""";
        var encoded = encoder.Encode(json, 0x0400);

        Assert.Equal(8, encoded.Length);
        Assert.Equal(99, BinaryPrimitives.ReadInt32LittleEndian(encoded));
        Assert.Equal(5, BinaryPrimitives.ReadInt32LittleEndian(encoded.AsSpan(4)));
    }

    [Fact]
    public void Encode_ArrayMember()
    {
        var db = CreateArrayUdtDb();
        var encoder = new UdtJsonEncoder(db);

        var json = """{"Values": [10, 20, 30, 40, 50]}""";
        var encoded = encoder.Encode(json, 0x0500);

        Assert.Equal(20, encoded.Length);
        for (var i = 0; i < 5; i++)
            Assert.Equal((i + 1) * 10, BinaryPrimitives.ReadInt32LittleEndian(encoded.AsSpan(i * 4)));
    }

    [Fact]
    public void Encode_StructureArrayMember()
    {
        var db = CreateStructArrayUdtDb();
        var encoder = new UdtJsonEncoder(db);

        var json = """{"Items": [{"Value": 100}, {"Value": 200}, {"Value": 300}]}""";
        var encoded = encoder.Encode(json, 0x0600);

        Assert.Equal(12, encoded.Length);
        Assert.Equal(100, BinaryPrimitives.ReadInt32LittleEndian(encoded.AsSpan(0)));
        Assert.Equal(200, BinaryPrimitives.ReadInt32LittleEndian(encoded.AsSpan(4)));
        Assert.Equal(300, BinaryPrimitives.ReadInt32LittleEndian(encoded.AsSpan(8)));
    }

    [Fact]
    public void Encode_CaseInsensitiveProperties()
    {
        var db = CreateSimpleUdtDb();
        var encoder = new UdtJsonEncoder(db);

        var json = """{"intfield": 42, "floatfield": 1.5}""";
        var encoded = encoder.Encode(json, 0x0100);

        Assert.Equal(42, BinaryPrimitives.ReadInt32LittleEndian(encoded));
        Assert.Equal(1.5f, BinaryPrimitives.ReadSingleLittleEndian(encoded.AsSpan(4)));
    }

    [Fact]
    public void Encode_ExtraJsonProperties_Ignored()
    {
        var db = CreateSimpleUdtDb();
        var encoder = new UdtJsonEncoder(db);

        var json = """{"IntField": 42, "NonExistent": 99, "FloatField": 1.0}""";
        var encoded = encoder.Encode(json, 0x0100);

        Assert.Equal(42, BinaryPrimitives.ReadInt32LittleEndian(encoded));
        Assert.Equal(1.0f, BinaryPrimitives.ReadSingleLittleEndian(encoded.AsSpan(4)));
    }

    [Fact]
    public void Encode_UnknownTemplate_Throws()
    {
        var db = new TagDatabase();
        var encoder = new UdtJsonEncoder(db);

        Assert.Throws<InvalidOperationException>(() =>
            encoder.Encode("{}", 0xFFFF));
    }

    [Fact]
    public void Encode_PartialWrite_MismatchedSize_Throws()
    {
        var db = CreateSimpleUdtDb();
        var encoder = new UdtJsonEncoder(db);

        Assert.Throws<ArgumentException>(() =>
            encoder.Encode("{}", 0x0100, new byte[4])); // 4 != 8
    }

    [Fact]
    public void RoundTrip_EncodeAndDecode()
    {
        var db = CreateSimpleUdtDb();
        var encoder = new UdtJsonEncoder(db);
        var decoder = new UdtJsonDecoder(db);

        var inputJson = """{"IntField": 12345, "FloatField": 2.718}""";
        var encoded = encoder.Encode(inputJson, 0x0100);
        var outputJson = decoder.ToJson(encoded, 0x0100);

        using var doc = JsonDocument.Parse(outputJson);
        Assert.Equal(12345, doc.RootElement.GetProperty("IntField").GetInt32());
        Assert.Equal(2.718f, doc.RootElement.GetProperty("FloatField").GetSingle());
    }

    [Fact]
    public void RoundTrip_NestedStructure()
    {
        var db = CreateNestedUdtDb();
        var encoder = new UdtJsonEncoder(db);
        var decoder = new UdtJsonDecoder(db);

        var inputJson = """{"Nested": {"Value": 42}, "Count": 7}""";
        var encoded = encoder.Encode(inputJson, 0x0400);
        var outputJson = decoder.ToJson(encoded, 0x0400);

        using var doc = JsonDocument.Parse(outputJson);
        Assert.Equal(42, doc.RootElement.GetProperty("Nested").GetProperty("Value").GetInt32());
        Assert.Equal(7, doc.RootElement.GetProperty("Count").GetInt32());
    }

    [Fact]
    public void RoundTrip_StructureArray()
    {
        var db = CreateStructArrayUdtDb();
        var encoder = new UdtJsonEncoder(db);
        var decoder = new UdtJsonDecoder(db);

        var inputJson = """{"Items": [{"Value": 1}, {"Value": 2}, {"Value": 3}]}""";
        var encoded = encoder.Encode(inputJson, 0x0600);
        var outputJson = decoder.ToJson(encoded, 0x0600);

        using var doc = JsonDocument.Parse(outputJson);
        var items = doc.RootElement.GetProperty("Items");
        Assert.Equal(3, items.GetArrayLength());
        Assert.Equal(1, items[0].GetProperty("Value").GetInt32());
        Assert.Equal(2, items[1].GetProperty("Value").GetInt32());
        Assert.Equal(3, items[2].GetProperty("Value").GetInt32());
    }

    [Fact]
    public void RoundTrip_PartialWrite()
    {
        var db = CreateSimpleUdtDb();
        var encoder = new UdtJsonEncoder(db);
        var decoder = new UdtJsonDecoder(db);

        // Initial full write
        var fullJson = """{"IntField": 100, "FloatField": 9.99}""";
        var baseData = encoder.Encode(fullJson, 0x0100);

        // Partial update - only change IntField
        var partialJson = """{"IntField": 200}""";
        var patched = encoder.Encode(partialJson, 0x0100, baseData);

        var outputJson = decoder.ToJson(patched, 0x0100);
        using var doc = JsonDocument.Parse(outputJson);

        Assert.Equal(200, doc.RootElement.GetProperty("IntField").GetInt32());
        Assert.Equal(9.99f, doc.RootElement.GetProperty("FloatField").GetSingle()); // Preserved
    }
}
