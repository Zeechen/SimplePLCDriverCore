using SimplePLCDriverCore.Abstractions;
using SimplePLCDriverCore.Protocols.EtherNetIP.Cip;

namespace SimplePLCDriverCore.Tests.TypeSystem;

public class TagDatabaseTests
{
    private static PlcTagInfo MakeTag(string name, ushort typeCode = CipDataTypes.Dint,
        bool isProgramScoped = false, string? programName = null)
    {
        return new PlcTagInfo
        {
            Name = name,
            DataType = CipTypeCodec.ToPlcDataType(typeCode),
            TypeName = CipDataTypes.GetTypeName(typeCode),
            RawTypeCode = typeCode,
            IsProgramScoped = isProgramScoped,
            ProgramName = programName,
        };
    }

    [Fact]
    public void LookupTag_ExactMatch()
    {
        var db = new TagDatabase();
        db.AddTag(MakeTag("MyDINT", CipDataTypes.Dint));

        var result = db.LookupTag("MyDINT");
        Assert.NotNull(result);
        Assert.Equal("DINT", result!.TypeName);
    }

    [Fact]
    public void LookupTag_CaseInsensitive()
    {
        var db = new TagDatabase();
        db.AddTag(MakeTag("MyTag"));

        Assert.NotNull(db.LookupTag("mytag"));
        Assert.NotNull(db.LookupTag("MYTAG"));
        Assert.NotNull(db.LookupTag("MyTag"));
    }

    [Fact]
    public void LookupTag_NotFound_ReturnsNull()
    {
        var db = new TagDatabase();
        Assert.Null(db.LookupTag("NonExistent"));
    }

    [Fact]
    public void LookupTag_ArrayAccess_FindsBaseTag()
    {
        var db = new TagDatabase();
        db.AddTag(MakeTag("MyArray", CipDataTypes.Dint));

        // Lookup with array index should find the base tag
        var result = db.LookupTag("MyArray[5]");
        Assert.NotNull(result);
        Assert.Equal("MyArray", result!.Name);
    }

    [Fact]
    public void LookupTag_MemberAccess_FindsBaseTag()
    {
        var db = new TagDatabase();
        db.AddTag(MakeTag("MyUDT", 0x8001));

        // Lookup with member access should find the base tag
        var result = db.LookupTag("MyUDT.Field1");
        Assert.NotNull(result);
        Assert.Equal("MyUDT", result!.Name);
    }

    [Fact]
    public void LookupTag_ComplexPath_FindsBaseTag()
    {
        var db = new TagDatabase();
        db.AddTag(MakeTag("MyUDT", 0x8001));

        var result = db.LookupTag("MyUDT[0].Field.SubField");
        Assert.NotNull(result);
        Assert.Equal("MyUDT", result!.Name);
    }

    [Fact]
    public void LookupTag_ProgramScopedTag()
    {
        var db = new TagDatabase();
        db.AddTag(MakeTag("Program:MainProgram.LocalTag", CipDataTypes.Real,
            isProgramScoped: true, programName: "MainProgram"));

        var result = db.LookupTag("Program:MainProgram.LocalTag");
        Assert.NotNull(result);
        Assert.Equal("REAL", result!.TypeName);
    }

    [Fact]
    public void AddTag_ControllerScoped_AppearsInControllerTags()
    {
        var db = new TagDatabase();
        db.AddTag(MakeTag("Tag1"));
        db.AddTag(MakeTag("Tag2"));

        Assert.Equal(2, db.ControllerTags.Count);
    }

    [Fact]
    public void AddTag_ProgramScoped_NotInControllerTags()
    {
        var db = new TagDatabase();
        db.AddTag(MakeTag("ControllerTag"));
        db.AddTag(MakeTag("Program:Main.LocalTag", isProgramScoped: true, programName: "Main"));

        Assert.Single(db.ControllerTags);
        Assert.Equal(2, db.AllTags.Count);
    }

    [Fact]
    public void GetProgramTags_ReturnsProgramSpecificTags()
    {
        var db = new TagDatabase();
        db.AddTag(MakeTag("Program:Main.Tag1", isProgramScoped: true, programName: "Main"));
        db.AddTag(MakeTag("Program:Main.Tag2", isProgramScoped: true, programName: "Main"));
        db.AddTag(MakeTag("Program:Other.Tag3", isProgramScoped: true, programName: "Other"));

        var mainTags = db.GetProgramTags("Main");
        Assert.Equal(2, mainTags.Count);

        var otherTags = db.GetProgramTags("Other");
        Assert.Single(otherTags);
    }

    [Fact]
    public void GetProgramTags_UnknownProgram_ReturnsEmpty()
    {
        var db = new TagDatabase();
        Assert.Empty(db.GetProgramTags("NonExistent"));
    }

    [Fact]
    public void AddProgram_AddsUnique()
    {
        var db = new TagDatabase();
        db.AddProgram("Main");
        db.AddProgram("Main"); // duplicate
        db.AddProgram("Other");

        Assert.Equal(2, db.Programs.Count);
    }

    [Fact]
    public void AddUdtDefinition_CanRetrieveByIdAndName()
    {
        var db = new TagDatabase();
        var udt = new UdtDefinition
        {
            Name = "MyUDT",
            TemplateInstanceId = 0x0FCE,
            ByteSize = 64,
            Members = new[]
            {
                new UdtMember { Name = "Field1", TypeName = "DINT", Offset = 0, Size = 4 },
                new UdtMember { Name = "Field2", TypeName = "REAL", Offset = 4, Size = 4 },
            }
        };
        db.AddUdtDefinition(udt);

        Assert.NotNull(db.GetUdtByTemplateId(0x0FCE));
        Assert.NotNull(db.GetUdtByName("MyUDT"));
        Assert.Equal("MyUDT", db.GetUdtByName("myudt")!.Name); // case insensitive
    }

    [Fact]
    public void GetUdtByTemplateId_NotFound_ReturnsNull()
    {
        var db = new TagDatabase();
        Assert.Null(db.GetUdtByTemplateId(0x1234));
    }

    [Fact]
    public void Clear_RemovesEverything()
    {
        var db = new TagDatabase();
        db.AddTag(MakeTag("Tag1"));
        db.AddProgram("Main");
        db.AddUdtDefinition(new UdtDefinition
        {
            Name = "UDT", TemplateInstanceId = 1, ByteSize = 4,
            Members = Array.Empty<UdtMember>()
        });

        Assert.True(db.IsPopulated);

        db.Clear();

        Assert.False(db.IsPopulated);
        Assert.Empty(db.AllTags);
        Assert.Empty(db.Programs);
        Assert.Empty(db.UdtDefinitions);
    }

    [Fact]
    public void IsPopulated_FalseWhenEmpty()
    {
        var db = new TagDatabase();
        Assert.False(db.IsPopulated);
    }

    [Fact]
    public void IsPopulated_TrueAfterAddTag()
    {
        var db = new TagDatabase();
        db.AddTag(MakeTag("Tag1"));
        Assert.True(db.IsPopulated);
    }

    // --- ResolveMemberTypeCode Tests ---

    private static TagDatabase CreateDbWithUdtTag()
    {
        var db = new TagDatabase();

        // Add a UDT definition
        db.AddUdtDefinition(new UdtDefinition
        {
            Name = "MyUDT",
            TemplateInstanceId = 0x0100,
            ByteSize = 12,
            Members =
            [
                new UdtMember { Name = "IntField", DataType = PlcDataType.Dint, TypeName = "DINT", Offset = 0, Size = 4 },
                new UdtMember { Name = "FloatField", DataType = PlcDataType.Real, TypeName = "REAL", Offset = 4, Size = 4 },
                new UdtMember { Name = "BoolField", DataType = PlcDataType.Bool, TypeName = "BOOL", Offset = 8, Size = 1, BitOffset = 0 },
            ]
        });

        // Add a tag that uses this UDT
        db.AddTag(new PlcTagInfo
        {
            Name = "MyUDT",
            DataType = PlcDataType.Structure,
            TypeName = "MyUDT",
            RawTypeCode = 0x8100, // structure bit | template 0x0100
            IsStructure = true,
            TemplateInstanceId = 0x0100,
        });

        return db;
    }

    [Fact]
    public void ResolveMemberTypeCode_DirectTag_ReturnsTagTypeCode()
    {
        var db = new TagDatabase();
        db.AddTag(MakeTag("MyDINT", CipDataTypes.Dint));

        Assert.Equal(CipDataTypes.Dint, db.ResolveMemberTypeCode("MyDINT"));
    }

    [Fact]
    public void ResolveMemberTypeCode_DintMember_ReturnsDint()
    {
        var db = CreateDbWithUdtTag();
        Assert.Equal(CipDataTypes.Dint, db.ResolveMemberTypeCode("MyUDT.IntField"));
    }

    [Fact]
    public void ResolveMemberTypeCode_RealMember_ReturnsReal()
    {
        var db = CreateDbWithUdtTag();
        Assert.Equal(CipDataTypes.Real, db.ResolveMemberTypeCode("MyUDT.FloatField"));
    }

    [Fact]
    public void ResolveMemberTypeCode_BoolMember_ReturnsBool()
    {
        var db = CreateDbWithUdtTag();
        Assert.Equal(CipDataTypes.Bool, db.ResolveMemberTypeCode("MyUDT.BoolField"));
    }

    [Fact]
    public void ResolveMemberTypeCode_NonExistentMember_ReturnsZero()
    {
        var db = CreateDbWithUdtTag();
        Assert.Equal((ushort)0, db.ResolveMemberTypeCode("MyUDT.NonExistent"));
    }

    [Fact]
    public void ResolveMemberTypeCode_NonExistentTag_ReturnsZero()
    {
        var db = new TagDatabase();
        Assert.Equal((ushort)0, db.ResolveMemberTypeCode("Unknown.Field"));
    }

    [Fact]
    public void ResolveMemberTypeCode_NestedStructMember()
    {
        var db = new TagDatabase();

        // Inner UDT
        db.AddUdtDefinition(new UdtDefinition
        {
            Name = "InnerUDT",
            TemplateInstanceId = 0x0200,
            ByteSize = 4,
            Members = [new UdtMember { Name = "Value", DataType = PlcDataType.Dint, TypeName = "DINT", Offset = 0, Size = 4 }]
        });

        // Outer UDT with nested struct
        db.AddUdtDefinition(new UdtDefinition
        {
            Name = "OuterUDT",
            TemplateInstanceId = 0x0300,
            ByteSize = 8,
            Members =
            [
                new UdtMember { Name = "Nested", DataType = PlcDataType.Structure, TypeName = "InnerUDT", Offset = 0, Size = 4, IsStructure = true, TemplateInstanceId = 0x0200 },
                new UdtMember { Name = "Count", DataType = PlcDataType.Dint, TypeName = "DINT", Offset = 4, Size = 4 },
            ]
        });

        db.AddTag(new PlcTagInfo
        {
            Name = "OuterTag",
            DataType = PlcDataType.Structure,
            TypeName = "OuterUDT",
            RawTypeCode = 0x8300,
            IsStructure = true,
            TemplateInstanceId = 0x0300,
        });

        // Resolve nested member
        Assert.Equal(CipDataTypes.Dint, db.ResolveMemberTypeCode("OuterTag.Nested.Value"));
        Assert.Equal(CipDataTypes.Dint, db.ResolveMemberTypeCode("OuterTag.Count"));
    }

    // --- Batch Dot Notation Tests ---
    // These verify that multiple UDT member paths from different tags
    // can all be resolved independently, which is how batch operations work.

    private static TagDatabase CreateDbWithMultipleUdtTags()
    {
        var db = new TagDatabase();

        db.AddUdtDefinition(new UdtDefinition
        {
            Name = "MotorUDT",
            TemplateInstanceId = 0x0400,
            ByteSize = 12,
            Members =
            [
                new UdtMember { Name = "Speed", DataType = PlcDataType.Dint, TypeName = "DINT", Offset = 0, Size = 4 },
                new UdtMember { Name = "Current", DataType = PlcDataType.Real, TypeName = "REAL", Offset = 4, Size = 4 },
                new UdtMember { Name = "Enabled", DataType = PlcDataType.Bool, TypeName = "BOOL", Offset = 8, Size = 1, BitOffset = 0 },
            ]
        });

        db.AddUdtDefinition(new UdtDefinition
        {
            Name = "ReactorUDT",
            TemplateInstanceId = 0x0500,
            ByteSize = 8,
            Members =
            [
                new UdtMember { Name = "Setpoint", DataType = PlcDataType.Real, TypeName = "REAL", Offset = 0, Size = 4 },
                new UdtMember { Name = "Temperature", DataType = PlcDataType.Real, TypeName = "REAL", Offset = 4, Size = 4 },
            ]
        });

        // Two instances of MotorUDT
        db.AddTag(new PlcTagInfo
        {
            Name = "Motor1",
            DataType = PlcDataType.Structure,
            TypeName = "MotorUDT",
            RawTypeCode = 0x8400,
            IsStructure = true,
            TemplateInstanceId = 0x0400,
        });
        db.AddTag(new PlcTagInfo
        {
            Name = "Motor2",
            DataType = PlcDataType.Structure,
            TypeName = "MotorUDT",
            RawTypeCode = 0x8400,
            IsStructure = true,
            TemplateInstanceId = 0x0400,
        });

        // One instance of ReactorUDT
        db.AddTag(new PlcTagInfo
        {
            Name = "Reactor",
            DataType = PlcDataType.Structure,
            TypeName = "ReactorUDT",
            RawTypeCode = 0x8500,
            IsStructure = true,
            TemplateInstanceId = 0x0500,
        });

        // A scalar tag
        db.AddTag(MakeTag("Temperature", CipDataTypes.Real));

        return db;
    }

    [Fact]
    public void BatchDotNotation_LookupMultipleMemberPaths()
    {
        var db = CreateDbWithMultipleUdtTags();

        // Simulate what batch operations do: look up multiple dot-notation paths
        var paths = new[] { "Motor1.Speed", "Motor1.Current", "Motor2.Speed", "Reactor.Setpoint", "Temperature" };

        foreach (var path in paths)
        {
            var result = db.LookupTag(path);
            Assert.NotNull(result);
        }
    }

    [Fact]
    public void BatchDotNotation_ResolveMemberTypes_MixedUdts()
    {
        var db = CreateDbWithMultipleUdtTags();

        // Motor1 members
        Assert.Equal(CipDataTypes.Dint, db.ResolveMemberTypeCode("Motor1.Speed"));
        Assert.Equal(CipDataTypes.Real, db.ResolveMemberTypeCode("Motor1.Current"));
        Assert.Equal(CipDataTypes.Bool, db.ResolveMemberTypeCode("Motor1.Enabled"));

        // Motor2 members (same UDT type, different instance)
        Assert.Equal(CipDataTypes.Dint, db.ResolveMemberTypeCode("Motor2.Speed"));
        Assert.Equal(CipDataTypes.Real, db.ResolveMemberTypeCode("Motor2.Current"));

        // Reactor members (different UDT type)
        Assert.Equal(CipDataTypes.Real, db.ResolveMemberTypeCode("Reactor.Setpoint"));
        Assert.Equal(CipDataTypes.Real, db.ResolveMemberTypeCode("Reactor.Temperature"));

        // Scalar tag (not a member path)
        Assert.Equal(CipDataTypes.Real, db.ResolveMemberTypeCode("Temperature"));
    }

    [Fact]
    public void BatchDotNotation_MemberLookup_ReturnsBaseTagInfo()
    {
        var db = CreateDbWithMultipleUdtTags();

        // Dot notation lookup should return the base tag (not a member-specific tag)
        var motor1Speed = db.LookupTag("Motor1.Speed");
        Assert.NotNull(motor1Speed);
        Assert.Equal("Motor1", motor1Speed!.Name);
        Assert.True(motor1Speed.IsStructure);

        var motor2Current = db.LookupTag("Motor2.Current");
        Assert.NotNull(motor2Current);
        Assert.Equal("Motor2", motor2Current!.Name);

        var reactorSetpoint = db.LookupTag("Reactor.Setpoint");
        Assert.NotNull(reactorSetpoint);
        Assert.Equal("Reactor", reactorSetpoint!.Name);
    }

    [Fact]
    public void BatchDotNotation_ScalarAndUdtMembers_CanCoexist()
    {
        var db = CreateDbWithMultipleUdtTags();

        // "Temperature" is a scalar tag, "Reactor.Temperature" is a UDT member.
        // Both should resolve correctly.
        var scalarTag = db.LookupTag("Temperature");
        Assert.NotNull(scalarTag);
        Assert.Equal("Temperature", scalarTag!.Name);
        Assert.Equal(CipDataTypes.Real, scalarTag.RawTypeCode);
        Assert.False(scalarTag.IsStructure);

        var udtMember = db.LookupTag("Reactor.Temperature");
        Assert.NotNull(udtMember);
        Assert.Equal("Reactor", udtMember!.Name);
        Assert.True(udtMember.IsStructure);

        // Type resolution should differ: scalar returns REAL directly, member resolves through UDT
        Assert.Equal(CipDataTypes.Real, db.ResolveMemberTypeCode("Temperature"));
        Assert.Equal(CipDataTypes.Real, db.ResolveMemberTypeCode("Reactor.Temperature"));
    }
}
