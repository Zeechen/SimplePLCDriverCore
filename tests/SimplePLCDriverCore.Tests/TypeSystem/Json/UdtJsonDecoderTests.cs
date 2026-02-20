using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using SimplePLCDriverCore.Abstractions;
using SimplePLCDriverCore.Protocols.EtherNetIP.Cip;
using SimplePLCDriverCore.TypeSystem;
using SimplePLCDriverCore.TypeSystem.Json;

namespace SimplePLCDriverCore.Tests.TypeSystem.Json;

public class UdtJsonDecoderTests
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
            ByteSize = 12,
            TemplateInstanceId = 0x0400,
            Members =
            [
                new UdtMember { Name = "Nested", DataType = PlcDataType.Structure, TypeName = "InnerUDT", Offset = 0, Size = 4, IsStructure = true, TemplateInstanceId = 0x0300 },
                new UdtMember { Name = "Count", DataType = PlcDataType.Dint, TypeName = "DINT", Offset = 4, Size = 4 },
                new UdtMember { Name = "Flag", DataType = PlcDataType.Real, TypeName = "REAL", Offset = 8, Size = 4 },
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
    public void ToJson_SimpleUdt()
    {
        var db = CreateSimpleUdtDb();
        var decoder = new UdtJsonDecoder(db);

        var data = new byte[8];
        BinaryPrimitives.WriteInt32LittleEndian(data, 42);
        BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(4), 3.14f);

        var json = decoder.ToJson(data, 0x0100);
        using var doc = JsonDocument.Parse(json);

        Assert.Equal(42, doc.RootElement.GetProperty("IntField").GetInt32());
        Assert.Equal(3.14f, doc.RootElement.GetProperty("FloatField").GetSingle());
    }

    [Fact]
    public void ToJson_BoolBitMembers()
    {
        var db = CreateBoolUdtDb();
        var decoder = new UdtJsonDecoder(db);

        var data = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(data, 0x00000081); // bits 0 and 7 set

        var json = decoder.ToJson(data, 0x0200);
        using var doc = JsonDocument.Parse(json);

        Assert.True(doc.RootElement.GetProperty("Flag0").GetBoolean());
        Assert.False(doc.RootElement.GetProperty("Flag1").GetBoolean());
        Assert.True(doc.RootElement.GetProperty("Flag7").GetBoolean());
    }

    [Fact]
    public void ToJson_NestedStructure()
    {
        var db = CreateNestedUdtDb();
        var decoder = new UdtJsonDecoder(db);

        var data = new byte[12];
        BinaryPrimitives.WriteInt32LittleEndian(data, 99); // InnerUDT.Value
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(4), 5); // Count
        BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(8), 1.5f); // Flag

        var json = decoder.ToJson(data, 0x0400);
        using var doc = JsonDocument.Parse(json);

        Assert.Equal(99, doc.RootElement.GetProperty("Nested").GetProperty("Value").GetInt32());
        Assert.Equal(5, doc.RootElement.GetProperty("Count").GetInt32());
        Assert.Equal(1.5f, doc.RootElement.GetProperty("Flag").GetSingle());
    }

    [Fact]
    public void ToJson_ArrayMember()
    {
        var db = CreateArrayUdtDb();
        var decoder = new UdtJsonDecoder(db);

        var data = new byte[20];
        for (var i = 0; i < 5; i++)
            BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(i * 4), (i + 1) * 10);

        var json = decoder.ToJson(data, 0x0500);
        using var doc = JsonDocument.Parse(json);

        var arr = doc.RootElement.GetProperty("Values");
        Assert.Equal(5, arr.GetArrayLength());
        Assert.Equal(10, arr[0].GetInt32());
        Assert.Equal(50, arr[4].GetInt32());
    }

    [Fact]
    public void ToJson_StructureArrayMember()
    {
        var db = CreateStructArrayUdtDb();
        var decoder = new UdtJsonDecoder(db);

        var data = new byte[12];
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0), 100);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(4), 200);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(8), 300);

        var json = decoder.ToJson(data, 0x0600);
        using var doc = JsonDocument.Parse(json);

        var items = doc.RootElement.GetProperty("Items");
        Assert.Equal(3, items.GetArrayLength());
        Assert.Equal(100, items[0].GetProperty("Value").GetInt32());
        Assert.Equal(200, items[1].GetProperty("Value").GetInt32());
        Assert.Equal(300, items[2].GetProperty("Value").GetInt32());
    }

    [Fact]
    public void ToJson_UnknownTemplate_Throws()
    {
        var db = new TagDatabase();
        var decoder = new UdtJsonDecoder(db);

        Assert.Throws<InvalidOperationException>(() =>
            decoder.ToJson(new byte[4], 0xFFFF));
    }

    [Fact]
    public void ToJson_Indented_ContainsNewlines()
    {
        var db = CreateSimpleUdtDb();
        var decoder = new UdtJsonDecoder(db);

        var data = new byte[8];
        var json = decoder.ToJson(data, 0x0100, indented: true);

        Assert.Contains("\n", json);
    }

    [Fact]
    public void ToJson_UdtWithStringMember_WritesStringAsJsonString()
    {
        var db = CreateSimpleUdtDb();

        // Add STRING template
        db.AddUdtDefinition(new UdtDefinition
        {
            Name = "STRING",
            ByteSize = 88,
            TemplateInstanceId = 0x0CE8,
            Members =
            [
                new UdtMember { Name = "LEN", DataType = PlcDataType.Dint, TypeName = "DINT", Offset = 0, Size = 4 },
                new UdtMember { Name = "DATA", DataType = PlcDataType.Sint, TypeName = "SINT", Offset = 4, Size = 1, Dimensions = [82] },
            ],
        });

        // Add UDT with STRING member
        db.AddUdtDefinition(new UdtDefinition
        {
            Name = "TaggedItem",
            ByteSize = 92,
            TemplateInstanceId = 0x0800,
            Members =
            [
                new UdtMember { Name = "Code", DataType = PlcDataType.Dint, TypeName = "DINT", Offset = 0, Size = 4 },
                new UdtMember
                {
                    Name = "Label", DataType = PlcDataType.Structure, TypeName = "STRING",
                    Offset = 4, Size = 88, IsStructure = true, TemplateInstanceId = 0x0CE8
                },
            ],
        });

        var decoder = new UdtJsonDecoder(db);

        var data = new byte[92];
        BinaryPrimitives.WriteInt32LittleEndian(data, 7); // Code = 7
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(4), 4); // STRING LEN = 4
        data[8] = (byte)'T'; data[9] = (byte)'e'; data[10] = (byte)'s'; data[11] = (byte)'t';

        var json = decoder.ToJson(data, 0x0800);
        using var doc = JsonDocument.Parse(json);

        Assert.Equal(7, doc.RootElement.GetProperty("Code").GetInt32());
        // STRING member should be a JSON string, not an object with LEN/DATA
        Assert.Equal(JsonValueKind.String, doc.RootElement.GetProperty("Label").ValueKind);
        Assert.Equal("Test", doc.RootElement.GetProperty("Label").GetString());
    }

    [Fact]
    public void ToJson_AllAtomicTypes()
    {
        var db = new TagDatabase();
        db.AddUdtDefinition(new UdtDefinition
        {
            Name = "AllTypes",
            ByteSize = 36,
            TemplateInstanceId = 0x0700,
            Members =
            [
                new UdtMember { Name = "SintVal", DataType = PlcDataType.Sint, TypeName = "SINT", Offset = 0, Size = 1 },
                new UdtMember { Name = "IntVal", DataType = PlcDataType.Int, TypeName = "INT", Offset = 2, Size = 2 },
                new UdtMember { Name = "DintVal", DataType = PlcDataType.Dint, TypeName = "DINT", Offset = 4, Size = 4 },
                new UdtMember { Name = "LintVal", DataType = PlcDataType.Lint, TypeName = "LINT", Offset = 8, Size = 8 },
                new UdtMember { Name = "UsintVal", DataType = PlcDataType.Usint, TypeName = "USINT", Offset = 16, Size = 1 },
                new UdtMember { Name = "UintVal", DataType = PlcDataType.Uint, TypeName = "UINT", Offset = 18, Size = 2 },
                new UdtMember { Name = "UdintVal", DataType = PlcDataType.Udint, TypeName = "UDINT", Offset = 20, Size = 4 },
                new UdtMember { Name = "RealVal", DataType = PlcDataType.Real, TypeName = "REAL", Offset = 24, Size = 4 },
                new UdtMember { Name = "LrealVal", DataType = PlcDataType.Lreal, TypeName = "LREAL", Offset = 28, Size = 8 },
            ],
        });

        var data = new byte[36];
        data[0] = unchecked((byte)-5); // SINT
        BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(2), 1000);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(4), 100000);
        BinaryPrimitives.WriteInt64LittleEndian(data.AsSpan(8), 9999999999L);
        data[16] = 250; // USINT
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(18), 60000);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(20), 4000000000u);
        BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(24), 2.5f);
        BinaryPrimitives.WriteDoubleLittleEndian(data.AsSpan(28), 3.14159);

        var decoder = new UdtJsonDecoder(db);
        var json = decoder.ToJson(data, 0x0700);
        using var doc = JsonDocument.Parse(json);

        Assert.Equal(-5, doc.RootElement.GetProperty("SintVal").GetInt32());
        Assert.Equal(1000, doc.RootElement.GetProperty("IntVal").GetInt32());
        Assert.Equal(100000, doc.RootElement.GetProperty("DintVal").GetInt32());
        Assert.Equal(9999999999L, doc.RootElement.GetProperty("LintVal").GetInt64());
        Assert.Equal(250, doc.RootElement.GetProperty("UsintVal").GetInt32());
        Assert.Equal(60000, doc.RootElement.GetProperty("UintVal").GetInt32());
        Assert.Equal(4000000000u, doc.RootElement.GetProperty("UdintVal").GetUInt32());
        Assert.Equal(2.5f, doc.RootElement.GetProperty("RealVal").GetSingle());
        Assert.Equal(3.14159, doc.RootElement.GetProperty("LrealVal").GetDouble(), 5);
    }
}
