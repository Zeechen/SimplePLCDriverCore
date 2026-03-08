using System.Buffers.Binary;
using SimplePLCDriverCore.Abstractions;
using SimplePLCDriverCore.Protocols.EtherNetIP.Cip;
using SimplePLCDriverCore.TypeSystem;

namespace SimplePLCDriverCore.Tests.TypeSystem;

public class StructureCodecTests
{
    private static TagDatabase CreateDatabaseWithUdt()
    {
        var db = new TagDatabase();

        // Simple UDT: { IntField: DINT @ 0, FloatField: REAL @ 4 }
        db.AddUdtDefinition(new UdtDefinition
        {
            Name = "SimpleUDT",
            ByteSize = 8,
            TemplateInstanceId = 0x0100,
            Members =
            [
                new UdtMember
                {
                    Name = "IntField",
                    DataType = PlcDataType.Dint,
                    TypeName = "DINT",
                    Offset = 0,
                    Size = 4,
                },
                new UdtMember
                {
                    Name = "FloatField",
                    DataType = PlcDataType.Real,
                    TypeName = "REAL",
                    Offset = 4,
                    Size = 4,
                },
            ],
        });

        return db;
    }

    private static TagDatabase CreateDatabaseWithBoolUdt()
    {
        var db = new TagDatabase();

        // UDT with bool bit members
        db.AddUdtDefinition(new UdtDefinition
        {
            Name = "BoolUDT",
            ByteSize = 4,
            TemplateInstanceId = 0x0200,
            Members =
            [
                new UdtMember
                {
                    Name = "Flag0",
                    DataType = PlcDataType.Bool,
                    TypeName = "BOOL",
                    Offset = 0,
                    Size = 1,
                    BitOffset = 0,
                },
                new UdtMember
                {
                    Name = "Flag1",
                    DataType = PlcDataType.Bool,
                    TypeName = "BOOL",
                    Offset = 0,
                    Size = 1,
                    BitOffset = 1,
                },
                new UdtMember
                {
                    Name = "Flag7",
                    DataType = PlcDataType.Bool,
                    TypeName = "BOOL",
                    Offset = 0,
                    Size = 1,
                    BitOffset = 7,
                },
            ],
        });

        return db;
    }

    // --- Decoder Tests ---

    [Fact]
    public void Decoder_DecodeSimpleUdt()
    {
        var db = CreateDatabaseWithUdt();
        var decoder = new StructureDecoder(db);

        // Build 8 bytes: DINT=42 at offset 0, REAL=3.14f at offset 4
        var data = new byte[8];
        BinaryPrimitives.WriteInt32LittleEndian(data, 42);
        BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(4), 3.14f);

        var result = decoder.Decode(data, 0x0100);

        Assert.Equal(PlcDataType.Structure, result.DataType);
        var members = result.AsStructure();
        Assert.NotNull(members);
        Assert.Equal(2, members.Count);
        Assert.Equal(42, members["IntField"].AsInt32());
        Assert.Equal(3.14f, members["FloatField"].AsSingle());
    }

    [Fact]
    public void Decoder_DecodeBoolBitMembers()
    {
        var db = CreateDatabaseWithBoolUdt();
        var decoder = new StructureDecoder(db);

        // Bit 0 = true, bit 1 = false, bit 7 = true -> 0b10000001 = 0x81
        var data = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(data, 0x00000081);

        var result = decoder.Decode(data, 0x0200);

        var members = result.AsStructure();
        Assert.NotNull(members);
        Assert.True(members["Flag0"].AsBoolean());
        Assert.False(members["Flag1"].AsBoolean());
        Assert.True(members["Flag7"].AsBoolean());
    }

    [Fact]
    public void Decoder_UnknownTemplateId_ReturnsRawBytes()
    {
        var db = new TagDatabase();
        var decoder = new StructureDecoder(db);

        var data = new byte[] { 1, 2, 3, 4 };
        var result = decoder.Decode(data, 0xFFFF);

        Assert.Equal(PlcDataType.Structure, result.DataType);
        Assert.IsType<byte[]>(result.RawValue);
    }

    // --- Encoder Tests ---

    [Fact]
    public void Encoder_EncodeSimpleUdt()
    {
        var db = CreateDatabaseWithUdt();
        var encoder = new StructureEncoder(db);

        var members = new Dictionary<string, object>
        {
            ["IntField"] = 42,
            ["FloatField"] = 3.14f,
        };

        var encoded = encoder.Encode(members, 0x0100);

        Assert.Equal(8, encoded.Length);
        Assert.Equal(42, BinaryPrimitives.ReadInt32LittleEndian(encoded));
        Assert.Equal(3.14f, BinaryPrimitives.ReadSingleLittleEndian(encoded.AsSpan(4)));
    }

    [Fact]
    public void Encoder_EncodeBoolBitMembers()
    {
        var db = CreateDatabaseWithBoolUdt();
        var encoder = new StructureEncoder(db);

        var members = new Dictionary<string, object>
        {
            ["Flag0"] = true,
            ["Flag1"] = false,
            ["Flag7"] = true,
        };

        var encoded = encoder.Encode(members, 0x0200);

        Assert.Equal(4, encoded.Length);
        var word = BinaryPrimitives.ReadUInt32LittleEndian(encoded);
        Assert.True((word & 1) != 0);     // bit 0 = true
        Assert.False((word & 2) != 0);    // bit 1 = false
        Assert.True((word & 128) != 0);   // bit 7 = true
    }

    [Fact]
    public void Encoder_UnknownTemplateId_Throws()
    {
        var db = new TagDatabase();
        var encoder = new StructureEncoder(db);

        var members = new Dictionary<string, object> { ["X"] = 1 };

        Assert.Throws<InvalidOperationException>(() => encoder.Encode(members, 0xFFFF));
    }

    [Fact]
    public void Encoder_PartialMembers_OnlyEncodeProvided()
    {
        var db = CreateDatabaseWithUdt();
        var encoder = new StructureEncoder(db);

        // Only provide IntField, FloatField should remain zero
        var members = new Dictionary<string, object>
        {
            ["IntField"] = 99,
        };

        var encoded = encoder.Encode(members, 0x0100);

        Assert.Equal(8, encoded.Length);
        Assert.Equal(99, BinaryPrimitives.ReadInt32LittleEndian(encoded));
        Assert.Equal(0.0f, BinaryPrimitives.ReadSingleLittleEndian(encoded.AsSpan(4)));
    }

    // --- STRING Structure Tests ---

    private static TagDatabase CreateDatabaseWithStringUdt()
    {
        var db = new TagDatabase();

        // STRING template (LEN + DATA)
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

        return db;
    }

    private static TagDatabase CreateDatabaseWithUdtContainingString()
    {
        var db = CreateDatabaseWithStringUdt();

        // Parent UDT with a STRING member
        db.AddUdtDefinition(new UdtDefinition
        {
            Name = "DeviceInfo",
            ByteSize = 92, // 4 (DINT) + 88 (STRING)
            TemplateInstanceId = 0x0300,
            Members =
            [
                new UdtMember { Name = "ID", DataType = PlcDataType.Dint, TypeName = "DINT", Offset = 0, Size = 4 },
                new UdtMember
                {
                    Name = "LocationName", DataType = PlcDataType.Structure, TypeName = "STRING",
                    Offset = 4, Size = 88, IsStructure = true, TemplateInstanceId = 0x0CE8
                },
            ],
        });

        return db;
    }

    [Fact]
    public void Decoder_StringStructure_DecodesAsText()
    {
        var db = CreateDatabaseWithStringUdt();
        var decoder = new StructureDecoder(db);

        // Build STRING bytes: LEN=5, DATA="Hello"
        var data = new byte[88];
        BinaryPrimitives.WriteInt32LittleEndian(data, 5);
        data[4] = (byte)'H'; data[5] = (byte)'e'; data[6] = (byte)'l';
        data[7] = (byte)'l'; data[8] = (byte)'o';

        var result = decoder.Decode(data, 0x0CE8);

        Assert.Equal(PlcDataType.String, result.DataType);
        Assert.Equal("Hello", result.AsString());
    }

    [Fact]
    public void Decoder_UdtWithStringMember_DecodesStringAsText()
    {
        var db = CreateDatabaseWithUdtContainingString();
        var decoder = new StructureDecoder(db);

        // Build DeviceInfo bytes: ID=42 + STRING "Lab"
        var data = new byte[92];
        BinaryPrimitives.WriteInt32LittleEndian(data, 42); // ID
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(4), 3); // STRING LEN
        data[8] = (byte)'L'; data[9] = (byte)'a'; data[10] = (byte)'b';

        var result = decoder.Decode(data, 0x0300);

        Assert.Equal(PlcDataType.Structure, result.DataType);
        var members = result.AsStructure();
        Assert.NotNull(members);
        Assert.Equal(42, members["ID"].AsInt32());
        Assert.Equal(PlcDataType.String, members["LocationName"].DataType);
        Assert.Equal("Lab", members["LocationName"].AsString());
    }

    [Fact]
    public void Decoder_IsStringUdt_DetectsByNameAndPattern()
    {
        var db = CreateDatabaseWithStringUdt();

        Assert.True(db.IsStringTemplate(0x0CE8));
        Assert.False(db.IsStringTemplate(0x0100)); // non-existent

        // Also test with custom string type name
        db.AddUdtDefinition(new UdtDefinition
        {
            Name = "MyString50",
            ByteSize = 54,
            TemplateInstanceId = 0x0400,
            Members =
            [
                new UdtMember { Name = "LEN", DataType = PlcDataType.Dint, TypeName = "DINT", Offset = 0, Size = 4 },
                new UdtMember { Name = "DATA", DataType = PlcDataType.Sint, TypeName = "SINT", Offset = 4, Size = 1, Dimensions = [50] },
            ],
        });

        Assert.True(db.IsStringTemplate(0x0400)); // custom string detected by LEN+DATA pattern
    }

    // --- UDT with multiple atomic types ---

    private static TagDatabase CreateDatabaseWithAllTypes()
    {
        var db = new TagDatabase();

        db.AddUdtDefinition(new UdtDefinition
        {
            Name = "AllTypesUDT",
            ByteSize = 40,
            TemplateInstanceId = 0x0500,
            Members =
            [
                new UdtMember { Name = "SintField", DataType = PlcDataType.Sint, TypeName = "SINT", Offset = 0, Size = 1 },
                new UdtMember { Name = "IntField", DataType = PlcDataType.Int, TypeName = "INT", Offset = 2, Size = 2 },
                new UdtMember { Name = "DintField", DataType = PlcDataType.Dint, TypeName = "DINT", Offset = 4, Size = 4 },
                new UdtMember { Name = "LintField", DataType = PlcDataType.Lint, TypeName = "LINT", Offset = 8, Size = 8 },
                new UdtMember { Name = "UsintField", DataType = PlcDataType.Usint, TypeName = "USINT", Offset = 16, Size = 1 },
                new UdtMember { Name = "UintField", DataType = PlcDataType.Uint, TypeName = "UINT", Offset = 18, Size = 2 },
                new UdtMember { Name = "UdintField", DataType = PlcDataType.Udint, TypeName = "UDINT", Offset = 20, Size = 4 },
                new UdtMember { Name = "UlintField", DataType = PlcDataType.Ulint, TypeName = "ULINT", Offset = 24, Size = 8 },
                new UdtMember { Name = "RealField", DataType = PlcDataType.Real, TypeName = "REAL", Offset = 32, Size = 4 },
                new UdtMember { Name = "LrealField", DataType = PlcDataType.Lreal, TypeName = "LREAL", Offset = 36, Size = 8 },
            ],
        });

        return db;
    }

    [Fact]
    public void Decoder_DecodeAllAtomicTypes()
    {
        var db = CreateDatabaseWithAllTypes();
        var decoder = new StructureDecoder(db);

        var data = new byte[48]; // extra padding OK
        data[0] = 42; // SINT
        BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(2), 1000); // INT
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(4), 100000); // DINT
        BinaryPrimitives.WriteInt64LittleEndian(data.AsSpan(8), 999999L); // LINT
        data[16] = 200; // USINT
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(18), 50000); // UINT
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(20), 3000000U); // UDINT
        BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(24), 9999999UL); // ULINT
        BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(32), 1.5f); // REAL
        BinaryPrimitives.WriteDoubleLittleEndian(data.AsSpan(36), 2.7); // LREAL

        var result = decoder.Decode(data, 0x0500);
        var members = result.AsStructure();
        Assert.NotNull(members);

        Assert.Equal(42, members["SintField"].AsSByte());
        Assert.Equal(1000, members["IntField"].AsInt16());
        Assert.Equal(100000, members["DintField"].AsInt32());
        Assert.Equal(999999L, members["LintField"].AsInt64());
        Assert.Equal((byte)200, members["UsintField"].AsByte());
        Assert.Equal((ushort)50000, members["UintField"].AsUInt16());
        Assert.Equal(3000000U, members["UdintField"].AsUInt32());
        Assert.Equal(9999999UL, members["UlintField"].AsUInt64());
        Assert.Equal(1.5f, members["RealField"].AsSingle());
        Assert.Equal(2.7, members["LrealField"].AsDouble());
    }

    [Fact]
    public void Encoder_EncodeAllAtomicTypes()
    {
        var db = CreateDatabaseWithAllTypes();
        var encoder = new StructureEncoder(db);

        var members = new Dictionary<string, object>
        {
            ["SintField"] = (sbyte)42,
            ["IntField"] = (short)1000,
            ["DintField"] = 100000,
            ["LintField"] = 999999L,
            ["UsintField"] = (byte)200,
            ["UintField"] = (ushort)50000,
            ["UdintField"] = 3000000U,
            ["UlintField"] = 9999999UL,
            ["RealField"] = 1.5f,
            ["LrealField"] = 2.7,
        };

        var encoded = encoder.Encode(members, 0x0500);

        Assert.Equal(40, encoded.Length);
        Assert.Equal(42, (sbyte)encoded[0]);
        Assert.Equal(1000, BinaryPrimitives.ReadInt16LittleEndian(encoded.AsSpan(2)));
        Assert.Equal(100000, BinaryPrimitives.ReadInt32LittleEndian(encoded.AsSpan(4)));
    }

    // --- Array member tests ---

    private static TagDatabase CreateDatabaseWithArrayUdt()
    {
        var db = new TagDatabase();

        db.AddUdtDefinition(new UdtDefinition
        {
            Name = "ArrayUDT",
            ByteSize = 20,
            TemplateInstanceId = 0x0600,
            Members =
            [
                new UdtMember
                {
                    Name = "Values",
                    DataType = PlcDataType.Dint,
                    TypeName = "DINT",
                    Offset = 0,
                    Size = 4,
                    Dimensions = [5],
                },
            ],
        });

        return db;
    }

    [Fact]
    public void Decoder_DecodeArrayMember()
    {
        var db = CreateDatabaseWithArrayUdt();
        var decoder = new StructureDecoder(db);

        var data = new byte[20];
        for (int i = 0; i < 5; i++)
            BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(i * 4), (i + 1) * 10);

        var result = decoder.Decode(data, 0x0600);
        var members = result.AsStructure();
        Assert.NotNull(members);

        var arr = members["Values"].AsArray();
        Assert.NotNull(arr);
        Assert.Equal(5, arr!.Count);
        Assert.Equal(10, arr[0].AsInt32());
        Assert.Equal(50, arr[4].AsInt32());
    }

    [Fact]
    public void Encoder_EncodeArrayMember()
    {
        var db = CreateDatabaseWithArrayUdt();
        var encoder = new StructureEncoder(db);

        var members = new Dictionary<string, object>
        {
            ["Values"] = new int[] { 10, 20, 30, 40, 50 },
        };

        var encoded = encoder.Encode(members, 0x0600);

        Assert.Equal(20, encoded.Length);
        Assert.Equal(10, BinaryPrimitives.ReadInt32LittleEndian(encoded.AsSpan(0)));
        Assert.Equal(50, BinaryPrimitives.ReadInt32LittleEndian(encoded.AsSpan(16)));
    }

    // --- Nested structure tests ---

    private static TagDatabase CreateDatabaseWithNestedUdt()
    {
        var db = CreateDatabaseWithUdt(); // has SimpleUDT at 0x0100

        db.AddUdtDefinition(new UdtDefinition
        {
            Name = "ParentUDT",
            ByteSize = 12,
            TemplateInstanceId = 0x0700,
            Members =
            [
                new UdtMember { Name = "ID", DataType = PlcDataType.Dint, TypeName = "DINT", Offset = 0, Size = 4 },
                new UdtMember
                {
                    Name = "Child", DataType = PlcDataType.Structure, TypeName = "SimpleUDT",
                    Offset = 4, Size = 8, IsStructure = true, TemplateInstanceId = 0x0100
                },
            ],
        });

        return db;
    }

    [Fact]
    public void Decoder_DecodeNestedStructure()
    {
        var db = CreateDatabaseWithNestedUdt();
        var decoder = new StructureDecoder(db);

        var data = new byte[12];
        BinaryPrimitives.WriteInt32LittleEndian(data, 99); // ID
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(4), 42); // Child.IntField
        BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(8), 3.14f); // Child.FloatField

        var result = decoder.Decode(data, 0x0700);
        var members = result.AsStructure();
        Assert.NotNull(members);
        Assert.Equal(99, members["ID"].AsInt32());

        var child = members["Child"].AsStructure();
        Assert.NotNull(child);
        Assert.Equal(42, child!["IntField"].AsInt32());
        Assert.Equal(3.14f, child["FloatField"].AsSingle());
    }

    [Fact]
    public void Encoder_EncodeNestedStructure()
    {
        var db = CreateDatabaseWithNestedUdt();
        var encoder = new StructureEncoder(db);

        var members = new Dictionary<string, object>
        {
            ["ID"] = 99,
            ["Child"] = new Dictionary<string, object>
            {
                ["IntField"] = 42,
                ["FloatField"] = 3.14f,
            } as IReadOnlyDictionary<string, object>,
        };

        var encoded = encoder.Encode(members, 0x0700);

        Assert.Equal(12, encoded.Length);
        Assert.Equal(99, BinaryPrimitives.ReadInt32LittleEndian(encoded));
        Assert.Equal(42, BinaryPrimitives.ReadInt32LittleEndian(encoded.AsSpan(4)));
        Assert.Equal(3.14f, BinaryPrimitives.ReadSingleLittleEndian(encoded.AsSpan(8)));
    }

    // --- Nested STRING member encoding ---

    [Fact]
    public void Encoder_EncodeStringMember()
    {
        var db = CreateDatabaseWithUdtContainingString();
        var encoder = new StructureEncoder(db);

        var members = new Dictionary<string, object>
        {
            ["ID"] = 42,
            ["LocationName"] = "Hello",
        };

        var encoded = encoder.Encode(members, 0x0300);
        Assert.Equal(92, encoded.Length);
        Assert.Equal(42, BinaryPrimitives.ReadInt32LittleEndian(encoded));
        // The STRING at offset 4: LEN=5 then 'H','e','l','l','o'
        Assert.Equal(5, BinaryPrimitives.ReadInt32LittleEndian(encoded.AsSpan(4)));
        Assert.Equal((byte)'H', encoded[8]);
    }

    // --- Structure array member tests ---

    private static TagDatabase CreateDatabaseWithStructureArray()
    {
        var db = CreateDatabaseWithUdt(); // SimpleUDT @ 0x0100

        db.AddUdtDefinition(new UdtDefinition
        {
            Name = "StructArrayUDT",
            ByteSize = 20,
            TemplateInstanceId = 0x0800,
            Members =
            [
                new UdtMember { Name = "Count", DataType = PlcDataType.Dint, TypeName = "DINT", Offset = 0, Size = 4 },
                new UdtMember
                {
                    Name = "Items", DataType = PlcDataType.Structure, TypeName = "SimpleUDT",
                    Offset = 4, Size = 8, IsStructure = true, TemplateInstanceId = 0x0100,
                    Dimensions = [2],
                },
            ],
        });

        return db;
    }

    [Fact]
    public void Decoder_DecodeStructureArrayMember()
    {
        var db = CreateDatabaseWithStructureArray();
        var decoder = new StructureDecoder(db);

        var data = new byte[20];
        BinaryPrimitives.WriteInt32LittleEndian(data, 2); // Count
        // Item 0: IntField=10, FloatField=1.0
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(4), 10);
        BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(8), 1.0f);
        // Item 1: IntField=20, FloatField=2.0
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(12), 20);
        BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(16), 2.0f);

        var result = decoder.Decode(data, 0x0800);
        var members = result.AsStructure();
        Assert.NotNull(members);
        Assert.Equal(2, members["Count"].AsInt32());

        var items = members["Items"].AsArray();
        Assert.NotNull(items);
        Assert.Equal(2, items!.Count);

        var item0 = items[0].AsStructure();
        Assert.NotNull(item0);
        Assert.Equal(10, item0!["IntField"].AsInt32());
    }

    // --- Decoder: member offset out of range ---

    [Fact]
    public void Decoder_MemberOffsetOutOfRange_Skips()
    {
        var db = new TagDatabase();
        db.AddUdtDefinition(new UdtDefinition
        {
            Name = "TruncatedUDT",
            ByteSize = 100,
            TemplateInstanceId = 0x0900,
            Members =
            [
                new UdtMember { Name = "Field1", DataType = PlcDataType.Dint, TypeName = "DINT", Offset = 0, Size = 4 },
                new UdtMember { Name = "Field2", DataType = PlcDataType.Dint, TypeName = "DINT", Offset = 200, Size = 4 }, // out of range
            ],
        });

        var decoder = new StructureDecoder(db);
        var data = new byte[8];
        BinaryPrimitives.WriteInt32LittleEndian(data, 42);

        var result = decoder.Decode(data, 0x0900);
        var members = result.AsStructure();
        Assert.NotNull(members);
        Assert.Single(members); // only Field1 decoded
        Assert.Equal(42, members["Field1"].AsInt32());
    }

    // --- Decoder: unknown data type member ---

    [Fact]
    public void Decoder_UnknownDataType_ReturnsRawBytes()
    {
        var db = new TagDatabase();
        db.AddUdtDefinition(new UdtDefinition
        {
            Name = "WeirdUDT",
            ByteSize = 4,
            TemplateInstanceId = 0x0A00,
            Members =
            [
                new UdtMember { Name = "Unknown", DataType = PlcDataType.Unknown, TypeName = "???", Offset = 0, Size = 4 },
            ],
        });

        var decoder = new StructureDecoder(db);
        var data = new byte[] { 0x01, 0x02, 0x03, 0x04 };

        var result = decoder.Decode(data, 0x0A00);
        var members = result.AsStructure();
        Assert.NotNull(members);
        Assert.Equal(PlcDataType.Unknown, members["Unknown"].DataType);
    }

    // --- Decoder: nested structure without template ID ---

    [Fact]
    public void Decoder_NestedStructure_NoTemplateId_StringFallback()
    {
        var db = new TagDatabase();
        db.AddUdtDefinition(new UdtDefinition
        {
            Name = "StringishUDT",
            ByteSize = 92,
            TemplateInstanceId = 0x0B00,
            Members =
            [
                new UdtMember
                {
                    Name = "StringMember", DataType = PlcDataType.Structure, TypeName = "STRING",
                    Offset = 0, Size = 88, IsStructure = true, TemplateInstanceId = 0, // no template ID
                },
            ],
        });

        var decoder = new StructureDecoder(db);
        var data = new byte[92];
        BinaryPrimitives.WriteInt32LittleEndian(data, 5); // string length
        data[4] = (byte)'H'; data[5] = (byte)'e'; data[6] = (byte)'l';
        data[7] = (byte)'l'; data[8] = (byte)'o';

        var result = decoder.Decode(data, 0x0B00);
        var members = result.AsStructure();
        Assert.NotNull(members);
        Assert.Equal("Hello", members["StringMember"].AsString());
    }

    // --- PlcDataTypeToCipType branches ---

    [Fact]
    public void Encoder_BoolBitMember_SingleByte()
    {
        var db = new TagDatabase();
        db.AddUdtDefinition(new UdtDefinition
        {
            Name = "SmallBoolUDT",
            ByteSize = 1,
            TemplateInstanceId = 0x0C00,
            Members =
            [
                new UdtMember { Name = "Bit0", DataType = PlcDataType.Bool, TypeName = "BOOL", Offset = 0, Size = 1, BitOffset = 0 },
                new UdtMember { Name = "Bit3", DataType = PlcDataType.Bool, TypeName = "BOOL", Offset = 0, Size = 1, BitOffset = 3 },
            ],
        });

        var encoder = new StructureEncoder(db);
        var members = new Dictionary<string, object>
        {
            ["Bit0"] = true,
            ["Bit3"] = true,
        };

        var encoded = encoder.EncodeStructure(members, db.GetUdtByTemplateId(0x0C00)!);
        Assert.Single(encoded);
        Assert.Equal(0x09, encoded[0]); // bit 0 and bit 3 set
    }

    [Fact]
    public void Decoder_BoolBitMember_SingleByte()
    {
        var db = new TagDatabase();
        db.AddUdtDefinition(new UdtDefinition
        {
            Name = "SmallBoolUDT",
            ByteSize = 1,
            TemplateInstanceId = 0x0C00,
            Members =
            [
                new UdtMember { Name = "Bit0", DataType = PlcDataType.Bool, TypeName = "BOOL", Offset = 0, Size = 1, BitOffset = 0 },
                new UdtMember { Name = "Bit3", DataType = PlcDataType.Bool, TypeName = "BOOL", Offset = 0, Size = 1, BitOffset = 3 },
            ],
        });

        var decoder = new StructureDecoder(db);
        var data = new byte[] { 0x09 }; // bits 0 and 3

        var result = decoder.DecodeStructure(data, db.GetUdtByTemplateId(0x0C00)!);
        var members = result.AsStructure();
        Assert.NotNull(members);
        Assert.True(members["Bit0"].AsBoolean());
        Assert.True(members["Bit3"].AsBoolean());
    }

    // --- Round-Trip Tests ---

    [Fact]
    public void RoundTrip_EncodeAndDecode()
    {
        var db = CreateDatabaseWithUdt();
        var encoder = new StructureEncoder(db);
        var decoder = new StructureDecoder(db);

        var original = new Dictionary<string, object>
        {
            ["IntField"] = 12345,
            ["FloatField"] = 2.718f,
        };

        var encoded = encoder.Encode(original, 0x0100);
        var decoded = decoder.Decode(encoded, 0x0100);

        var members = decoded.AsStructure();
        Assert.NotNull(members);
        Assert.Equal(12345, members["IntField"].AsInt32());
        Assert.Equal(2.718f, members["FloatField"].AsSingle());
    }

    [Fact]
    public void RoundTrip_AllAtomicTypes()
    {
        var db = CreateDatabaseWithAllTypes();
        var encoder = new StructureEncoder(db);
        var decoder = new StructureDecoder(db);

        var original = new Dictionary<string, object>
        {
            ["SintField"] = (sbyte)-10,
            ["IntField"] = (short)5000,
            ["DintField"] = 100000,
            ["LintField"] = 999999L,
            ["UsintField"] = (byte)200,
            ["UintField"] = (ushort)50000,
            ["UdintField"] = 3000000U,
            ["UlintField"] = 9999999UL,
            ["RealField"] = 1.5f,
            ["LrealField"] = 2.7,
        };

        var encoded = encoder.Encode(original, 0x0500);
        var decoded = decoder.Decode(encoded, 0x0500);

        var members = decoded.AsStructure();
        Assert.NotNull(members);
        Assert.Equal(-10, members["SintField"].AsSByte());
        Assert.Equal(5000, members["IntField"].AsInt16());
        Assert.Equal(100000, members["DintField"].AsInt32());
    }

    // --- UdtDefinition/UdtMember ToString ---

    [Fact]
    public void UdtDefinition_ToString()
    {
        var udt = new UdtDefinition
        {
            Name = "TestUDT",
            ByteSize = 16,
            Members = new UdtMember[]
            {
                new() { Name = "Field1", TypeName = "DINT" },
            },
        };
        Assert.Contains("TestUDT", udt.ToString());
        Assert.Contains("16 bytes", udt.ToString());
    }

    [Fact]
    public void UdtMember_ToString()
    {
        var member = new UdtMember
        {
            Name = "MyField",
            TypeName = "REAL",
            Offset = 4,
        };
        Assert.Contains("MyField", member.ToString());
        Assert.Contains("REAL", member.ToString());
        Assert.Contains("offset 4", member.ToString());
    }

    // =======================================================================
    // Encoder short-data / missing-data branches for Bool
    // =======================================================================

    [Fact]
    public void Encoder_BoolBitMember_ZeroBytesTarget_DoesNotThrow()
    {
        // UDT with ByteSize=0 means the result array is empty, so Bool encoding
        // hits the "target.Length < 1" path and nothing is written.
        var db = new TagDatabase();
        db.AddUdtDefinition(new UdtDefinition
        {
            Name = "EmptyUDT",
            ByteSize = 0,
            TemplateInstanceId = 0x1000,
            Members =
            [
                new UdtMember { Name = "Flag", DataType = PlcDataType.Bool, TypeName = "BOOL", Offset = 0, Size = 1, BitOffset = 0 },
            ],
        });

        var encoder = new StructureEncoder(db);
        var members = new Dictionary<string, object> { ["Flag"] = true };

        // Offset >= result.Length so member is skipped
        var encoded = encoder.EncodeStructure(members, db.GetUdtByTemplateId(0x1000)!);
        Assert.Empty(encoded);
    }

    // =======================================================================
    // Encoder: Bool with false clears bits (single byte path)
    // =======================================================================

    [Fact]
    public void Encoder_BoolBitMember_SingleByte_ClearBit()
    {
        var db = new TagDatabase();
        db.AddUdtDefinition(new UdtDefinition
        {
            Name = "SmallBoolUDT2",
            ByteSize = 1,
            TemplateInstanceId = 0x1001,
            Members =
            [
                new UdtMember { Name = "Bit0", DataType = PlcDataType.Bool, TypeName = "BOOL", Offset = 0, Size = 1, BitOffset = 0 },
                new UdtMember { Name = "Bit3", DataType = PlcDataType.Bool, TypeName = "BOOL", Offset = 0, Size = 1, BitOffset = 3 },
            ],
        });

        var encoder = new StructureEncoder(db);
        // Set Bit0=false, Bit3=false explicitly (exercises the clear-bit path)
        var members = new Dictionary<string, object>
        {
            ["Bit0"] = false,
            ["Bit3"] = false,
        };

        var encoded = encoder.EncodeStructure(members, db.GetUdtByTemplateId(0x1001)!);
        Assert.Single(encoded);
        Assert.Equal(0x00, encoded[0]);
    }

    // =======================================================================
    // Encoder: Bool 4-byte word with false clears bits
    // =======================================================================

    [Fact]
    public void Encoder_BoolBitMember_4ByteWord_ClearBit()
    {
        var db = CreateDatabaseWithBoolUdt(); // 4 byte, template 0x0200
        var encoder = new StructureEncoder(db);

        // All false -> all bits cleared
        var members = new Dictionary<string, object>
        {
            ["Flag0"] = false,
            ["Flag1"] = false,
            ["Flag7"] = false,
        };

        var encoded = encoder.Encode(members, 0x0200);
        var word = BinaryPrimitives.ReadUInt32LittleEndian(encoded);
        Assert.Equal(0u, word);
    }

    // =======================================================================
    // Encoder: Int member with target too short (encoded.Length > target.Length)
    // =======================================================================

    [Fact]
    public void Encoder_AtomicMember_TargetTooShort_DataNotWritten()
    {
        // INT needs 2 bytes but ByteSize is only 1 -> encoded won't fit
        var db = new TagDatabase();
        db.AddUdtDefinition(new UdtDefinition
        {
            Name = "TinyUDT",
            ByteSize = 1,
            TemplateInstanceId = 0x1002,
            Members =
            [
                new UdtMember { Name = "Value", DataType = PlcDataType.Int, TypeName = "INT", Offset = 0, Size = 2 },
            ],
        });

        var encoder = new StructureEncoder(db);
        var members = new Dictionary<string, object> { ["Value"] = (short)1234 };

        // The encoded 2 bytes can't fit in 1-byte target, so nothing written
        var encoded = encoder.EncodeStructure(members, db.GetUdtByTemplateId(0x1002)!);
        Assert.Single(encoded);
        Assert.Equal(0, encoded[0]);
    }

    // =======================================================================
    // Encoder: Array member with IReadOnlyList<object> path
    // =======================================================================

    [Fact]
    public void Encoder_ArrayMember_ReadOnlyList()
    {
        var db = CreateDatabaseWithArrayUdt(); // DINT[5] at offset 0, 20 bytes
        var encoder = new StructureEncoder(db);

        var list = new List<object> { 10, 20, 30, 40, 50 } as IReadOnlyList<object>;
        var members = new Dictionary<string, object> { ["Values"] = list };

        var encoded = encoder.Encode(members, 0x0600);
        Assert.Equal(20, encoded.Length);
        Assert.Equal(10, BinaryPrimitives.ReadInt32LittleEndian(encoded.AsSpan(0)));
        Assert.Equal(50, BinaryPrimitives.ReadInt32LittleEndian(encoded.AsSpan(16)));
    }

    // =======================================================================
    // Encoder: Array member encoded too large for target
    // =======================================================================

    [Fact]
    public void Encoder_ArrayMember_EncodedTooLargeForTarget()
    {
        // Array of 5 DINTs = 20 bytes but ByteSize is only 8
        var db = new TagDatabase();
        db.AddUdtDefinition(new UdtDefinition
        {
            Name = "SmallArrayUDT",
            ByteSize = 8,
            TemplateInstanceId = 0x1003,
            Members =
            [
                new UdtMember { Name = "Values", DataType = PlcDataType.Dint, TypeName = "DINT", Offset = 0, Size = 4, Dimensions = [5] },
            ],
        });

        var encoder = new StructureEncoder(db);
        var members = new Dictionary<string, object>
        {
            ["Values"] = new int[] { 10, 20, 30, 40, 50 },
        };

        // 5 DINTs = 20 bytes > 8 byte target -> nothing written (encoded.Length > target.Length)
        var encoded = encoder.EncodeStructure(members, db.GetUdtByTemplateId(0x1003)!);
        Assert.Equal(8, encoded.Length);
        // Data should remain zeroed since the encoded array was too large
        Assert.Equal(0, BinaryPrimitives.ReadInt32LittleEndian(encoded.AsSpan(0)));
    }

    // =======================================================================
    // Encoder: Array member with IReadOnlyList<object> too large
    // =======================================================================

    [Fact]
    public void Encoder_ArrayMember_ReadOnlyList_TooLarge()
    {
        var db = new TagDatabase();
        db.AddUdtDefinition(new UdtDefinition
        {
            Name = "SmallArrayUDT2",
            ByteSize = 8,
            TemplateInstanceId = 0x1004,
            Members =
            [
                new UdtMember { Name = "Values", DataType = PlcDataType.Dint, TypeName = "DINT", Offset = 0, Size = 4, Dimensions = [5] },
            ],
        });

        var encoder = new StructureEncoder(db);
        var list = new List<object> { 10, 20, 30, 40, 50 } as IReadOnlyList<object>;
        var members = new Dictionary<string, object> { ["Values"] = list };

        var encoded = encoder.EncodeStructure(members, db.GetUdtByTemplateId(0x1004)!);
        Assert.Equal(8, encoded.Length);
        Assert.Equal(0, BinaryPrimitives.ReadInt32LittleEndian(encoded.AsSpan(0)));
    }

    // =======================================================================
    // Encoder: STRING member via EncodeNestedStructure (string value, STRING type)
    // =======================================================================

    [Fact]
    public void Encoder_StringMember_ViaStringValue()
    {
        var db = CreateDatabaseWithUdtContainingString();
        var encoder = new StructureEncoder(db);

        // Pass string directly as value for the STRING structure member
        var members = new Dictionary<string, object>
        {
            ["ID"] = 1,
            ["LocationName"] = "Test",
        };

        var encoded = encoder.Encode(members, 0x0300);
        Assert.Equal(92, encoded.Length);
        Assert.Equal(1, BinaryPrimitives.ReadInt32LittleEndian(encoded));
        // STRING at offset 4: LEN=4 then 'T','e','s','t'
        Assert.Equal(4, BinaryPrimitives.ReadInt32LittleEndian(encoded.AsSpan(4)));
        Assert.Equal((byte)'T', encoded[8]);
        Assert.Equal((byte)'e', encoded[9]);
        Assert.Equal((byte)'s', encoded[10]);
        Assert.Equal((byte)'t', encoded[11]);
    }

    // =======================================================================
    // Encoder: PlcDataType.String as atomic member
    // =======================================================================

    [Fact]
    public void Encoder_AtomicStringMember()
    {
        var db = new TagDatabase();
        db.AddUdtDefinition(new UdtDefinition
        {
            Name = "StringAtomicUDT",
            ByteSize = 92,
            TemplateInstanceId = 0x1005,
            Members =
            [
                new UdtMember { Name = "Text", DataType = PlcDataType.String, TypeName = "STRING", Offset = 0, Size = 88 },
            ],
        });

        var encoder = new StructureEncoder(db);
        var members = new Dictionary<string, object> { ["Text"] = "Hi" };

        var encoded = encoder.EncodeStructure(members, db.GetUdtByTemplateId(0x1005)!);
        Assert.Equal(92, encoded.Length);
        // EncodeValue for String produces LEN prefix + chars
        Assert.Equal(2, BinaryPrimitives.ReadInt32LittleEndian(encoded));
        Assert.Equal((byte)'H', encoded[4]);
        Assert.Equal((byte)'i', encoded[5]);
    }

    // =======================================================================
    // Encoder: Unknown data type member (cipType == 0) is skipped
    // =======================================================================

    [Fact]
    public void Encoder_UnknownDataType_Skipped()
    {
        var db = new TagDatabase();
        db.AddUdtDefinition(new UdtDefinition
        {
            Name = "UnknownUDT",
            ByteSize = 4,
            TemplateInstanceId = 0x1006,
            Members =
            [
                new UdtMember { Name = "Unknown", DataType = PlcDataType.Unknown, TypeName = "???", Offset = 0, Size = 4 },
            ],
        });

        var encoder = new StructureEncoder(db);
        var members = new Dictionary<string, object> { ["Unknown"] = 42 };

        var encoded = encoder.EncodeStructure(members, db.GetUdtByTemplateId(0x1006)!);
        Assert.Equal(4, encoded.Length);
        // Should remain zeroed since unknown type is skipped
        Assert.All(encoded, b => Assert.Equal(0, b));
    }

    // =======================================================================
    // Encoder: Array with unknown data type (cipType == 0) is skipped
    // =======================================================================

    [Fact]
    public void Encoder_ArrayMember_UnknownType_Skipped()
    {
        var db = new TagDatabase();
        db.AddUdtDefinition(new UdtDefinition
        {
            Name = "UnknownArrayUDT",
            ByteSize = 8,
            TemplateInstanceId = 0x1007,
            Members =
            [
                new UdtMember { Name = "Arr", DataType = PlcDataType.Unknown, TypeName = "???", Offset = 0, Size = 4, Dimensions = [2] },
            ],
        });

        var encoder = new StructureEncoder(db);
        var members = new Dictionary<string, object> { ["Arr"] = new int[] { 1, 2 } };

        var encoded = encoder.EncodeStructure(members, db.GetUdtByTemplateId(0x1007)!);
        Assert.All(encoded, b => Assert.Equal(0, b));
    }

    // =======================================================================
    // Encoder: Structure array member with templateInstanceId == 0 is skipped
    // =======================================================================

    [Fact]
    public void Encoder_StructureArrayMember_NoTemplateId_Skipped()
    {
        var db = new TagDatabase();
        db.AddUdtDefinition(new UdtDefinition
        {
            Name = "NoTemplateArrayUDT",
            ByteSize = 20,
            TemplateInstanceId = 0x1008,
            Members =
            [
                new UdtMember
                {
                    Name = "Items", DataType = PlcDataType.Structure, TypeName = "SomeStruct",
                    Offset = 0, Size = 8, IsStructure = true, TemplateInstanceId = 0, Dimensions = [2],
                },
            ],
        });

        var encoder = new StructureEncoder(db);
        var list = new List<IReadOnlyDictionary<string, object>>
        {
            new Dictionary<string, object> { ["X"] = 1 },
        } as IReadOnlyList<IReadOnlyDictionary<string, object>>;
        var members = new Dictionary<string, object> { ["Items"] = list };

        var encoded = encoder.EncodeStructure(members, db.GetUdtByTemplateId(0x1008)!);
        Assert.All(encoded, b => Assert.Equal(0, b));
    }

    // =======================================================================
    // Encoder: Structure array member with unknown nested UDT is skipped
    // =======================================================================

    [Fact]
    public void Encoder_StructureArrayMember_UnknownNestedUdt_Skipped()
    {
        var db = new TagDatabase();
        db.AddUdtDefinition(new UdtDefinition
        {
            Name = "MissingNestedUDT",
            ByteSize = 20,
            TemplateInstanceId = 0x1009,
            Members =
            [
                new UdtMember
                {
                    Name = "Items", DataType = PlcDataType.Structure, TypeName = "Missing",
                    Offset = 0, Size = 8, IsStructure = true, TemplateInstanceId = 0xFFFF, Dimensions = [2],
                },
            ],
        });

        var encoder = new StructureEncoder(db);
        var list = new List<IReadOnlyDictionary<string, object>>
        {
            new Dictionary<string, object> { ["X"] = 1 },
        } as IReadOnlyList<IReadOnlyDictionary<string, object>>;
        var members = new Dictionary<string, object> { ["Items"] = list };

        var encoded = encoder.EncodeStructure(members, db.GetUdtByTemplateId(0x1009)!);
        Assert.All(encoded, b => Assert.Equal(0, b));
    }

    // =======================================================================
    // Encoder: Structure array via Array (not IReadOnlyList<IReadOnlyDictionary>)
    // =======================================================================

    [Fact]
    public void Encoder_StructureArrayMember_ViaObjectArray()
    {
        var db = CreateDatabaseWithStructureArray(); // SimpleUDT @ 0x0100, StructArrayUDT @ 0x0800
        var encoder = new StructureEncoder(db);

        var elem0 = new Dictionary<string, object> { ["IntField"] = 10, ["FloatField"] = 1.0f } as IReadOnlyDictionary<string, object>;
        var elem1 = new Dictionary<string, object> { ["IntField"] = 20, ["FloatField"] = 2.0f } as IReadOnlyDictionary<string, object>;
        var arr = new object[] { elem0, elem1 };

        var members = new Dictionary<string, object>
        {
            ["Count"] = 2,
            ["Items"] = arr,
        };

        var encoded = encoder.Encode(members, 0x0800);
        Assert.Equal(20, encoded.Length);
        Assert.Equal(2, BinaryPrimitives.ReadInt32LittleEndian(encoded));
        Assert.Equal(10, BinaryPrimitives.ReadInt32LittleEndian(encoded.AsSpan(4)));
        Assert.Equal(1.0f, BinaryPrimitives.ReadSingleLittleEndian(encoded.AsSpan(8)));
        Assert.Equal(20, BinaryPrimitives.ReadInt32LittleEndian(encoded.AsSpan(12)));
    }

    // =======================================================================
    // Encoder: Nested structure with templateInstanceId == 0 is skipped
    // =======================================================================

    [Fact]
    public void Encoder_NestedStructure_NoTemplateId_Skipped()
    {
        var db = new TagDatabase();
        db.AddUdtDefinition(new UdtDefinition
        {
            Name = "NoTemplateNestedUDT",
            ByteSize = 12,
            TemplateInstanceId = 0x100A,
            Members =
            [
                new UdtMember
                {
                    Name = "Child", DataType = PlcDataType.Structure, TypeName = "SomeStruct",
                    Offset = 0, Size = 8, IsStructure = true, TemplateInstanceId = 0,
                },
            ],
        });

        var encoder = new StructureEncoder(db);
        var child = new Dictionary<string, object> { ["X"] = 1 } as IReadOnlyDictionary<string, object>;
        var members = new Dictionary<string, object> { ["Child"] = child };

        var encoded = encoder.EncodeStructure(members, db.GetUdtByTemplateId(0x100A)!);
        Assert.All(encoded, b => Assert.Equal(0, b));
    }

    // =======================================================================
    // Decoder: SINT with 0 bytes -> short-data fallback
    // =======================================================================

    [Fact]
    public void Decoder_Sint_ShortData_ReturnsFallback()
    {
        var db = new TagDatabase();
        db.AddUdtDefinition(new UdtDefinition
        {
            Name = "SintUDT",
            ByteSize = 1,
            TemplateInstanceId = 0x1100,
            Members =
            [
                new UdtMember { Name = "Val", DataType = PlcDataType.Sint, TypeName = "SINT", Offset = 0, Size = 1 },
            ],
        });

        var decoder = new StructureDecoder(db);
        // Empty data -> member offset 0 >= data.Length (0), so member skipped
        var result = decoder.DecodeStructure(Array.Empty<byte>(), db.GetUdtByTemplateId(0x1100)!);
        var members = result.AsStructure();
        Assert.NotNull(members);
        Assert.Empty(members); // member skipped because offset >= data.Length
    }

    // =======================================================================
    // Decoder: INT with < 2 bytes -> short-data fallback
    // =======================================================================

    [Fact]
    public void Decoder_Int_ShortData_ReturnsFallback()
    {
        var db = new TagDatabase();
        db.AddUdtDefinition(new UdtDefinition
        {
            Name = "IntShortUDT",
            ByteSize = 2,
            TemplateInstanceId = 0x1101,
            Members =
            [
                new UdtMember { Name = "Val", DataType = PlcDataType.Int, TypeName = "INT", Offset = 0, Size = 2 },
            ],
        });

        var decoder = new StructureDecoder(db);
        // Only 1 byte -> INT needs 2, hits atomicSize(2) > data.Length(1)
        var result = decoder.DecodeStructure(new byte[] { 0xFF }, db.GetUdtByTemplateId(0x1101)!);
        var members = result.AsStructure();
        Assert.NotNull(members);
        Assert.Equal(PlcDataType.Unknown, members["Val"].DataType);
    }

    // =======================================================================
    // Decoder: DINT with < 4 bytes -> short-data fallback
    // =======================================================================

    [Fact]
    public void Decoder_Dint_ShortData_ReturnsFallback()
    {
        var db = new TagDatabase();
        db.AddUdtDefinition(new UdtDefinition
        {
            Name = "DintShortUDT",
            ByteSize = 4,
            TemplateInstanceId = 0x1102,
            Members =
            [
                new UdtMember { Name = "Val", DataType = PlcDataType.Dint, TypeName = "DINT", Offset = 0, Size = 4 },
            ],
        });

        var decoder = new StructureDecoder(db);
        var result = decoder.DecodeStructure(new byte[] { 0x01, 0x02 }, db.GetUdtByTemplateId(0x1102)!);
        var members = result.AsStructure();
        Assert.Equal(PlcDataType.Unknown, members["Val"].DataType);
    }

    // =======================================================================
    // Decoder: LINT with < 8 bytes -> short-data fallback
    // =======================================================================

    [Fact]
    public void Decoder_Lint_ShortData_ReturnsFallback()
    {
        var db = new TagDatabase();
        db.AddUdtDefinition(new UdtDefinition
        {
            Name = "LintShortUDT",
            ByteSize = 8,
            TemplateInstanceId = 0x1103,
            Members =
            [
                new UdtMember { Name = "Val", DataType = PlcDataType.Lint, TypeName = "LINT", Offset = 0, Size = 8 },
            ],
        });

        var decoder = new StructureDecoder(db);
        var result = decoder.DecodeStructure(new byte[] { 1, 2, 3, 4 }, db.GetUdtByTemplateId(0x1103)!);
        var members = result.AsStructure();
        Assert.Equal(PlcDataType.Unknown, members["Val"].DataType);
    }

    // =======================================================================
    // Decoder: REAL with < 4 bytes -> short-data fallback
    // =======================================================================

    [Fact]
    public void Decoder_Real_ShortData_ReturnsFallback()
    {
        var db = new TagDatabase();
        db.AddUdtDefinition(new UdtDefinition
        {
            Name = "RealShortUDT",
            ByteSize = 4,
            TemplateInstanceId = 0x1104,
            Members =
            [
                new UdtMember { Name = "Val", DataType = PlcDataType.Real, TypeName = "REAL", Offset = 0, Size = 4 },
            ],
        });

        var decoder = new StructureDecoder(db);
        var result = decoder.DecodeStructure(new byte[] { 0x01 }, db.GetUdtByTemplateId(0x1104)!);
        var members = result.AsStructure();
        Assert.Equal(PlcDataType.Unknown, members["Val"].DataType);
    }

    // =======================================================================
    // Decoder: LREAL with < 8 bytes -> short-data fallback
    // =======================================================================

    [Fact]
    public void Decoder_Lreal_ShortData_ReturnsFallback()
    {
        var db = new TagDatabase();
        db.AddUdtDefinition(new UdtDefinition
        {
            Name = "LrealShortUDT",
            ByteSize = 8,
            TemplateInstanceId = 0x1105,
            Members =
            [
                new UdtMember { Name = "Val", DataType = PlcDataType.Lreal, TypeName = "LREAL", Offset = 0, Size = 8 },
            ],
        });

        var decoder = new StructureDecoder(db);
        var result = decoder.DecodeStructure(new byte[] { 1, 2, 3, 4 }, db.GetUdtByTemplateId(0x1105)!);
        var members = result.AsStructure();
        Assert.Equal(PlcDataType.Unknown, members["Val"].DataType);
    }

    // =======================================================================
    // Decoder: UINT with < 2 bytes -> short-data fallback
    // =======================================================================

    [Fact]
    public void Decoder_Uint_ShortData_ReturnsFallback()
    {
        var db = new TagDatabase();
        db.AddUdtDefinition(new UdtDefinition
        {
            Name = "UintShortUDT",
            ByteSize = 2,
            TemplateInstanceId = 0x1106,
            Members =
            [
                new UdtMember { Name = "Val", DataType = PlcDataType.Uint, TypeName = "UINT", Offset = 0, Size = 2 },
            ],
        });

        var decoder = new StructureDecoder(db);
        var result = decoder.DecodeStructure(new byte[] { 0xFF }, db.GetUdtByTemplateId(0x1106)!);
        var members = result.AsStructure();
        Assert.Equal(PlcDataType.Unknown, members["Val"].DataType);
    }

    // =======================================================================
    // Decoder: UDINT with < 4 bytes -> short-data fallback
    // =======================================================================

    [Fact]
    public void Decoder_Udint_ShortData_ReturnsFallback()
    {
        var db = new TagDatabase();
        db.AddUdtDefinition(new UdtDefinition
        {
            Name = "UdintShortUDT",
            ByteSize = 4,
            TemplateInstanceId = 0x1107,
            Members =
            [
                new UdtMember { Name = "Val", DataType = PlcDataType.Udint, TypeName = "UDINT", Offset = 0, Size = 4 },
            ],
        });

        var decoder = new StructureDecoder(db);
        var result = decoder.DecodeStructure(new byte[] { 1, 2 }, db.GetUdtByTemplateId(0x1107)!);
        var members = result.AsStructure();
        Assert.Equal(PlcDataType.Unknown, members["Val"].DataType);
    }

    // =======================================================================
    // Decoder: ULINT with < 8 bytes -> short-data fallback
    // =======================================================================

    [Fact]
    public void Decoder_Ulint_ShortData_ReturnsFallback()
    {
        var db = new TagDatabase();
        db.AddUdtDefinition(new UdtDefinition
        {
            Name = "UlintShortUDT",
            ByteSize = 8,
            TemplateInstanceId = 0x1108,
            Members =
            [
                new UdtMember { Name = "Val", DataType = PlcDataType.Ulint, TypeName = "ULINT", Offset = 0, Size = 8 },
            ],
        });

        var decoder = new StructureDecoder(db);
        var result = decoder.DecodeStructure(new byte[] { 1, 2, 3, 4 }, db.GetUdtByTemplateId(0x1108)!);
        var members = result.AsStructure();
        Assert.Equal(PlcDataType.Unknown, members["Val"].DataType);
    }

    // =======================================================================
    // Decoder: BOOL with 0 bytes -> returns false
    // =======================================================================

    [Fact]
    public void Decoder_Bool_ZeroBytes_ReturnsFalse()
    {
        var db = new TagDatabase();
        db.AddUdtDefinition(new UdtDefinition
        {
            Name = "BoolZeroUDT",
            ByteSize = 4,
            TemplateInstanceId = 0x1109,
            Members =
            [
                new UdtMember { Name = "Flag", DataType = PlcDataType.Bool, TypeName = "BOOL", Offset = 0, Size = 1, BitOffset = 0 },
                // A second member at a large offset to verify that offset >= data triggers skip
                new UdtMember { Name = "FarFlag", DataType = PlcDataType.Bool, TypeName = "BOOL", Offset = 3, Size = 1, BitOffset = 0 },
            ],
        });

        var decoder = new StructureDecoder(db);
        // 1 byte data: Flag at offset 0 gets 1 byte (single-byte path), FarFlag at offset 3 >= length 1 -> skip
        var result = decoder.DecodeStructure(new byte[] { 0x00 }, db.GetUdtByTemplateId(0x1109)!);
        var members = result.AsStructure();
        Assert.NotNull(members);
        Assert.False(members["Flag"].AsBoolean());
        Assert.DoesNotContain("FarFlag", (IDictionary<string, PlcTagValue>)members);
    }

    // =======================================================================
    // Decoder: STRING as atomic member (PlcDataType.String) -> atomicSize == 0 fallback
    // =======================================================================

    [Fact]
    public void Decoder_StringAtomicMember_FallsBackToUnknown()
    {
        // PlcDataType.String maps to CipDataTypes.String (0xDA), GetAtomicSize returns 0
        // so the decoder hits the atomicSize == 0 fallback
        var db = new TagDatabase();
        db.AddUdtDefinition(new UdtDefinition
        {
            Name = "StringAtomicUDT",
            ByteSize = 88,
            TemplateInstanceId = 0x110A,
            Members =
            [
                new UdtMember { Name = "Text", DataType = PlcDataType.String, TypeName = "STRING", Offset = 0, Size = 88 },
            ],
        });

        var decoder = new StructureDecoder(db);
        var data = new byte[88];
        BinaryPrimitives.WriteInt32LittleEndian(data, 3);
        data[4] = (byte)'A'; data[5] = (byte)'B'; data[6] = (byte)'C';

        var result = decoder.DecodeStructure(data, db.GetUdtByTemplateId(0x110A)!);
        var members = result.AsStructure();
        Assert.NotNull(members);
        // atomicSize for String is 0, so it returns Unknown with raw bytes
        Assert.Equal(PlcDataType.Unknown, members["Text"].DataType);
    }

    // =======================================================================
    // Decoder: Nested structure with unknown template (not in DB) -> STRING fallback
    // =======================================================================

    [Fact]
    public void Decoder_NestedStructure_UnknownTemplate_StringFallback()
    {
        var db = new TagDatabase();
        db.AddUdtDefinition(new UdtDefinition
        {
            Name = "MissingNestedUDT",
            ByteSize = 92,
            TemplateInstanceId = 0x110B,
            Members =
            [
                new UdtMember
                {
                    Name = "StringMember", DataType = PlcDataType.Structure, TypeName = "STRING",
                    Offset = 0, Size = 88, IsStructure = true, TemplateInstanceId = 0xFFFE, // not in DB
                },
            ],
        });

        var decoder = new StructureDecoder(db);
        var data = new byte[92];
        BinaryPrimitives.WriteInt32LittleEndian(data, 4);
        data[4] = (byte)'T'; data[5] = (byte)'e'; data[6] = (byte)'s'; data[7] = (byte)'t';

        var result = decoder.DecodeStructure(data, db.GetUdtByTemplateId(0x110B)!);
        var members = result.AsStructure();
        Assert.NotNull(members);
        Assert.Equal("Test", members["StringMember"].AsString());
    }

    // =======================================================================
    // Decoder: Nested structure with unknown template, non-STRING -> raw bytes
    // =======================================================================

    [Fact]
    public void Decoder_NestedStructure_UnknownTemplate_NonString_RawBytes()
    {
        var db = new TagDatabase();
        db.AddUdtDefinition(new UdtDefinition
        {
            Name = "MissingNestedUDT2",
            ByteSize = 8,
            TemplateInstanceId = 0x110C,
            Members =
            [
                new UdtMember
                {
                    Name = "Child", DataType = PlcDataType.Structure, TypeName = "SomeOtherStruct",
                    Offset = 0, Size = 8, IsStructure = true, TemplateInstanceId = 0xFFFD, // not in DB
                },
            ],
        });

        var decoder = new StructureDecoder(db);
        var data = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };

        var result = decoder.DecodeStructure(data, db.GetUdtByTemplateId(0x110C)!);
        var members = result.AsStructure();
        Assert.NotNull(members);
        Assert.Equal(PlcDataType.Structure, members["Child"].DataType);
        Assert.IsType<byte[]>(members["Child"].RawValue);
    }

    // =======================================================================
    // Decoder: Nested structure with no templateId (0), non-STRING -> raw bytes
    // =======================================================================

    [Fact]
    public void Decoder_NestedStructure_NoTemplateId_NonString_RawBytes()
    {
        var db = new TagDatabase();
        db.AddUdtDefinition(new UdtDefinition
        {
            Name = "NoTemplateNonStringUDT",
            ByteSize = 8,
            TemplateInstanceId = 0x110D,
            Members =
            [
                new UdtMember
                {
                    Name = "Child", DataType = PlcDataType.Structure, TypeName = "SomeStruct",
                    Offset = 0, Size = 8, IsStructure = true, TemplateInstanceId = 0,
                },
            ],
        });

        var decoder = new StructureDecoder(db);
        var data = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };

        var result = decoder.DecodeStructure(data, db.GetUdtByTemplateId(0x110D)!);
        var members = result.AsStructure();
        Assert.NotNull(members);
        Assert.Equal(PlcDataType.Structure, members["Child"].DataType);
        Assert.IsType<byte[]>(members["Child"].RawValue);
    }

    // =======================================================================
    // Decoder: Nested structure with data shorter than nestedUdt.ByteSize
    // =======================================================================

    [Fact]
    public void Decoder_NestedStructure_DataShorterThanExpected()
    {
        var db = CreateDatabaseWithNestedUdt(); // ParentUDT 0x0700 -> SimpleUDT 0x0100 (8 bytes)
        var decoder = new StructureDecoder(db);

        // Only 8 bytes total: ID=99 at offset 0, then only 4 bytes for Child (needs 8)
        var data = new byte[8];
        BinaryPrimitives.WriteInt32LittleEndian(data, 99);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(4), 42); // partial Child (IntField only)

        var result = decoder.DecodeStructure(data, db.GetUdtByTemplateId(0x0700)!);
        var members = result.AsStructure();
        Assert.NotNull(members);
        Assert.Equal(99, members["ID"].AsInt32());
        // Child decoded with shorter data
        var child = members["Child"].AsStructure();
        Assert.NotNull(child);
        Assert.Equal(42, child!["IntField"].AsInt32());
    }

    // =======================================================================
    // Decoder: Structure array with unknown nested template
    // =======================================================================

    [Fact]
    public void Decoder_StructureArrayMember_UnknownTemplate_RawBytes()
    {
        var db = new TagDatabase();
        db.AddUdtDefinition(new UdtDefinition
        {
            Name = "MissingArrayUDT",
            ByteSize = 20,
            TemplateInstanceId = 0x110E,
            Members =
            [
                new UdtMember
                {
                    Name = "Items", DataType = PlcDataType.Structure, TypeName = "Missing",
                    Offset = 0, Size = 8, IsStructure = true, TemplateInstanceId = 0xFFFC, Dimensions = [2],
                },
            ],
        });

        var decoder = new StructureDecoder(db);
        var data = new byte[20];

        var result = decoder.DecodeStructure(data, db.GetUdtByTemplateId(0x110E)!);
        var members = result.AsStructure();
        Assert.NotNull(members);
        Assert.Equal(PlcDataType.Structure, members["Items"].DataType);
    }

    // =======================================================================
    // Decoder: atomic member with zero-length data -> empty array fallback
    // =======================================================================

    [Fact]
    public void Decoder_AtomicMember_EmptyData_FallbackWithEmptyArray()
    {
        // Create a UDT where the member offset is 0 and we provide exactly
        // 1 byte of data, but the member type needs more
        var db = new TagDatabase();
        db.AddUdtDefinition(new UdtDefinition
        {
            Name = "EmptyDataUDT",
            ByteSize = 4,
            TemplateInstanceId = 0x110F,
            Members =
            [
                // Unknown type at offset 0 -> cipType == 0, data.Length > 0 returns raw bytes
                new UdtMember { Name = "Unk", DataType = PlcDataType.Unknown, TypeName = "???", Offset = 0, Size = 1 },
            ],
        });

        var decoder = new StructureDecoder(db);
        var result = decoder.DecodeStructure(new byte[] { 0xAB }, db.GetUdtByTemplateId(0x110F)!);
        var members = result.AsStructure();
        Assert.NotNull(members);
        Assert.Equal(PlcDataType.Unknown, members["Unk"].DataType);
    }

    // =======================================================================
    // Encode/Decode: ULINT with edge values
    // =======================================================================

    [Fact]
    public void RoundTrip_Ulint_MaxValue()
    {
        var db = new TagDatabase();
        db.AddUdtDefinition(new UdtDefinition
        {
            Name = "UlintUDT",
            ByteSize = 8,
            TemplateInstanceId = 0x1200,
            Members =
            [
                new UdtMember { Name = "Val", DataType = PlcDataType.Ulint, TypeName = "ULINT", Offset = 0, Size = 8 },
            ],
        });

        var encoder = new StructureEncoder(db);
        var decoder = new StructureDecoder(db);

        var members = new Dictionary<string, object> { ["Val"] = ulong.MaxValue };
        var encoded = encoder.EncodeStructure(members, db.GetUdtByTemplateId(0x1200)!);

        Assert.Equal(8, encoded.Length);
        Assert.Equal(ulong.MaxValue, BinaryPrimitives.ReadUInt64LittleEndian(encoded));

        var decoded = decoder.DecodeStructure(encoded, db.GetUdtByTemplateId(0x1200)!);
        var result = decoded.AsStructure();
        Assert.NotNull(result);
        Assert.Equal(ulong.MaxValue, result["Val"].AsUInt64());
    }

    [Fact]
    public void RoundTrip_Ulint_Zero()
    {
        var db = new TagDatabase();
        db.AddUdtDefinition(new UdtDefinition
        {
            Name = "UlintUDT0",
            ByteSize = 8,
            TemplateInstanceId = 0x1201,
            Members =
            [
                new UdtMember { Name = "Val", DataType = PlcDataType.Ulint, TypeName = "ULINT", Offset = 0, Size = 8 },
            ],
        });

        var encoder = new StructureEncoder(db);
        var decoder = new StructureDecoder(db);

        var members = new Dictionary<string, object> { ["Val"] = 0UL };
        var encoded = encoder.EncodeStructure(members, db.GetUdtByTemplateId(0x1201)!);
        var decoded = decoder.DecodeStructure(encoded, db.GetUdtByTemplateId(0x1201)!);
        var result = decoded.AsStructure();
        Assert.Equal(0UL, result!["Val"].AsUInt64());
    }

    // =======================================================================
    // Encode/Decode: USINT with edge values
    // =======================================================================

    [Fact]
    public void RoundTrip_Usint_MaxValue()
    {
        var db = new TagDatabase();
        db.AddUdtDefinition(new UdtDefinition
        {
            Name = "UsintUDT",
            ByteSize = 1,
            TemplateInstanceId = 0x1202,
            Members =
            [
                new UdtMember { Name = "Val", DataType = PlcDataType.Usint, TypeName = "USINT", Offset = 0, Size = 1 },
            ],
        });

        var encoder = new StructureEncoder(db);
        var decoder = new StructureDecoder(db);

        var members = new Dictionary<string, object> { ["Val"] = byte.MaxValue };
        var encoded = encoder.EncodeStructure(members, db.GetUdtByTemplateId(0x1202)!);
        Assert.Single(encoded);
        Assert.Equal(255, encoded[0]);

        var decoded = decoder.DecodeStructure(encoded, db.GetUdtByTemplateId(0x1202)!);
        var result = decoded.AsStructure();
        Assert.Equal(byte.MaxValue, result!["Val"].AsByte());
    }

    // =======================================================================
    // Encode/Decode: UINT with edge values
    // =======================================================================

    [Fact]
    public void RoundTrip_Uint_MaxValue()
    {
        var db = new TagDatabase();
        db.AddUdtDefinition(new UdtDefinition
        {
            Name = "UintUDT",
            ByteSize = 2,
            TemplateInstanceId = 0x1203,
            Members =
            [
                new UdtMember { Name = "Val", DataType = PlcDataType.Uint, TypeName = "UINT", Offset = 0, Size = 2 },
            ],
        });

        var encoder = new StructureEncoder(db);
        var decoder = new StructureDecoder(db);

        var members = new Dictionary<string, object> { ["Val"] = ushort.MaxValue };
        var encoded = encoder.EncodeStructure(members, db.GetUdtByTemplateId(0x1203)!);
        Assert.Equal(2, encoded.Length);
        Assert.Equal(ushort.MaxValue, BinaryPrimitives.ReadUInt16LittleEndian(encoded));

        var decoded = decoder.DecodeStructure(encoded, db.GetUdtByTemplateId(0x1203)!);
        var result = decoded.AsStructure();
        Assert.Equal(ushort.MaxValue, result!["Val"].AsUInt16());
    }

    // =======================================================================
    // Encode/Decode: UDINT with edge values
    // =======================================================================

    [Fact]
    public void RoundTrip_Udint_MaxValue()
    {
        var db = new TagDatabase();
        db.AddUdtDefinition(new UdtDefinition
        {
            Name = "UdintUDT",
            ByteSize = 4,
            TemplateInstanceId = 0x1204,
            Members =
            [
                new UdtMember { Name = "Val", DataType = PlcDataType.Udint, TypeName = "UDINT", Offset = 0, Size = 4 },
            ],
        });

        var encoder = new StructureEncoder(db);
        var decoder = new StructureDecoder(db);

        var members = new Dictionary<string, object> { ["Val"] = uint.MaxValue };
        var encoded = encoder.EncodeStructure(members, db.GetUdtByTemplateId(0x1204)!);
        Assert.Equal(4, encoded.Length);
        Assert.Equal(uint.MaxValue, BinaryPrimitives.ReadUInt32LittleEndian(encoded));

        var decoded = decoder.DecodeStructure(encoded, db.GetUdtByTemplateId(0x1204)!);
        var result = decoded.AsStructure();
        Assert.Equal(uint.MaxValue, result!["Val"].AsUInt32());
    }

    // =======================================================================
    // Decoder: USINT with 0 bytes (short data) -> fallback
    // =======================================================================

    [Fact]
    public void Decoder_Usint_ShortData_ReturnsFallback()
    {
        var db = new TagDatabase();
        db.AddUdtDefinition(new UdtDefinition
        {
            Name = "UsintShortUDT",
            ByteSize = 2,
            TemplateInstanceId = 0x1205,
            Members =
            [
                // Put member at offset 1, provide only 1 byte of data -> memberData is empty
                new UdtMember { Name = "Val", DataType = PlcDataType.Usint, TypeName = "USINT", Offset = 1, Size = 1 },
            ],
        });

        var decoder = new StructureDecoder(db);
        // Only 1 byte, member at offset 1 -> memberData length is 0 -> atomicSize(1) > 0 -> fallback
        var result = decoder.DecodeStructure(new byte[] { 0xFF }, db.GetUdtByTemplateId(0x1205)!);
        var members = result.AsStructure();
        // offset 1 >= data.Length 1, so member skipped
        Assert.Empty(members);
    }

    // =======================================================================
    // Decoder: Nested STRING structure with data shorter than nestedUdt.ByteSize
    // =======================================================================

    [Fact]
    public void Decoder_NestedStringStructure_ShorterData()
    {
        var db = CreateDatabaseWithUdtContainingString(); // DeviceInfo @ 0x0300, STRING @ 0x0CE8 (88 bytes)
        var decoder = new StructureDecoder(db);

        // DeviceInfo expects 92 bytes, provide only 20 (partial STRING data)
        var data = new byte[20];
        BinaryPrimitives.WriteInt32LittleEndian(data, 7); // ID = 7
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(4), 2); // STRING LEN = 2
        data[8] = (byte)'O'; data[9] = (byte)'K';

        var result = decoder.DecodeStructure(data, db.GetUdtByTemplateId(0x0300)!);
        var members = result.AsStructure();
        Assert.NotNull(members);
        Assert.Equal(7, members["ID"].AsInt32());
        // STRING decoded with shorter data
        Assert.Equal("OK", members["LocationName"].AsString());
    }

    // =======================================================================
    // Decoder: Array member with unknown type -> fallback
    // =======================================================================

    [Fact]
    public void Decoder_ArrayMember_UnknownType_ReturnsFallback()
    {
        var db = new TagDatabase();
        db.AddUdtDefinition(new UdtDefinition
        {
            Name = "UnknownArrayUDT",
            ByteSize = 8,
            TemplateInstanceId = 0x1206,
            Members =
            [
                new UdtMember { Name = "Arr", DataType = PlcDataType.Unknown, TypeName = "???", Offset = 0, Size = 4, Dimensions = [2] },
            ],
        });

        var decoder = new StructureDecoder(db);
        var result = decoder.DecodeStructure(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }, db.GetUdtByTemplateId(0x1206)!);
        var members = result.AsStructure();
        Assert.Equal(PlcDataType.Unknown, members["Arr"].DataType);
    }

    // =======================================================================
    // Encoder: member offset >= result.Length -> skipped
    // =======================================================================

    [Fact]
    public void Encoder_MemberOffsetOutOfRange_Skipped()
    {
        var db = new TagDatabase();
        db.AddUdtDefinition(new UdtDefinition
        {
            Name = "OffsetOOBUDT",
            ByteSize = 4,
            TemplateInstanceId = 0x1207,
            Members =
            [
                new UdtMember { Name = "OK", DataType = PlcDataType.Dint, TypeName = "DINT", Offset = 0, Size = 4 },
                new UdtMember { Name = "OOB", DataType = PlcDataType.Dint, TypeName = "DINT", Offset = 100, Size = 4 },
            ],
        });

        var encoder = new StructureEncoder(db);
        var members = new Dictionary<string, object>
        {
            ["OK"] = 42,
            ["OOB"] = 99,
        };

        var encoded = encoder.EncodeStructure(members, db.GetUdtByTemplateId(0x1207)!);
        Assert.Equal(4, encoded.Length);
        Assert.Equal(42, BinaryPrimitives.ReadInt32LittleEndian(encoded));
    }

    // =======================================================================
    // Encoder: structure array element exceeds target span
    // =======================================================================

    [Fact]
    public void Encoder_StructureArrayMember_ElementExceedsTarget()
    {
        var db = CreateDatabaseWithUdt(); // SimpleUDT @ 0x0100, 8 bytes
        db.AddUdtDefinition(new UdtDefinition
        {
            Name = "TightStructArrayUDT",
            ByteSize = 12, // Only room for Count + 1 element (not 2)
            TemplateInstanceId = 0x1208,
            Members =
            [
                new UdtMember { Name = "Count", DataType = PlcDataType.Dint, TypeName = "DINT", Offset = 0, Size = 4 },
                new UdtMember
                {
                    Name = "Items", DataType = PlcDataType.Structure, TypeName = "SimpleUDT",
                    Offset = 4, Size = 8, IsStructure = true, TemplateInstanceId = 0x0100,
                    Dimensions = [3], // claims 3 elements but only space for 1
                },
            ],
        });

        var encoder = new StructureEncoder(db);
        var list = new List<IReadOnlyDictionary<string, object>>
        {
            new Dictionary<string, object> { ["IntField"] = 10, ["FloatField"] = 1.0f },
            new Dictionary<string, object> { ["IntField"] = 20, ["FloatField"] = 2.0f },
            new Dictionary<string, object> { ["IntField"] = 30, ["FloatField"] = 3.0f },
        } as IReadOnlyList<IReadOnlyDictionary<string, object>>;

        var members = new Dictionary<string, object>
        {
            ["Count"] = 3,
            ["Items"] = list,
        };

        var encoded = encoder.EncodeStructure(members, db.GetUdtByTemplateId(0x1208)!);
        Assert.Equal(12, encoded.Length);
        Assert.Equal(3, BinaryPrimitives.ReadInt32LittleEndian(encoded));
        // Only first element fits (offset 0, size 8 within 8-byte target span)
        Assert.Equal(10, BinaryPrimitives.ReadInt32LittleEndian(encoded.AsSpan(4)));
    }

    // =======================================================================
    // Encoder: structure array via Array path, element exceeds target
    // =======================================================================

    [Fact]
    public void Encoder_StructureArrayMember_ViaArray_ElementExceedsTarget()
    {
        var db = CreateDatabaseWithUdt();
        db.AddUdtDefinition(new UdtDefinition
        {
            Name = "TightStructArrayUDT2",
            ByteSize = 12,
            TemplateInstanceId = 0x1209,
            Members =
            [
                new UdtMember { Name = "Count", DataType = PlcDataType.Dint, TypeName = "DINT", Offset = 0, Size = 4 },
                new UdtMember
                {
                    Name = "Items", DataType = PlcDataType.Structure, TypeName = "SimpleUDT",
                    Offset = 4, Size = 8, IsStructure = true, TemplateInstanceId = 0x0100,
                    Dimensions = [3],
                },
            ],
        });

        var encoder = new StructureEncoder(db);
        var elem0 = new Dictionary<string, object> { ["IntField"] = 10, ["FloatField"] = 1.0f } as IReadOnlyDictionary<string, object>;
        var elem1 = new Dictionary<string, object> { ["IntField"] = 20, ["FloatField"] = 2.0f } as IReadOnlyDictionary<string, object>;
        var arr = new object[] { elem0, elem1, "not a dict" }; // third element not a dict

        var members = new Dictionary<string, object>
        {
            ["Count"] = 3,
            ["Items"] = arr,
        };

        var encoded = encoder.EncodeStructure(members, db.GetUdtByTemplateId(0x1209)!);
        Assert.Equal(12, encoded.Length);
        // First element encoded OK
        Assert.Equal(10, BinaryPrimitives.ReadInt32LittleEndian(encoded.AsSpan(4)));
    }

    // =======================================================================
    // Decoder: Nested STRING member via IsStringUdt path (data shorter)
    // =======================================================================

    [Fact]
    public void Decoder_NestedStringUdt_DataShorterThanByteSize()
    {
        var db = CreateDatabaseWithStringUdt(); // STRING @ 0x0CE8, 88 bytes
        db.AddUdtDefinition(new UdtDefinition
        {
            Name = "ShortStringParent",
            ByteSize = 50, // less than STRING's 88
            TemplateInstanceId = 0x120A,
            Members =
            [
                new UdtMember
                {
                    Name = "Str", DataType = PlcDataType.Structure, TypeName = "STRING",
                    Offset = 0, Size = 88, IsStructure = true, TemplateInstanceId = 0x0CE8,
                },
            ],
        });

        var decoder = new StructureDecoder(db);
        var data = new byte[50];
        BinaryPrimitives.WriteInt32LittleEndian(data, 3);
        data[4] = (byte)'X'; data[5] = (byte)'Y'; data[6] = (byte)'Z';

        // data.Length (50) < nestedUdt.ByteSize (88), so it uses the shorter data
        var result = decoder.DecodeStructure(data, db.GetUdtByTemplateId(0x120A)!);
        var members = result.AsStructure();
        Assert.Equal("XYZ", members["Str"].AsString());
    }
}
