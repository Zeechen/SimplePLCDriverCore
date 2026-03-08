using SimplePLCDriverCore.Abstractions;

namespace SimplePLCDriverCore.Tests.Abstractions;

public class PlcTagInfoTests
{
    [Fact]
    public void ToString_Scalar_ShowsNameAndType()
    {
        var info = new PlcTagInfo
        {
            Name = "MyDINT",
            TypeName = "DINT",
            DataType = PlcDataType.Dint,
        };

        var str = info.ToString();
        Assert.Equal("MyDINT: DINT", str);
    }

    [Fact]
    public void ToString_WithDimensions_ShowsArrayBrackets()
    {
        var info = new PlcTagInfo
        {
            Name = "MyArray",
            TypeName = "DINT",
            DataType = PlcDataType.Dint,
            Dimensions = [10],
        };

        var str = info.ToString();
        Assert.Equal("MyArray: DINT[10]", str);
    }

    [Fact]
    public void ToString_MultiDimensional_ShowsAllDimensions()
    {
        var info = new PlcTagInfo
        {
            Name = "Matrix",
            TypeName = "REAL",
            DataType = PlcDataType.Real,
            Dimensions = [10, 5],
        };

        var str = info.ToString();
        Assert.Equal("Matrix: REAL[10,5]", str);
    }

    [Fact]
    public void ToString_ProgramScoped_ShowsScope()
    {
        var info = new PlcTagInfo
        {
            Name = "LocalTag",
            TypeName = "DINT",
            DataType = PlcDataType.Dint,
            IsProgramScoped = true,
            ProgramName = "MainProgram",
        };

        var str = info.ToString();
        Assert.Equal("LocalTag: DINT (Program:MainProgram)", str);
    }

    [Fact]
    public void ToString_ProgramScoped_WithDimensions()
    {
        var info = new PlcTagInfo
        {
            Name = "LocalArray",
            TypeName = "REAL",
            DataType = PlcDataType.Real,
            Dimensions = [5, 3],
            IsProgramScoped = true,
            ProgramName = "Sub1",
        };

        var str = info.ToString();
        Assert.Equal("LocalArray: REAL[5,3] (Program:Sub1)", str);
    }

    [Fact]
    public void Properties_SetCorrectly()
    {
        var info = new PlcTagInfo
        {
            Name = "TestTag",
            TypeName = "MyUDT",
            DataType = PlcDataType.Structure,
            IsStructure = true,
            InstanceId = 42,
            RawTypeCode = 0x8100,
            TemplateInstanceId = 0x0100,
        };

        Assert.Equal("TestTag", info.Name);
        Assert.Equal("MyUDT", info.TypeName);
        Assert.Equal(PlcDataType.Structure, info.DataType);
        Assert.True(info.IsStructure);
        Assert.Equal(42U, info.InstanceId);
        Assert.Equal((ushort)0x8100, info.RawTypeCode);
        Assert.Equal((ushort)0x0100, info.TemplateInstanceId);
    }

    [Fact]
    public void Dimensions_DefaultsToEmpty()
    {
        var info = new PlcTagInfo
        {
            Name = "Tag",
            TypeName = "DINT",
        };

        Assert.Empty(info.Dimensions);
    }
}
