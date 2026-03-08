using SimplePLCDriverCore.Abstractions;

namespace SimplePLCDriverCore.Tests.Abstractions;

public class TagResultTests
{
    [Fact]
    public void Success_CreatesSuccessResult()
    {
        var result = TagResult.Success("MyTag", PlcTagValue.FromDInt(42), "DINT");

        Assert.True(result.IsSuccess);
        Assert.Equal("MyTag", result.TagName);
        Assert.Equal(42, result.Value.AsInt32());
        Assert.Equal("DINT", result.TypeName);
        Assert.Null(result.Error);
        Assert.Null(result.ErrorDetail);
    }

    [Fact]
    public void Failure_CreatesFailedResult()
    {
        var result = TagResult.Failure("BadTag", "Tag not found");

        Assert.False(result.IsSuccess);
        Assert.Equal("BadTag", result.TagName);
        Assert.Equal("Tag not found", result.Error);
        Assert.Null(result.ErrorDetail);
        Assert.True(result.Value.IsNull);
    }

    [Fact]
    public void Failure_WithErrorDetail_StoresBoth()
    {
        var result = TagResult.Failure("BadTag", "Tag not found in PLC",
            "CIP error: PathSegmentError (0x04) [extended: 0x0000]");

        Assert.False(result.IsSuccess);
        Assert.Equal("BadTag", result.TagName);
        Assert.Equal("Tag not found in PLC", result.Error);
        Assert.Equal("CIP error: PathSegmentError (0x04) [extended: 0x0000]", result.ErrorDetail);
    }

    [Fact]
    public void GetValueOrThrow_ReturnsValue_OnSuccess()
    {
        var result = TagResult.Success("Tag", PlcTagValue.FromReal(3.14f), "REAL");
        var value = result.GetValueOrThrow();
        Assert.Equal(3.14f, value.AsSingle());
    }

    [Fact]
    public void GetValueOrThrow_Throws_OnFailure()
    {
        var result = TagResult.Failure("Tag", "some error");
        var ex = Assert.Throws<PlcOperationException>(() => result.GetValueOrThrow());
        Assert.Equal("Tag", ex.TagName);
        Assert.Contains("some error", ex.Message);
        Assert.Null(ex.ErrorDetail);
    }

    [Fact]
    public void GetValueOrThrow_Throws_WithErrorDetail()
    {
        var result = TagResult.Failure("Tag", "Tag not found in PLC", "CIP error: PathSegmentError (0x04)");
        var ex = Assert.Throws<PlcOperationException>(() => result.GetValueOrThrow());
        Assert.Equal("Tag", ex.TagName);
        Assert.Contains("Tag not found in PLC", ex.Message);
        Assert.Equal("CIP error: PathSegmentError (0x04)", ex.ErrorDetail);
    }

    [Fact]
    public void ToString_Success_ShowsValue()
    {
        var result = TagResult.Success("MyDINT", PlcTagValue.FromDInt(42), "DINT");
        var str = result.ToString();
        Assert.Contains("MyDINT", str);
        Assert.Contains("42", str);
        Assert.Contains("DINT", str);
    }

    [Fact]
    public void ToString_Failure_ShowsError()
    {
        var result = TagResult.Failure("BadTag", "Path error");
        var str = result.ToString();
        Assert.Contains("BadTag", str);
        Assert.Contains("ERROR", str);
        Assert.Contains("Path error", str);
    }

    [Fact]
    public void ToString_Failure_WithDetail_ShowsBoth()
    {
        var result = TagResult.Failure("BadTag", "Tag not found in PLC", "CIP error: 0x04");
        var str = result.ToString();
        Assert.Contains("BadTag", str);
        Assert.Contains("Tag not found in PLC", str);
        Assert.Contains("CIP error: 0x04", str);
    }

    [Fact]
    public void GenericTagResult_Failure_WithErrorDetail()
    {
        var result = TagResult<string>.Failure("Tag", "Tag not found in PLC", "CIP error: 0x04");

        Assert.False(result.IsSuccess);
        Assert.Equal("Tag not found in PLC", result.Error);
        Assert.Equal("CIP error: 0x04", result.ErrorDetail);
    }

    [Fact]
    public void GenericTagResult_Success_HasNullErrorDetail()
    {
        var result = TagResult<string>.Success("Tag", "hello", "STRING");

        Assert.True(result.IsSuccess);
        Assert.Null(result.Error);
        Assert.Null(result.ErrorDetail);
    }

    // --- TagResult<T> additional coverage ---

    [Fact]
    public void GenericTagResult_Success_ReturnsValue()
    {
        var result = TagResult<int>.Success("MyTag", 42, "DINT");

        Assert.True(result.IsSuccess);
        Assert.Equal("MyTag", result.TagName);
        Assert.Equal(42, result.Value);
        Assert.Equal("DINT", result.TypeName);
    }

    [Fact]
    public void GenericTagResult_Failure_HasDefaultValue()
    {
        var result = TagResult<int>.Failure("Tag", "some error");

        Assert.False(result.IsSuccess);
        Assert.Equal(default, result.Value);
        Assert.Equal("some error", result.Error);
    }

    [Fact]
    public void GenericTagResult_GetValueOrThrow_ReturnsValue_OnSuccess()
    {
        var result = TagResult<string>.Success("Tag", "hello", "STRING");
        Assert.Equal("hello", result.GetValueOrThrow());
    }

    [Fact]
    public void GenericTagResult_GetValueOrThrow_Throws_OnFailure()
    {
        var result = TagResult<int>.Failure("BadTag", "not found");
        var ex = Assert.Throws<PlcOperationException>(() => result.GetValueOrThrow());
        Assert.Equal("BadTag", ex.TagName);
        Assert.Contains("not found", ex.Message);
    }

    [Fact]
    public void GenericTagResult_GetValueOrThrow_Throws_WithErrorDetail()
    {
        var result = TagResult<int>.Failure("BadTag", "not found", "CIP 0x04");
        var ex = Assert.Throws<PlcOperationException>(() => result.GetValueOrThrow());
        Assert.Equal("CIP 0x04", ex.ErrorDetail);
    }

    [Fact]
    public void GenericTagResult_ToString_Success()
    {
        var result = TagResult<int>.Success("MyTag", 42, "DINT");
        var str = result.ToString();
        Assert.Contains("MyTag", str);
        Assert.Contains("42", str);
        Assert.Contains("DINT", str);
    }

    [Fact]
    public void GenericTagResult_ToString_Failure_WithoutDetail()
    {
        var result = TagResult<int>.Failure("BadTag", "error msg");
        var str = result.ToString();
        Assert.Contains("BadTag", str);
        Assert.Contains("ERROR", str);
        Assert.Contains("error msg", str);
    }

    [Fact]
    public void GenericTagResult_ToString_Failure_WithDetail()
    {
        var result = TagResult<int>.Failure("BadTag", "error msg", "CIP 0x04");
        var str = result.ToString();
        Assert.Contains("CIP 0x04", str);
    }

    // --- PlcOperationException ---

    [Fact]
    public void PlcOperationException_StoresTagNameAndDetail()
    {
        var ex = new PlcOperationException("MyTag", "something failed", "detail info");
        Assert.Equal("MyTag", ex.TagName);
        Assert.Equal("detail info", ex.ErrorDetail);
        Assert.Contains("MyTag", ex.Message);
        Assert.Contains("something failed", ex.Message);
    }

    [Fact]
    public void PlcOperationException_NullErrorDetail()
    {
        var ex = new PlcOperationException("Tag", "msg");
        Assert.Null(ex.ErrorDetail);
    }
}
