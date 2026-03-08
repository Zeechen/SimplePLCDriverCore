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

    // =====================================================================
    // IsStringUdt branch coverage
    // =====================================================================

    [Fact]
    public void IsStringUdt_NameIsSTRING_ReturnsTrue()
    {
        var udt = new UdtDefinition
        {
            Name = "STRING",
            TemplateInstanceId = 1,
            ByteSize = 88,
            Members = [new UdtMember { Name = "Irrelevant", TypeName = "DINT", Offset = 0, Size = 4 }]
        };
        Assert.True(TagDatabase.IsStringUdt(udt));
    }

    [Fact]
    public void IsStringUdt_NameIsStringCaseInsensitive_ReturnsTrue()
    {
        var udt = new UdtDefinition
        {
            Name = "string",
            TemplateInstanceId = 1,
            ByteSize = 88,
            Members = []
        };
        Assert.True(TagDatabase.IsStringUdt(udt));
    }

    [Fact]
    public void IsStringUdt_TwoMembers_LenAndData_ReturnsTrue()
    {
        var udt = new UdtDefinition
        {
            Name = "MyCustomString",
            TemplateInstanceId = 2,
            ByteSize = 86,
            Members =
            [
                new UdtMember { Name = "LEN", DataType = PlcDataType.Dint, TypeName = "DINT", Offset = 0, Size = 4 },
                new UdtMember { Name = "DATA", DataType = PlcDataType.Sint, TypeName = "SINT", Offset = 4, Size = 82, Dimensions = [82] },
            ]
        };
        Assert.True(TagDatabase.IsStringUdt(udt));
    }

    [Fact]
    public void IsStringUdt_TwoMembers_LenAndDataCaseInsensitive_ReturnsTrue()
    {
        var udt = new UdtDefinition
        {
            Name = "CustomStr",
            TemplateInstanceId = 3,
            ByteSize = 86,
            Members =
            [
                new UdtMember { Name = "len", DataType = PlcDataType.Dint, TypeName = "DINT", Offset = 0, Size = 4 },
                new UdtMember { Name = "data", DataType = PlcDataType.Sint, TypeName = "SINT", Offset = 4, Size = 82, Dimensions = [82] },
            ]
        };
        Assert.True(TagDatabase.IsStringUdt(udt));
    }

    [Fact]
    public void IsStringUdt_TwoMembers_LenWrongType_ReturnsFalse()
    {
        var udt = new UdtDefinition
        {
            Name = "NotString",
            TemplateInstanceId = 4,
            ByteSize = 86,
            Members =
            [
                new UdtMember { Name = "LEN", DataType = PlcDataType.Int, TypeName = "INT", Offset = 0, Size = 2 },
                new UdtMember { Name = "DATA", DataType = PlcDataType.Sint, TypeName = "SINT", Offset = 2, Size = 82, Dimensions = [82] },
            ]
        };
        Assert.False(TagDatabase.IsStringUdt(udt));
    }

    [Fact]
    public void IsStringUdt_TwoMembers_DataWrongType_ReturnsFalse()
    {
        var udt = new UdtDefinition
        {
            Name = "NotString",
            TemplateInstanceId = 5,
            ByteSize = 86,
            Members =
            [
                new UdtMember { Name = "LEN", DataType = PlcDataType.Dint, TypeName = "DINT", Offset = 0, Size = 4 },
                new UdtMember { Name = "DATA", DataType = PlcDataType.Dint, TypeName = "DINT", Offset = 4, Size = 82, Dimensions = [82] },
            ]
        };
        Assert.False(TagDatabase.IsStringUdt(udt));
    }

    [Fact]
    public void IsStringUdt_TwoMembers_DataNoDimensions_ReturnsFalse()
    {
        var udt = new UdtDefinition
        {
            Name = "NotString",
            TemplateInstanceId = 6,
            ByteSize = 8,
            Members =
            [
                new UdtMember { Name = "LEN", DataType = PlcDataType.Dint, TypeName = "DINT", Offset = 0, Size = 4 },
                new UdtMember { Name = "DATA", DataType = PlcDataType.Sint, TypeName = "SINT", Offset = 4, Size = 1 },
            ]
        };
        Assert.False(TagDatabase.IsStringUdt(udt));
    }

    [Fact]
    public void IsStringUdt_TwoMembers_WrongMemberNames_ReturnsFalse()
    {
        var udt = new UdtDefinition
        {
            Name = "NotString",
            TemplateInstanceId = 7,
            ByteSize = 8,
            Members =
            [
                new UdtMember { Name = "Field1", DataType = PlcDataType.Dint, TypeName = "DINT", Offset = 0, Size = 4 },
                new UdtMember { Name = "Field2", DataType = PlcDataType.Sint, TypeName = "SINT", Offset = 4, Size = 1, Dimensions = [1] },
            ]
        };
        Assert.False(TagDatabase.IsStringUdt(udt));
    }

    [Fact]
    public void IsStringUdt_OneMembers_ReturnsFalse()
    {
        var udt = new UdtDefinition
        {
            Name = "SingleField",
            TemplateInstanceId = 8,
            ByteSize = 4,
            Members = [new UdtMember { Name = "Value", DataType = PlcDataType.Dint, TypeName = "DINT", Offset = 0, Size = 4 }]
        };
        Assert.False(TagDatabase.IsStringUdt(udt));
    }

    [Fact]
    public void IsStringUdt_ThreeMembers_ReturnsFalse()
    {
        var udt = new UdtDefinition
        {
            Name = "ThreeFields",
            TemplateInstanceId = 9,
            ByteSize = 12,
            Members =
            [
                new UdtMember { Name = "LEN", DataType = PlcDataType.Dint, TypeName = "DINT", Offset = 0, Size = 4 },
                new UdtMember { Name = "DATA", DataType = PlcDataType.Sint, TypeName = "SINT", Offset = 4, Size = 82, Dimensions = [82] },
                new UdtMember { Name = "Extra", DataType = PlcDataType.Dint, TypeName = "DINT", Offset = 86, Size = 4 },
            ]
        };
        Assert.False(TagDatabase.IsStringUdt(udt));
    }

    [Fact]
    public void IsStringUdt_ZeroMembers_ReturnsFalse()
    {
        var udt = new UdtDefinition
        {
            Name = "Empty",
            TemplateInstanceId = 10,
            ByteSize = 0,
            Members = []
        };
        Assert.False(TagDatabase.IsStringUdt(udt));
    }

    // =====================================================================
    // IsStringTemplate branch coverage
    // =====================================================================

    [Fact]
    public void IsStringTemplate_ValidStringTemplate_ReturnsTrue()
    {
        var db = new TagDatabase();
        db.AddUdtDefinition(new UdtDefinition
        {
            Name = "STRING",
            TemplateInstanceId = 0x0ABC,
            ByteSize = 88,
            Members = []
        });
        Assert.True(db.IsStringTemplate(0x0ABC));
    }

    [Fact]
    public void IsStringTemplate_NonStringTemplate_ReturnsFalse()
    {
        var db = new TagDatabase();
        db.AddUdtDefinition(new UdtDefinition
        {
            Name = "NotString",
            TemplateInstanceId = 0x0DEF,
            ByteSize = 12,
            Members =
            [
                new UdtMember { Name = "Field1", TypeName = "DINT", Offset = 0, Size = 4 },
            ]
        });
        Assert.False(db.IsStringTemplate(0x0DEF));
    }

    [Fact]
    public void IsStringTemplate_MissingTemplate_ReturnsFalse()
    {
        var db = new TagDatabase();
        Assert.False(db.IsStringTemplate(0x9999));
    }

    // =====================================================================
    // AddUdtDefinition duplicate handling
    // =====================================================================

    [Fact]
    public void AddUdtDefinition_Duplicate_OverwritesPrevious()
    {
        var db = new TagDatabase();
        var udt1 = new UdtDefinition
        {
            Name = "MyUDT",
            TemplateInstanceId = 0x0100,
            ByteSize = 4,
            Members = [new UdtMember { Name = "Old", TypeName = "DINT", Offset = 0, Size = 4 }]
        };
        var udt2 = new UdtDefinition
        {
            Name = "MyUDT",
            TemplateInstanceId = 0x0100,
            ByteSize = 8,
            Members = [new UdtMember { Name = "New", TypeName = "REAL", Offset = 0, Size = 4 }]
        };
        db.AddUdtDefinition(udt1);
        db.AddUdtDefinition(udt2);

        var result = db.GetUdtByTemplateId(0x0100);
        Assert.NotNull(result);
        Assert.Equal(8, result!.ByteSize);
        Assert.Equal("New", result.Members[0].Name);

        var byName = db.GetUdtByName("MyUDT");
        Assert.NotNull(byName);
        Assert.Equal(8, byName!.ByteSize);
    }

    // =====================================================================
    // GetUdtByName branch coverage
    // =====================================================================

    [Fact]
    public void GetUdtByName_NotFound_ReturnsNull()
    {
        var db = new TagDatabase();
        Assert.Null(db.GetUdtByName("DoesNotExist"));
    }

    // =====================================================================
    // LookupTag - baseName == tagName returns null
    // =====================================================================

    [Fact]
    public void LookupTag_SimpleNameNotInDb_ReturnsNull()
    {
        var db = new TagDatabase();
        db.AddTag(MakeTag("OtherTag"));
        // "MissingTag" has no dot or bracket, so baseName == tagName, second branch skipped
        Assert.Null(db.LookupTag("MissingTag"));
    }

    [Fact]
    public void LookupTag_DottedPath_BaseNotInDb_ReturnsNull()
    {
        var db = new TagDatabase();
        // "Unknown.Field" -> base "Unknown" not found
        Assert.Null(db.LookupTag("Unknown.Field"));
    }

    // =====================================================================
    // ResolveMemberTypeCode - all data type switch arms
    // =====================================================================

    private static TagDatabase CreateDbWithAllTypeMembers()
    {
        var db = new TagDatabase();
        db.AddUdtDefinition(new UdtDefinition
        {
            Name = "AllTypesUDT",
            TemplateInstanceId = 0x0700,
            ByteSize = 64,
            Members =
            [
                new UdtMember { Name = "BoolField", DataType = PlcDataType.Bool, TypeName = "BOOL", Offset = 0, Size = 1 },
                new UdtMember { Name = "SintField", DataType = PlcDataType.Sint, TypeName = "SINT", Offset = 1, Size = 1 },
                new UdtMember { Name = "IntField", DataType = PlcDataType.Int, TypeName = "INT", Offset = 2, Size = 2 },
                new UdtMember { Name = "DintField", DataType = PlcDataType.Dint, TypeName = "DINT", Offset = 4, Size = 4 },
                new UdtMember { Name = "LintField", DataType = PlcDataType.Lint, TypeName = "LINT", Offset = 8, Size = 8 },
                new UdtMember { Name = "UsintField", DataType = PlcDataType.Usint, TypeName = "USINT", Offset = 16, Size = 1 },
                new UdtMember { Name = "UintField", DataType = PlcDataType.Uint, TypeName = "UINT", Offset = 17, Size = 2 },
                new UdtMember { Name = "UdintField", DataType = PlcDataType.Udint, TypeName = "UDINT", Offset = 20, Size = 4 },
                new UdtMember { Name = "UlintField", DataType = PlcDataType.Ulint, TypeName = "ULINT", Offset = 24, Size = 8 },
                new UdtMember { Name = "RealField", DataType = PlcDataType.Real, TypeName = "REAL", Offset = 32, Size = 4 },
                new UdtMember { Name = "LrealField", DataType = PlcDataType.Lreal, TypeName = "LREAL", Offset = 36, Size = 8 },
                new UdtMember { Name = "StringField", DataType = PlcDataType.String, TypeName = "STRING", Offset = 44, Size = 88 },
                new UdtMember { Name = "UnknownField", DataType = PlcDataType.Unknown, TypeName = "UNKNOWN", Offset = 132, Size = 4 },
            ]
        });

        db.AddTag(new PlcTagInfo
        {
            Name = "AllTypes",
            DataType = PlcDataType.Structure,
            TypeName = "AllTypesUDT",
            RawTypeCode = 0x8700,
            IsStructure = true,
            TemplateInstanceId = 0x0700,
        });

        return db;
    }

    [Theory]
    [InlineData("AllTypes.BoolField", CipDataTypes.Bool)]
    [InlineData("AllTypes.SintField", CipDataTypes.Sint)]
    [InlineData("AllTypes.IntField", CipDataTypes.Int)]
    [InlineData("AllTypes.DintField", CipDataTypes.Dint)]
    [InlineData("AllTypes.LintField", CipDataTypes.Lint)]
    [InlineData("AllTypes.UsintField", CipDataTypes.Usint)]
    [InlineData("AllTypes.UintField", CipDataTypes.Uint)]
    [InlineData("AllTypes.UdintField", CipDataTypes.Udint)]
    [InlineData("AllTypes.UlintField", CipDataTypes.Ulint)]
    [InlineData("AllTypes.RealField", CipDataTypes.Real)]
    [InlineData("AllTypes.LrealField", CipDataTypes.Lreal)]
    [InlineData("AllTypes.StringField", CipDataTypes.String)]
    public void ResolveMemberTypeCode_AllAtomicTypes(string path, ushort expectedCode)
    {
        var db = CreateDbWithAllTypeMembers();
        Assert.Equal(expectedCode, db.ResolveMemberTypeCode(path));
    }

    [Fact]
    public void ResolveMemberTypeCode_UnknownDataType_ReturnsZero()
    {
        var db = CreateDbWithAllTypeMembers();
        Assert.Equal((ushort)0, db.ResolveMemberTypeCode("AllTypes.UnknownField"));
    }

    [Fact]
    public void ResolveMemberTypeCode_StructureMember_NoRemaining_ReturnsStructCode()
    {
        var db = new TagDatabase();
        db.AddUdtDefinition(new UdtDefinition
        {
            Name = "InnerUDT",
            TemplateInstanceId = 0x0200,
            ByteSize = 4,
            Members = [new UdtMember { Name = "Value", DataType = PlcDataType.Dint, TypeName = "DINT", Offset = 0, Size = 4 }]
        });
        db.AddUdtDefinition(new UdtDefinition
        {
            Name = "OuterUDT",
            TemplateInstanceId = 0x0300,
            ByteSize = 8,
            Members =
            [
                new UdtMember { Name = "Nested", DataType = PlcDataType.Structure, TypeName = "InnerUDT", Offset = 0, Size = 4, IsStructure = true, TemplateInstanceId = 0x0200 },
            ]
        });
        db.AddTag(new PlcTagInfo
        {
            Name = "Outer",
            DataType = PlcDataType.Structure,
            TypeName = "OuterUDT",
            RawTypeCode = 0x8300,
            IsStructure = true,
            TemplateInstanceId = 0x0300,
        });

        // Resolving just "Outer.Nested" should return structure type code (0x8000 | 0x0200)
        Assert.Equal((ushort)(0x8000 | 0x0200), db.ResolveMemberTypeCode("Outer.Nested"));
    }

    // =====================================================================
    // ResolveMemberTypeCode - array index in path
    // =====================================================================

    [Fact]
    public void ResolveMemberTypeCode_ArrayIndexThenMember()
    {
        var db = CreateDbWithUdtTag();
        // "MyUDT[0].IntField" -> base "MyUDT", memberPath "[0].IntField"
        // Strips array index -> ".IntField" -> "IntField"
        Assert.Equal(CipDataTypes.Dint, db.ResolveMemberTypeCode("MyUDT[0].IntField"));
    }

    [Fact]
    public void ResolveMemberTypeCode_ArrayIndexMissingCloseBracket_ReturnsZero()
    {
        var db = CreateDbWithUdtTag();
        // Malformed: no closing bracket
        Assert.Equal((ushort)0, db.ResolveMemberTypeCode("MyUDT[0.IntField"));
    }

    [Fact]
    public void ResolveMemberTypeCode_ArrayIndexNoMember_NoDot_ReturnsZero()
    {
        var db = CreateDbWithUdtTag();
        // "MyUDT[0]" -> memberPath "[0]" -> after strip: "" -> no leading dot -> return 0
        // But wait, "MyUDT[0]" base is "MyUDT", memberPath is "[0]", after strip bracket: "",
        // which doesn't start with '.', so returns 0
        Assert.Equal((ushort)0, db.ResolveMemberTypeCode("MyUDT[0]NoField"));
    }

    // =====================================================================
    // ResolveMemberTypeCode - non-structure base tag
    // =====================================================================

    [Fact]
    public void ResolveMemberTypeCode_NonStructureTag_ReturnsZero()
    {
        var db = new TagDatabase();
        db.AddTag(MakeTag("ScalarTag", CipDataTypes.Dint));
        // "ScalarTag.Field" -> base "ScalarTag" found but not structure
        Assert.Equal((ushort)0, db.ResolveMemberTypeCode("ScalarTag.Field"));
    }

    [Fact]
    public void ResolveMemberTypeCode_StructureTagWithZeroTemplateId_ReturnsZero()
    {
        var db = new TagDatabase();
        db.AddTag(new PlcTagInfo
        {
            Name = "BadStruct",
            DataType = PlcDataType.Structure,
            TypeName = "Unknown",
            RawTypeCode = 0x8000,
            IsStructure = true,
            TemplateInstanceId = 0, // zero template ID
        });
        Assert.Equal((ushort)0, db.ResolveMemberTypeCode("BadStruct.Field"));
    }

    // =====================================================================
    // ResolveMemberTypeCode - UDT not found for template ID
    // =====================================================================

    [Fact]
    public void ResolveMemberTypeCode_MissingUdtDefinition_ReturnsZero()
    {
        var db = new TagDatabase();
        db.AddTag(new PlcTagInfo
        {
            Name = "OrphanStruct",
            DataType = PlcDataType.Structure,
            TypeName = "MissingUDT",
            RawTypeCode = 0x8999,
            IsStructure = true,
            TemplateInstanceId = 0x0999,
        });
        // UDT 0x0999 not added, so resolution fails
        Assert.Equal((ushort)0, db.ResolveMemberTypeCode("OrphanStruct.Field"));
    }

    // =====================================================================
    // ResolveMemberTypeCode - bracket access in nested member path
    // =====================================================================

    [Fact]
    public void ResolveMemberTypeCode_BracketInMemberPath_WithRemainingDot()
    {
        var db = new TagDatabase();
        db.AddUdtDefinition(new UdtDefinition
        {
            Name = "InnerUDT",
            TemplateInstanceId = 0x0200,
            ByteSize = 4,
            Members = [new UdtMember { Name = "Val", DataType = PlcDataType.Real, TypeName = "REAL", Offset = 0, Size = 4 }]
        });
        db.AddUdtDefinition(new UdtDefinition
        {
            Name = "OuterUDT",
            TemplateInstanceId = 0x0300,
            ByteSize = 8,
            Members =
            [
                new UdtMember { Name = "Items", DataType = PlcDataType.Structure, TypeName = "InnerUDT", Offset = 0, Size = 4, IsStructure = true, TemplateInstanceId = 0x0200 },
            ]
        });
        db.AddTag(new PlcTagInfo
        {
            Name = "Container",
            DataType = PlcDataType.Structure,
            TypeName = "OuterUDT",
            RawTypeCode = 0x8300,
            IsStructure = true,
            TemplateInstanceId = 0x0300,
        });

        // "Container.Items[0].Val" -> resolves member "Items", then bracket found in remaining
        // In ResolveMemberTypeCodeFromUdt: memberPath="Items[0].Val", bracketIndex=5, dotIndex=-1
        // currentMember="Items", remaining="Val"
        Assert.Equal(CipDataTypes.Real, db.ResolveMemberTypeCode("Container.Items[0].Val"));
    }

    [Fact]
    public void ResolveMemberTypeCode_BracketInMemberPath_NoRemainingDot()
    {
        var db = new TagDatabase();
        db.AddUdtDefinition(new UdtDefinition
        {
            Name = "ArrayUDT",
            TemplateInstanceId = 0x0400,
            ByteSize = 40,
            Members =
            [
                new UdtMember { Name = "Values", DataType = PlcDataType.Dint, TypeName = "DINT", Offset = 0, Size = 40, Dimensions = [10] },
            ]
        });
        db.AddTag(new PlcTagInfo
        {
            Name = "ArrTag",
            DataType = PlcDataType.Structure,
            TypeName = "ArrayUDT",
            RawTypeCode = 0x8400,
            IsStructure = true,
            TemplateInstanceId = 0x0400,
        });

        // "ArrTag.Values[3]" -> currentMember="Values", bracket but no dot after -> remaining stays null
        Assert.Equal(CipDataTypes.Dint, db.ResolveMemberTypeCode("ArrTag.Values[3]"));
    }

    // =====================================================================
    // ResolveMemberTypeCode - dot before bracket in nested member path
    // =====================================================================

    [Fact]
    public void ResolveMemberTypeCode_DotBeforeBracket_InNestedPath()
    {
        var db = new TagDatabase();
        db.AddUdtDefinition(new UdtDefinition
        {
            Name = "Deep",
            TemplateInstanceId = 0x0050,
            ByteSize = 4,
            Members = [new UdtMember { Name = "X", DataType = PlcDataType.Int, TypeName = "INT", Offset = 0, Size = 2 }]
        });
        db.AddUdtDefinition(new UdtDefinition
        {
            Name = "Mid",
            TemplateInstanceId = 0x0060,
            ByteSize = 8,
            Members = [new UdtMember { Name = "Inner", DataType = PlcDataType.Structure, TypeName = "Deep", Offset = 0, Size = 4, IsStructure = true, TemplateInstanceId = 0x0050 }]
        });
        db.AddUdtDefinition(new UdtDefinition
        {
            Name = "Top",
            TemplateInstanceId = 0x0070,
            ByteSize = 16,
            Members = [new UdtMember { Name = "Mid", DataType = PlcDataType.Structure, TypeName = "Mid", Offset = 0, Size = 8, IsStructure = true, TemplateInstanceId = 0x0060 }]
        });
        db.AddTag(new PlcTagInfo
        {
            Name = "TopTag",
            DataType = PlcDataType.Structure,
            TypeName = "Top",
            RawTypeCode = 0x8070,
            IsStructure = true,
            TemplateInstanceId = 0x0070,
        });

        // dot comes before any bracket: "Mid.Inner.X"
        Assert.Equal(CipDataTypes.Int, db.ResolveMemberTypeCode("TopTag.Mid.Inner.X"));
    }

    // =====================================================================
    // GetBaseTagName - Program: prefix branches
    // =====================================================================

    [Fact]
    public void LookupTag_ProgramPrefix_NoDotAfterProgram_ReturnsDirectMatch()
    {
        var db = new TagDatabase();
        // "Program:NoDot" - no dot after "Program:" -> returns tagName as-is
        db.AddTag(MakeTag("Program:NoDot", isProgramScoped: true, programName: "NoDot"));
        var result = db.LookupTag("Program:NoDot");
        Assert.NotNull(result);
    }

    [Fact]
    public void LookupTag_ProgramPrefix_WithMemberAccess()
    {
        var db = new TagDatabase();
        // "Program:Main.Tag.Field" -> base is "Program:Main.Tag", member is "Field"
        db.AddTag(MakeTag("Program:Main.Tag", isProgramScoped: true, programName: "Main"));

        var result = db.LookupTag("Program:Main.Tag.Field");
        Assert.NotNull(result);
        Assert.Equal("Program:Main.Tag", result!.Name);
    }

    [Fact]
    public void LookupTag_ProgramPrefix_WithArrayAccess()
    {
        var db = new TagDatabase();
        db.AddTag(MakeTag("Program:Main.Arr", isProgramScoped: true, programName: "Main"));

        var result = db.LookupTag("Program:Main.Arr[5]");
        Assert.NotNull(result);
        Assert.Equal("Program:Main.Arr", result!.Name);
    }

    [Fact]
    public void LookupTag_ProgramPrefix_BracketBeforeDot()
    {
        var db = new TagDatabase();
        db.AddTag(MakeTag("Program:Main.Arr", isProgramScoped: true, programName: "Main"));

        // "Program:Main.Arr[0].Field" -> base is "Program:Main.Arr"
        var result = db.LookupTag("Program:Main.Arr[0].Field");
        Assert.NotNull(result);
        Assert.Equal("Program:Main.Arr", result!.Name);
    }

    // =====================================================================
    // GetBaseTagName - no Program: prefix, bracket-only
    // =====================================================================

    [Fact]
    public void LookupTag_BracketOnly_NoBaseInDb_ReturnsNull()
    {
        var db = new TagDatabase();
        // "Missing[0]" -> base "Missing" not found
        Assert.Null(db.LookupTag("Missing[0]"));
    }

    // =====================================================================
    // AddTag - program scoped with null ProgramName (branch: isProgramScoped && ProgramName != null)
    // =====================================================================

    [Fact]
    public void AddTag_ProgramScoped_NullProgramName_DoesNotAddToProgramTags()
    {
        var db = new TagDatabase();
        // isProgramScoped = true but programName = null -> should NOT add to program tags
        db.AddTag(new PlcTagInfo
        {
            Name = "Orphan",
            DataType = PlcDataType.Dint,
            TypeName = "DINT",
            RawTypeCode = CipDataTypes.Dint,
            IsProgramScoped = true,
            ProgramName = null,
        });

        Assert.Single(db.AllTags);
        Assert.Empty(db.ControllerTags); // it's program-scoped
        Assert.Empty(db.GetProgramTags("Orphan"));
    }

    // =====================================================================
    // AddTag - second program-scoped tag to existing program
    // =====================================================================

    [Fact]
    public void AddTag_SecondProgramScopedTag_AppendsToExistingList()
    {
        var db = new TagDatabase();
        db.AddTag(MakeTag("Program:P1.T1", isProgramScoped: true, programName: "P1"));
        db.AddTag(MakeTag("Program:P1.T2", isProgramScoped: true, programName: "P1"));

        var tags = db.GetProgramTags("P1");
        Assert.Equal(2, tags.Count);
    }

    // =====================================================================
    // ResolveMemberTypeCode - nested struct with remaining but member is struct with templateId=0
    // =====================================================================

    [Fact]
    public void ResolveMemberTypeCode_NestedStructMember_ZeroTemplateId_ReturnsStructCode()
    {
        var db = new TagDatabase();
        db.AddUdtDefinition(new UdtDefinition
        {
            Name = "BrokenUDT",
            TemplateInstanceId = 0x0800,
            ByteSize = 8,
            Members =
            [
                new UdtMember { Name = "Sub", DataType = PlcDataType.Structure, TypeName = "Unknown", Offset = 0, Size = 4, IsStructure = true, TemplateInstanceId = 0 },
            ]
        });
        db.AddTag(new PlcTagInfo
        {
            Name = "BrokenTag",
            DataType = PlcDataType.Structure,
            TypeName = "BrokenUDT",
            RawTypeCode = 0x8800,
            IsStructure = true,
            TemplateInstanceId = 0x0800,
        });

        // "BrokenTag.Sub.Deep" -> member "Sub" is structure but templateId=0, so can't recurse
        // remaining != null but !(member.TemplateInstanceId > 0), so falls through to structure return
        Assert.Equal((ushort)(0x8000 | 0), db.ResolveMemberTypeCode("BrokenTag.Sub.Deep"));
    }

    // =====================================================================
    // AddProgram case insensitive duplicate
    // =====================================================================

    [Fact]
    public void AddProgram_CaseInsensitiveDuplicate_NotAdded()
    {
        var db = new TagDatabase();
        db.AddProgram("Main");
        db.AddProgram("main"); // should be treated as duplicate
        Assert.Single(db.Programs);
    }
}
