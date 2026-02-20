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
}
