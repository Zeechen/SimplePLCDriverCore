namespace SimplePLCDriverCore.Abstractions;

/// <summary>
/// Result of a tag read/write operation.
/// Uses Result pattern - no exceptions for individual tag failures in batch operations.
/// </summary>
public readonly struct TagResult
{
    public string TagName { get; }
    public PlcTagValue Value { get; }
    public string TypeName { get; }
    public bool IsSuccess { get; }

    /// <summary>User-friendly error message (e.g., "Tag not found in PLC").</summary>
    public string? Error { get; }

    /// <summary>Technical diagnostic detail (e.g., CIP status codes). Null when Error is a non-CIP message.</summary>
    public string? ErrorDetail { get; }

    private TagResult(string tagName, PlcTagValue value, string typeName,
        bool isSuccess, string? error, string? errorDetail)
    {
        TagName = tagName;
        Value = value;
        TypeName = typeName;
        IsSuccess = isSuccess;
        Error = error;
        ErrorDetail = errorDetail;
    }

    /// <summary>Create a successful tag result.</summary>
    public static TagResult Success(string tagName, PlcTagValue value, string typeName) =>
        new(tagName, value, typeName, true, null, null);

    /// <summary>Create a failed tag result with a user-friendly error message.</summary>
    public static TagResult Failure(string tagName, string error) =>
        new(tagName, PlcTagValue.Null, string.Empty, false, error, null);

    /// <summary>Create a failed tag result with a user-friendly error and technical detail.</summary>
    public static TagResult Failure(string tagName, string error, string? errorDetail) =>
        new(tagName, PlcTagValue.Null, string.Empty, false, error, errorDetail);

    /// <summary>Get value or throw if the operation failed.</summary>
    public PlcTagValue GetValueOrThrow() =>
        IsSuccess ? Value : throw new PlcOperationException(TagName, Error!, ErrorDetail);

    public override string ToString() =>
        IsSuccess
            ? $"{TagName} = {Value} ({TypeName})"
            : ErrorDetail != null
                ? $"{TagName} ERROR: {Error} [{ErrorDetail}]"
                : $"{TagName} ERROR: {Error}";
}

/// <summary>
/// Result of a tag operation that returns a strongly-typed value.
/// Used by ReadAsync&lt;T&gt; and ReadJsonAsync.
/// </summary>
public readonly struct TagResult<T>
{
    public string TagName { get; }
    public T? Value { get; }
    public string TypeName { get; }
    public bool IsSuccess { get; }

    /// <summary>User-friendly error message.</summary>
    public string? Error { get; }

    /// <summary>Technical diagnostic detail (e.g., CIP status codes). Null when Error is a non-CIP message.</summary>
    public string? ErrorDetail { get; }

    private TagResult(string tagName, T? value, string typeName,
        bool isSuccess, string? error, string? errorDetail)
    {
        TagName = tagName;
        Value = value;
        TypeName = typeName;
        IsSuccess = isSuccess;
        Error = error;
        ErrorDetail = errorDetail;
    }

    /// <summary>Create a successful tag result with a typed value.</summary>
    public static TagResult<T> Success(string tagName, T value, string typeName) =>
        new(tagName, value, typeName, true, null, null);

    /// <summary>Create a failed tag result.</summary>
    public static TagResult<T> Failure(string tagName, string error) =>
        new(tagName, default, string.Empty, false, error, null);

    /// <summary>Create a failed tag result with a user-friendly error and technical detail.</summary>
    public static TagResult<T> Failure(string tagName, string error, string? errorDetail) =>
        new(tagName, default, string.Empty, false, error, errorDetail);

    /// <summary>Get value or throw if the operation failed.</summary>
    public T GetValueOrThrow() =>
        IsSuccess ? Value! : throw new PlcOperationException(TagName, Error!, ErrorDetail);

    public override string ToString() =>
        IsSuccess
            ? $"{TagName} = {Value} ({TypeName})"
            : ErrorDetail != null
                ? $"{TagName} ERROR: {Error} [{ErrorDetail}]"
                : $"{TagName} ERROR: {Error}";
}

/// <summary>
/// Exception thrown when a PLC tag operation fails and the caller uses GetValueOrThrow().
/// </summary>
public class PlcOperationException : Exception
{
    public string TagName { get; }

    /// <summary>Technical diagnostic detail (e.g., CIP status codes).</summary>
    public string? ErrorDetail { get; }

    public PlcOperationException(string tagName, string message, string? errorDetail = null)
        : base($"Tag '{tagName}': {message}")
    {
        TagName = tagName;
        ErrorDetail = errorDetail;
    }
}
