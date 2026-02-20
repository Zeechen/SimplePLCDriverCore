namespace SimplePLCDriverCore.Abstractions;

/// <summary>
/// Core driver interface for PLC communication.
/// All tag types are auto-detected - no type specification needed.
/// </summary>
public interface IPlcDriver : IAsyncDisposable, IDisposable
{
    /// <summary>Whether the driver is currently connected to the PLC.</summary>
    bool IsConnected { get; }

    /// <summary>Open connection to the PLC.</summary>
    ValueTask ConnectAsync(CancellationToken ct = default);

    /// <summary>Close connection to the PLC.</summary>
    ValueTask DisconnectAsync(CancellationToken ct = default);

    /// <summary>Read a single tag - type auto-detected from PLC metadata.</summary>
    ValueTask<TagResult> ReadAsync(string tagName, CancellationToken ct = default);

    /// <summary>Write a single tag - type auto-detected from PLC metadata.</summary>
    ValueTask<TagResult> WriteAsync(string tagName, object value, CancellationToken ct = default);

    /// <summary>Read multiple tags in minimal network round-trips using batch operations.</summary>
    ValueTask<TagResult[]> ReadAsync(IEnumerable<string> tagNames, CancellationToken ct = default);

    /// <summary>Write multiple tags in minimal network round-trips using batch operations.</summary>
    ValueTask<TagResult[]> WriteAsync(
        IEnumerable<(string TagName, object Value)> tags,
        CancellationToken ct = default);

    /// <summary>Read a UDT tag and return its value as a JSON string.</summary>
    ValueTask<TagResult<string>> ReadJsonAsync(string tagName, CancellationToken ct = default);

    /// <summary>Read a UDT tag and deserialize into a strongly-typed object.</summary>
    ValueTask<TagResult<T>> ReadAsync<T>(string tagName, CancellationToken ct = default) where T : class, new();

    /// <summary>Write a UDT tag from a strongly-typed object. All properties are written.</summary>
    ValueTask<TagResult> WriteAsync<T>(string tagName, T value, CancellationToken ct = default) where T : class;

    /// <summary>
    /// Write a UDT tag from a JSON string. Only fields present in the JSON are updated;
    /// other fields retain their current PLC values (read-modify-write).
    /// </summary>
    ValueTask<TagResult> WriteJsonAsync(string tagName, string json, CancellationToken ct = default);
}
