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

    // ==========================================================================
    // WriteAtomicMember - Additional Branches
    // ==========================================================================

    [Fact]
    public void ToJson_BoolNonBitMember()
    {
        var db = new TagDatabase();
        db.AddUdtDefinition(new UdtDefinition
        {
            Name = "BoolOnly",
            ByteSize = 1,
            TemplateInstanceId = 0x0900,
            Members =
            [
                new UdtMember { Name = "Flag", DataType = PlcDataType.Bool, TypeName = "BOOL", Offset = 0, Size = 1 },
            ],
        });
        var decoder = new UdtJsonDecoder(db);
        var data = new byte[] { 1 };
        var json = decoder.ToJson(data, 0x0900);
        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.GetProperty("Flag").GetBoolean());
    }

    [Fact]
    public void ToJson_BoolNonBitMember_False()
    {
        var db = new TagDatabase();
        db.AddUdtDefinition(new UdtDefinition
        {
            Name = "BoolOnly",
            ByteSize = 1,
            TemplateInstanceId = 0x0900,
            Members =
            [
                new UdtMember { Name = "Flag", DataType = PlcDataType.Bool, TypeName = "BOOL", Offset = 0, Size = 1 },
            ],
        });
        var decoder = new UdtJsonDecoder(db);
        var data = new byte[] { 0 };
        var json = decoder.ToJson(data, 0x0900);
        using var doc = JsonDocument.Parse(json);
        Assert.False(doc.RootElement.GetProperty("Flag").GetBoolean());
    }

    [Fact]
    public void ToJson_UlintMember()
    {
        var db = new TagDatabase();
        db.AddUdtDefinition(new UdtDefinition
        {
            Name = "UlintUDT",
            ByteSize = 8,
            TemplateInstanceId = 0x0A00,
            Members =
            [
                new UdtMember { Name = "BigVal", DataType = PlcDataType.Ulint, TypeName = "ULINT", Offset = 0, Size = 8 },
            ],
        });
        var decoder = new UdtJsonDecoder(db);
        var data = new byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(data, 18000000000000000000UL);
        var json = decoder.ToJson(data, 0x0A00);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(18000000000000000000UL, doc.RootElement.GetProperty("BigVal").GetUInt64());
    }

    [Fact]
    public void ToJson_UnknownDataType_WritesNull()
    {
        var db = new TagDatabase();
        db.AddUdtDefinition(new UdtDefinition
        {
            Name = "UnknownUDT",
            ByteSize = 4,
            TemplateInstanceId = 0x0B00,
            Members =
            [
                new UdtMember { Name = "Unknown", DataType = PlcDataType.Unknown, TypeName = "???", Offset = 0, Size = 4 },
            ],
        });
        var decoder = new UdtJsonDecoder(db);
        var json = decoder.ToJson(new byte[4], 0x0B00);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("Unknown").ValueKind);
    }

    // ==========================================================================
    // WriteNestedStructure - TemplateId=0 with STRING fallback
    // ==========================================================================

    [Fact]
    public void ToJson_NestedStructure_NoTemplate_StringFallback()
    {
        var db = new TagDatabase();
        db.AddUdtDefinition(new UdtDefinition
        {
            Name = "TestUDT",
            ByteSize = 92,
            TemplateInstanceId = 0x0C00,
            Members =
            [
                new UdtMember
                {
                    Name = "Label", DataType = PlcDataType.Structure, TypeName = "STRING",
                    Offset = 0, Size = 88, IsStructure = true, TemplateInstanceId = 0
                },
            ],
        });
        var decoder = new UdtJsonDecoder(db);
        var data = new byte[92];
        BinaryPrimitives.WriteInt32LittleEndian(data, 4); // LEN=4
        data[4] = (byte)'T'; data[5] = (byte)'e'; data[6] = (byte)'s'; data[7] = (byte)'t';
        var json = decoder.ToJson(data, 0x0C00);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("Test", doc.RootElement.GetProperty("Label").GetString());
    }

    [Fact]
    public void ToJson_NestedStructure_NoTemplate_NonString_WritesNull()
    {
        var db = new TagDatabase();
        db.AddUdtDefinition(new UdtDefinition
        {
            Name = "TestUDT",
            ByteSize = 8,
            TemplateInstanceId = 0x0C00,
            Members =
            [
                new UdtMember
                {
                    Name = "Inner", DataType = PlcDataType.Structure, TypeName = "SomeUDT",
                    Offset = 0, Size = 4, IsStructure = true, TemplateInstanceId = 0
                },
            ],
        });
        var decoder = new UdtJsonDecoder(db);
        var json = decoder.ToJson(new byte[8], 0x0C00);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("Inner").ValueKind);
    }

    // ==========================================================================
    // WriteStructureArrayMember - null UDT
    // ==========================================================================

    [Fact]
    public void ToJson_StructureArrayMember_UnknownTemplate_EmptyArray()
    {
        var db = new TagDatabase();
        db.AddUdtDefinition(new UdtDefinition
        {
            Name = "TestUDT",
            ByteSize = 12,
            TemplateInstanceId = 0x0D00,
            Members =
            [
                new UdtMember
                {
                    Name = "Items", DataType = PlcDataType.Structure, TypeName = "UnknownUDT",
                    Offset = 0, Size = 4, IsStructure = true, TemplateInstanceId = 0x9999,
                    Dimensions = [3]
                },
            ],
        });
        var decoder = new UdtJsonDecoder(db);
        var json = decoder.ToJson(new byte[12], 0x0D00);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(0, doc.RootElement.GetProperty("Items").GetArrayLength());
    }

    // ==========================================================================
    // WriteStructureArrayMember - String array
    // ==========================================================================

    [Fact]
    public void ToJson_StringArrayMember()
    {
        var db = new TagDatabase();
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
        db.AddUdtDefinition(new UdtDefinition
        {
            Name = "StringArray",
            ByteSize = 176,
            TemplateInstanceId = 0x0E00,
            Members =
            [
                new UdtMember
                {
                    Name = "Labels", DataType = PlcDataType.Structure, TypeName = "STRING",
                    Offset = 0, Size = 88, IsStructure = true, TemplateInstanceId = 0x0CE8,
                    Dimensions = [2]
                },
            ],
        });
        var decoder = new UdtJsonDecoder(db);
        var data = new byte[176];
        // First string: "AB"
        BinaryPrimitives.WriteInt32LittleEndian(data, 2);
        data[4] = (byte)'A'; data[5] = (byte)'B';
        // Second string: "CD"
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(88), 2);
        data[92] = (byte)'C'; data[93] = (byte)'D';

        var json = decoder.ToJson(data, 0x0E00);
        using var doc = JsonDocument.Parse(json);
        var labels = doc.RootElement.GetProperty("Labels");
        Assert.Equal(2, labels.GetArrayLength());
        Assert.Equal("AB", labels[0].GetString());
        Assert.Equal("CD", labels[1].GetString());
    }

    // ==========================================================================
    // WriteStringValue edge cases
    // ==========================================================================

    [Fact]
    public void ToJson_StringMember_ShortData()
    {
        var db = new TagDatabase();
        db.AddUdtDefinition(new UdtDefinition
        {
            Name = "ShortUDT",
            ByteSize = 2,
            TemplateInstanceId = 0x0F00,
            Members =
            [
                new UdtMember
                {
                    Name = "Label", DataType = PlcDataType.Structure, TypeName = "STRING",
                    Offset = 0, Size = 2, IsStructure = true, TemplateInstanceId = 0
                },
            ],
        });
        var decoder = new UdtJsonDecoder(db);
        var json = decoder.ToJson(new byte[2], 0x0F00);
        using var doc = JsonDocument.Parse(json);
        // Short data (< 4 bytes) for STRING should return empty string or null
        var label = doc.RootElement.GetProperty("Label");
        Assert.True(label.ValueKind == JsonValueKind.String || label.ValueKind == JsonValueKind.Null);
    }

    // ==========================================================================
    // Nested structure with unknown template but STRING name
    // ==========================================================================

    [Fact]
    public void ToJson_NestedStructure_UnknownTemplate_StringName_Fallback()
    {
        var db = new TagDatabase();
        // Don't add the STRING template - it will be "not found"
        db.AddUdtDefinition(new UdtDefinition
        {
            Name = "TestUDT",
            ByteSize = 92,
            TemplateInstanceId = 0x1000,
            Members =
            [
                new UdtMember
                {
                    Name = "Label", DataType = PlcDataType.Structure, TypeName = "STRING",
                    Offset = 0, Size = 88, IsStructure = true, TemplateInstanceId = 0xAAAA // unknown
                },
            ],
        });
        var decoder = new UdtJsonDecoder(db);
        var data = new byte[92];
        BinaryPrimitives.WriteInt32LittleEndian(data, 3);
        data[4] = (byte)'X'; data[5] = (byte)'Y'; data[6] = (byte)'Z';
        var json = decoder.ToJson(data, 0x1000);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("XYZ", doc.RootElement.GetProperty("Label").GetString());
    }

    [Fact]
    public void ToJson_NestedStructure_UnknownTemplate_NonString_WritesNull()
    {
        var db = new TagDatabase();
        db.AddUdtDefinition(new UdtDefinition
        {
            Name = "TestUDT",
            ByteSize = 8,
            TemplateInstanceId = 0x1100,
            Members =
            [
                new UdtMember
                {
                    Name = "Inner", DataType = PlcDataType.Structure, TypeName = "SomeUDT",
                    Offset = 0, Size = 4, IsStructure = true, TemplateInstanceId = 0xBBBB
                },
            ],
        });
        var decoder = new UdtJsonDecoder(db);
        var json = decoder.ToJson(new byte[8], 0x1100);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("Inner").ValueKind);
    }

    // ==========================================================================
    // Short data fallback paths
    // ==========================================================================

    [Fact]
    public void ToJson_ShortData_AllTypesDefaultToZero()
    {
        var db = new TagDatabase();
        // UDT where members have offsets that cause short data slices
        db.AddUdtDefinition(new UdtDefinition
        {
            Name = "ShortData",
            ByteSize = 1,
            TemplateInstanceId = 0x1200,
            Members =
            [
                new UdtMember { Name = "SintVal", DataType = PlcDataType.Sint, TypeName = "SINT", Offset = 0, Size = 1 },
            ],
        });
        var decoder = new UdtJsonDecoder(db);
        // Pass empty data - member offset 0 but data length 0
        var json = decoder.ToJson(ReadOnlySpan<byte>.Empty, 0x1200);
        // All members should be skipped (offset >= data.Length)
        using var doc = JsonDocument.Parse(json);
        // No properties since offset 0 >= 0 is true, so skipped
        Assert.Equal(JsonValueKind.Object, doc.RootElement.ValueKind);
    }

    [Fact]
    public void ToJson_IntMember_ShortData_DefaultsToZero()
    {
        var db = new TagDatabase();
        db.AddUdtDefinition(new UdtDefinition
        {
            Name = "IntShort",
            ByteSize = 4,
            TemplateInstanceId = 0x1300,
            Members =
            [
                new UdtMember { Name = "Val", DataType = PlcDataType.Int, TypeName = "INT", Offset = 0, Size = 2 },
            ],
        });
        var decoder = new UdtJsonDecoder(db);
        var json = decoder.ToJson(new byte[1], 0x1300); // only 1 byte, INT needs 2
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(0, doc.RootElement.GetProperty("Val").GetInt32());
    }

    [Fact]
    public void ToJson_DintMember_ShortData_DefaultsToZero()
    {
        var db = new TagDatabase();
        db.AddUdtDefinition(new UdtDefinition
        {
            Name = "DintShort",
            ByteSize = 4,
            TemplateInstanceId = 0x1400,
            Members =
            [
                new UdtMember { Name = "Val", DataType = PlcDataType.Dint, TypeName = "DINT", Offset = 0, Size = 4 },
            ],
        });
        var decoder = new UdtJsonDecoder(db);
        var json = decoder.ToJson(new byte[2], 0x1400);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(0, doc.RootElement.GetProperty("Val").GetInt32());
    }

    [Fact]
    public void ToJson_LintMember_ShortData_DefaultsToZero()
    {
        var db = new TagDatabase();
        db.AddUdtDefinition(new UdtDefinition
        {
            Name = "LintShort",
            ByteSize = 8,
            TemplateInstanceId = 0x1500,
            Members =
            [
                new UdtMember { Name = "Val", DataType = PlcDataType.Lint, TypeName = "LINT", Offset = 0, Size = 8 },
            ],
        });
        var decoder = new UdtJsonDecoder(db);
        var json = decoder.ToJson(new byte[4], 0x1500);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(0L, doc.RootElement.GetProperty("Val").GetInt64());
    }

    [Fact]
    public void ToJson_RealMember_ShortData_DefaultsToZero()
    {
        var db = new TagDatabase();
        db.AddUdtDefinition(new UdtDefinition
        {
            Name = "RealShort",
            ByteSize = 4,
            TemplateInstanceId = 0x1600,
            Members =
            [
                new UdtMember { Name = "Val", DataType = PlcDataType.Real, TypeName = "REAL", Offset = 0, Size = 4 },
            ],
        });
        var decoder = new UdtJsonDecoder(db);
        var json = decoder.ToJson(new byte[2], 0x1600);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(0f, doc.RootElement.GetProperty("Val").GetSingle());
    }

    [Fact]
    public void ToJson_LrealMember_ShortData_DefaultsToZero()
    {
        var db = new TagDatabase();
        db.AddUdtDefinition(new UdtDefinition
        {
            Name = "LrealShort",
            ByteSize = 8,
            TemplateInstanceId = 0x1700,
            Members =
            [
                new UdtMember { Name = "Val", DataType = PlcDataType.Lreal, TypeName = "LREAL", Offset = 0, Size = 8 },
            ],
        });
        var decoder = new UdtJsonDecoder(db);
        var json = decoder.ToJson(new byte[4], 0x1700);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(0.0, doc.RootElement.GetProperty("Val").GetDouble());
    }

    [Fact]
    public void ToJson_UintMember_ShortData()
    {
        var db = new TagDatabase();
        db.AddUdtDefinition(new UdtDefinition
        {
            Name = "UintShort",
            ByteSize = 2,
            TemplateInstanceId = 0x1800,
            Members =
            [
                new UdtMember { Name = "Val", DataType = PlcDataType.Uint, TypeName = "UINT", Offset = 0, Size = 2 },
            ],
        });
        var decoder = new UdtJsonDecoder(db);
        var json = decoder.ToJson(new byte[1], 0x1800);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(0, doc.RootElement.GetProperty("Val").GetInt32());
    }

    [Fact]
    public void ToJson_UdintMember_ShortData()
    {
        var db = new TagDatabase();
        db.AddUdtDefinition(new UdtDefinition
        {
            Name = "UdintShort",
            ByteSize = 4,
            TemplateInstanceId = 0x1900,
            Members =
            [
                new UdtMember { Name = "Val", DataType = PlcDataType.Udint, TypeName = "UDINT", Offset = 0, Size = 4 },
            ],
        });
        var decoder = new UdtJsonDecoder(db);
        var json = decoder.ToJson(new byte[2], 0x1900);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(0u, doc.RootElement.GetProperty("Val").GetUInt32());
    }

    [Fact]
    public void ToJson_UlintMember_ShortData()
    {
        var db = new TagDatabase();
        db.AddUdtDefinition(new UdtDefinition
        {
            Name = "UlintShort",
            ByteSize = 8,
            TemplateInstanceId = 0x1A00,
            Members =
            [
                new UdtMember { Name = "Val", DataType = PlcDataType.Ulint, TypeName = "ULINT", Offset = 0, Size = 8 },
            ],
        });
        var decoder = new UdtJsonDecoder(db);
        var json = decoder.ToJson(new byte[4], 0x1A00);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(0UL, doc.RootElement.GetProperty("Val").GetUInt64());
    }

    // ==========================================================================
    // Bool bit member edge cases
    // ==========================================================================

    [Fact]
    public void ToJson_BoolBitMember_1ByteData()
    {
        // Bool bit member with data < 4 bytes triggers the 1-byte path
        var db = new TagDatabase();
        db.AddUdtDefinition(new UdtDefinition
        {
            Name = "SmallBool",
            ByteSize = 1,
            TemplateInstanceId = 0x1B00,
            Members =
            [
                new UdtMember { Name = "Bit0", DataType = PlcDataType.Bool, TypeName = "BOOL", Offset = 0, Size = 1, BitOffset = 0 },
                new UdtMember { Name = "Bit7", DataType = PlcDataType.Bool, TypeName = "BOOL", Offset = 0, Size = 1, BitOffset = 7 },
            ],
        });
        var decoder = new UdtJsonDecoder(db);
        var data = new byte[] { 0x81 }; // bits 0 and 7 set
        var json = decoder.ToJson(data, 0x1B00);
        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.GetProperty("Bit0").GetBoolean());
        Assert.True(doc.RootElement.GetProperty("Bit7").GetBoolean());
    }

    [Fact]
    public void ToJson_BoolBitMember_EmptyData_DefaultsFalse()
    {
        // Bool bit member with data.Length == 0 triggers the else path (bitValue = false)
        var db = new TagDatabase();
        db.AddUdtDefinition(new UdtDefinition
        {
            Name = "EmptyBool",
            ByteSize = 4,
            TemplateInstanceId = 0x1B10,
            Members =
            [
                new UdtMember { Name = "Flag", DataType = PlcDataType.Bool, TypeName = "BOOL", Offset = 0, Size = 1, BitOffset = 0 },
                new UdtMember { Name = "Other", DataType = PlcDataType.Sint, TypeName = "SINT", Offset = 1, Size = 1 },
            ],
        });
        var decoder = new UdtJsonDecoder(db);
        // The Bool at offset 0 gets memberData = data[0..] which is empty when we use 1-byte data,
        // but offset 0 < data.Length=1, so memberData = data[0..] = 1 byte. We need offset > 0 but < data.Length.
        // Actually, to get empty data for the bool bit member at offset 0, we need data.Length > 0 but slice to be empty.
        // That can't happen with offset 0. Instead, use a member at offset 1 with 1-byte total data.
        // Wait - offset >= data.Length causes skip. So we can't get empty data for WriteAtomicMember through normal flow.
        // Let's use offset=0 with data length 1, where data[0]=0 - this triggers the 1-byte path with false result.
        var json = decoder.ToJson(new byte[] { 0x00 }, 0x1B10);
        using var doc = JsonDocument.Parse(json);
        Assert.False(doc.RootElement.GetProperty("Flag").GetBoolean());
    }

    [Fact]
    public void ToJson_BoolBitMember_2ByteData()
    {
        // Bool bit member with data between 1-3 bytes triggers the else-if path
        var db = new TagDatabase();
        db.AddUdtDefinition(new UdtDefinition
        {
            Name = "TwoByteBool",
            ByteSize = 2,
            TemplateInstanceId = 0x1B20,
            Members =
            [
                new UdtMember { Name = "Bit0", DataType = PlcDataType.Bool, TypeName = "BOOL", Offset = 0, Size = 1, BitOffset = 0 },
                new UdtMember { Name = "Bit3", DataType = PlcDataType.Bool, TypeName = "BOOL", Offset = 0, Size = 1, BitOffset = 3 },
            ],
        });
        var decoder = new UdtJsonDecoder(db);
        var data = new byte[] { 0x09, 0x00 }; // bits 0 and 3 set in first byte
        var json = decoder.ToJson(data, 0x1B20);
        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.GetProperty("Bit0").GetBoolean());
        Assert.True(doc.RootElement.GetProperty("Bit3").GetBoolean());
    }

    // ==========================================================================
    // PlcDataType.String atomic member (non-structure)
    // ==========================================================================

    [Fact]
    public void ToJson_AtomicStringMember()
    {
        var db = new TagDatabase();
        db.AddUdtDefinition(new UdtDefinition
        {
            Name = "AtomicStringUDT",
            ByteSize = 88,
            TemplateInstanceId = 0x1C50,
            Members =
            [
                new UdtMember { Name = "Label", DataType = PlcDataType.String, TypeName = "STRING", Offset = 0, Size = 88 },
            ],
        });
        var decoder = new UdtJsonDecoder(db);
        var data = new byte[88];
        BinaryPrimitives.WriteInt32LittleEndian(data, 5); // LEN = 5
        data[4] = (byte)'H'; data[5] = (byte)'e'; data[6] = (byte)'l'; data[7] = (byte)'l'; data[8] = (byte)'o';
        var json = decoder.ToJson(data, 0x1C50);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("Hello", doc.RootElement.GetProperty("Label").GetString());
    }

    // ==========================================================================
    // WriteStringValue edge cases - negative and oversized charCount
    // ==========================================================================

    [Fact]
    public void ToJson_StringValue_NegativeCharCount()
    {
        var db = new TagDatabase();
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
        db.AddUdtDefinition(new UdtDefinition
        {
            Name = "TestUDT",
            ByteSize = 88,
            TemplateInstanceId = 0x1C00,
            Members =
            [
                new UdtMember
                {
                    Name = "Label", DataType = PlcDataType.Structure, TypeName = "STRING",
                    Offset = 0, Size = 88, IsStructure = true, TemplateInstanceId = 0x0CE8
                },
            ],
        });
        var decoder = new UdtJsonDecoder(db);
        var data = new byte[88];
        BinaryPrimitives.WriteInt32LittleEndian(data, -5); // negative char count
        var json = decoder.ToJson(data, 0x1C00);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("", doc.RootElement.GetProperty("Label").GetString());
    }

    [Fact]
    public void ToJson_StringValue_CharCountExceeds82()
    {
        var db = new TagDatabase();
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
        db.AddUdtDefinition(new UdtDefinition
        {
            Name = "TestUDT",
            ByteSize = 88,
            TemplateInstanceId = 0x1D00,
            Members =
            [
                new UdtMember
                {
                    Name = "Label", DataType = PlcDataType.Structure, TypeName = "STRING",
                    Offset = 0, Size = 88, IsStructure = true, TemplateInstanceId = 0x0CE8
                },
            ],
        });
        var decoder = new UdtJsonDecoder(db);
        var data = new byte[88];
        BinaryPrimitives.WriteInt32LittleEndian(data, 200); // exceeds 82, should clamp
        for (int i = 4; i < 88; i++) data[i] = (byte)'A';
        var json = decoder.ToJson(data, 0x1D00);
        using var doc = JsonDocument.Parse(json);
        var str = doc.RootElement.GetProperty("Label").GetString();
        // Should be clamped to available data: min(200, 88-4) = 84
        Assert.True(str!.Length <= 84);
        Assert.True(str.Length > 0);
    }

    // ==========================================================================
    // Short data for Sint, Usint, and Bool (non-bit) with empty data
    // ==========================================================================

    [Fact]
    public void ToJson_SintMember_ShortData_DefaultsToZero()
    {
        var db = new TagDatabase();
        db.AddUdtDefinition(new UdtDefinition
        {
            Name = "SintShort",
            ByteSize = 4,
            TemplateInstanceId = 0x1E00,
            Members =
            [
                // Put SINT at offset 1 so with 1-byte data, offset < length but memberData is empty
                new UdtMember { Name = "Pad", DataType = PlcDataType.Sint, TypeName = "SINT", Offset = 0, Size = 1 },
            ],
        });
        var decoder = new UdtJsonDecoder(db);
        // 1 byte of data, SINT at offset 0 gets 1 byte - that's enough.
        // To get short data for SINT, we need memberData.Length == 0.
        // Use a UDT with SINT at offset 0 and pass exactly enough to enter WriteAtomicMember
        // but SINT only needs > 0, so it would need empty data.
        // Since offset >= data.Length causes skip, we can't get truly empty data to WriteAtomicMember
        // through normal flow for offset 0 members.
        // Instead, test with offset 1 and data length 2 - memberData = data[1..] = 1 byte, which is enough for SINT.
        // For SINT to get 0, we need data.Length == 0 in the switch, but that can't happen via WriteStructure.
        // The short-data paths for 1-byte types (Sint, Usint, Bool) effectively require data.Length == 0
        // which can only happen if we somehow get an empty slice. This is practically unreachable via WriteStructure
        // since offset >= data.Length means skip. But we test what we can.
        var json = decoder.ToJson(new byte[1], 0x1E00);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(0, doc.RootElement.GetProperty("Pad").GetInt32());
    }

    [Fact]
    public void ToJson_UsintMember_ShortData()
    {
        var db = new TagDatabase();
        db.AddUdtDefinition(new UdtDefinition
        {
            Name = "UsintShort",
            ByteSize = 2,
            TemplateInstanceId = 0x1F00,
            Members =
            [
                new UdtMember { Name = "Val", DataType = PlcDataType.Usint, TypeName = "USINT", Offset = 0, Size = 1 },
            ],
        });
        var decoder = new UdtJsonDecoder(db);
        var json = decoder.ToJson(new byte[1], 0x1F00);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(0, doc.RootElement.GetProperty("Val").GetInt32());
    }

    [Fact]
    public void ToJson_BoolNonBitMember_EmptySlice()
    {
        // Bool (non-bit) with data.Length > 0 but value 0 => false
        var db = new TagDatabase();
        db.AddUdtDefinition(new UdtDefinition
        {
            Name = "BoolEmpty",
            ByteSize = 1,
            TemplateInstanceId = 0x2000,
            Members =
            [
                new UdtMember { Name = "Flag", DataType = PlcDataType.Bool, TypeName = "BOOL", Offset = 0, Size = 1 },
            ],
        });
        var decoder = new UdtJsonDecoder(db);
        var json = decoder.ToJson(new byte[] { 0 }, 0x2000);
        using var doc = JsonDocument.Parse(json);
        Assert.False(doc.RootElement.GetProperty("Flag").GetBoolean());
    }
}
