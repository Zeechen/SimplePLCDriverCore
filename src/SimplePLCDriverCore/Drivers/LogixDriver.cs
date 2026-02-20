using System.Text.Json;
using SimplePLCDriverCore.Abstractions;
using SimplePLCDriverCore.Common;
using SimplePLCDriverCore.Protocols.EtherNetIP.Cip;
using SimplePLCDriverCore.TypeSystem;
using SimplePLCDriverCore.TypeSystem.Json;

namespace SimplePLCDriverCore.Drivers;

/// <summary>
/// High-level driver for Allen-Bradley ControlLogix and CompactLogix PLCs.
/// Implements typeless tag access: tag types are auto-detected from PLC metadata.
///
/// Usage:
///   await using var plc = new LogixDriver("192.168.1.100");
///   await plc.ConnectAsync();
///   var result = await plc.ReadAsync("MyTag");
///   Console.WriteLine(result.Value); // type auto-detected
/// </summary>
public sealed class LogixDriver : IPlcDriver, ITagBrowser
{
    private readonly string _host;
    private readonly ConnectionOptions _options;
    private readonly Func<Common.Transport.ITransport>? _transportFactory;
    private ConnectionManager? _connection;
    private TagDatabase? _tagDatabase;
    private TagOperations? _tagOperations;
    private StructureDecoder? _structureDecoder;
    private UdtJsonDecoder? _udtJsonDecoder;
    private UdtJsonEncoder? _udtJsonEncoder;
    private bool _disposed;

    public bool IsConnected => _connection?.IsConnected == true;

    /// <summary>
    /// Create a LogixDriver for the specified PLC host.
    /// </summary>
    /// <param name="host">PLC IP address or hostname.</param>
    /// <param name="slot">Processor slot number (0 for CompactLogix, varies for ControlLogix).</param>
    /// <param name="options">Connection options. If null, defaults are used.</param>
    public LogixDriver(string host, byte slot = 0, ConnectionOptions? options = null)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _options = options ?? new ConnectionOptions();
        _options.Slot = slot;
    }

    /// <summary>Internal constructor for testing with a custom transport factory.</summary>
    internal LogixDriver(
        string host, ConnectionOptions? options,
        Func<Common.Transport.ITransport>? transportFactory)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _options = options ?? new ConnectionOptions();
        _transportFactory = transportFactory;
    }

    /// <summary>
    /// Connect to the PLC: establishes TCP, EIP session, CIP connection,
    /// then uploads the tag database for typeless access.
    /// </summary>
    public async ValueTask ConnectAsync(CancellationToken ct = default)
    {
        if (IsConnected)
            return;

        _tagDatabase = new TagDatabase();
        _connection = _transportFactory != null
            ? new ConnectionManager(_host, _options, _transportFactory)
            : new ConnectionManager(_host, _options);

        await _connection.ConnectAsync(ct).ConfigureAwait(false);

        // Upload tag list and UDT definitions for typeless access
        await SymbolObject.UploadTagListAsync(_connection, _tagDatabase, ct).ConfigureAwait(false);
        await TemplateObject.UploadAllTemplatesAsync(_connection, _tagDatabase, ct).ConfigureAwait(false);

        var structureEncoder = new StructureEncoder(_tagDatabase);
        _tagOperations = new TagOperations(_connection, _tagDatabase, structureEncoder);
        _structureDecoder = new StructureDecoder(_tagDatabase);
        _udtJsonDecoder = new UdtJsonDecoder(_tagDatabase);
        _udtJsonEncoder = new UdtJsonEncoder(_tagDatabase);
    }

    /// <summary>
    /// Disconnect from the PLC.
    /// </summary>
    public async ValueTask DisconnectAsync(CancellationToken ct = default)
    {
        if (_connection != null)
        {
            await _connection.DisconnectAsync(ct).ConfigureAwait(false);
            _connection = null;
        }

        _tagOperations = null;
        _structureDecoder = null;
        _udtJsonDecoder = null;
        _udtJsonEncoder = null;
    }

    // --- IPlcDriver: Single Tag Operations ---

    public async ValueTask<TagResult> ReadAsync(string tagName, CancellationToken ct = default)
    {
        var ops = EnsureConnected();
        var result = await ops.ReadAsync(tagName, ct).ConfigureAwait(false);

        // If the result contains raw structure data, decode it
        if (result.IsSuccess)
            result = TryDecodeStructureResult(result);

        return result;
    }

    public async ValueTask<TagResult> WriteAsync(
        string tagName, object value, CancellationToken ct = default)
    {
        var ops = EnsureConnected();
        return await ops.WriteAsync(tagName, value, ct).ConfigureAwait(false);
    }

    // --- IPlcDriver: Batch Operations ---

    public async ValueTask<TagResult[]> ReadAsync(
        IEnumerable<string> tagNames, CancellationToken ct = default)
    {
        var ops = EnsureConnected();
        var nameList = tagNames as IReadOnlyList<string> ?? tagNames.ToList();
        var results = await ops.ReadBatchAsync(nameList, ct).ConfigureAwait(false);

        // Decode any structure results
        for (var i = 0; i < results.Length; i++)
        {
            if (results[i].IsSuccess)
                results[i] = TryDecodeStructureResult(results[i]);
        }

        return results;
    }

    public async ValueTask<TagResult[]> WriteAsync(
        IEnumerable<(string TagName, object Value)> tags, CancellationToken ct = default)
    {
        var ops = EnsureConnected();
        var tagList = tags as IReadOnlyList<(string TagName, object Value)> ?? tags.ToList();
        return await ops.WriteBatchAsync(tagList, ct).ConfigureAwait(false);
    }

    // --- IPlcDriver: JSON and Typed UDT Operations ---

    public async ValueTask<TagResult<string>> ReadJsonAsync(
        string tagName, CancellationToken ct = default)
    {
        var ops = EnsureConnected();
        var db = EnsureDatabase();

        try
        {
            var tagInfo = db.LookupTag(tagName);
            if (tagInfo is not { IsStructure: true, TemplateInstanceId: > 0 })
                return TagResult<string>.Failure(tagName,
                    "ReadJsonAsync is only supported for structure (UDT) tags.");

            var (typeCode, data, error, errorDetail) = await ops.ReadRawAsync(tagName, ct).ConfigureAwait(false);
            if (error != null)
                return TagResult<string>.Failure(tagName, error, errorDetail);

            var json = _udtJsonDecoder!.ToJson(data, tagInfo.TemplateInstanceId);
            return TagResult<string>.Success(tagName, json, tagInfo.TypeName);
        }
        catch (Exception ex)
        {
            return TagResult<string>.Failure(tagName, ex.Message);
        }
    }

    public async ValueTask<TagResult<T>> ReadAsync<T>(
        string tagName, CancellationToken ct = default) where T : class, new()
    {
        var result = await ReadJsonAsync(tagName, ct).ConfigureAwait(false);
        if (!result.IsSuccess)
            return TagResult<T>.Failure(tagName, result.Error!, result.ErrorDetail);

        try
        {
            var obj = JsonSerializer.Deserialize<T>(result.Value!, PlcJsonOptions.Default);
            if (obj == null)
                return TagResult<T>.Failure(tagName, "Deserialization returned null.");

            return TagResult<T>.Success(tagName, obj, result.TypeName);
        }
        catch (Exception ex)
        {
            return TagResult<T>.Failure(tagName, $"Deserialization failed: {ex.Message}");
        }
    }

    public async ValueTask<TagResult> WriteAsync<T>(
        string tagName, T value, CancellationToken ct = default) where T : class
    {
        // When T is string, redirect to the non-generic WriteAsync which handles
        // STRING structure encoding. C# overload resolution picks this generic method
        // for string values, but STRING tags need the standard write path, not JSON UDT encoding.
        if (value is string)
            return await WriteAsync(tagName, (object)value, ct).ConfigureAwait(false);

        var ops = EnsureConnected();
        var db = EnsureDatabase();

        try
        {
            var tagInfo = db.LookupTag(tagName);
            if (tagInfo is not { IsStructure: true, TemplateInstanceId: > 0 })
                return TagResult.Failure(tagName,
                    "WriteAsync<T> is only supported for structure (UDT) tags.");

            var json = JsonSerializer.Serialize(value, PlcJsonOptions.Default);
            var encoded = _udtJsonEncoder!.Encode(json, tagInfo.TemplateInstanceId);
            return await ops.WriteRawStructureAsync(
                tagName, tagInfo.RawTypeCode, encoded, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return TagResult.Failure(tagName, ex.Message);
        }
    }

    public async ValueTask<TagResult> WriteJsonAsync(
        string tagName, string json, CancellationToken ct = default)
    {
        var ops = EnsureConnected();
        var db = EnsureDatabase();

        try
        {
            var tagInfo = db.LookupTag(tagName);
            if (tagInfo is not { IsStructure: true, TemplateInstanceId: > 0 })
                return TagResult.Failure(tagName,
                    "WriteJsonAsync is only supported for structure (UDT) tags.");

            // Read-modify-write: get current raw bytes, patch with JSON, write back
            var (typeCode, currentData, readError, readErrorDetail) =
                await ops.ReadRawAsync(tagName, ct).ConfigureAwait(false);
            if (readError != null)
                return TagResult.Failure(tagName, $"Read for partial write failed: {readError}", readErrorDetail);

            var encoded = _udtJsonEncoder!.Encode(json, tagInfo.TemplateInstanceId, currentData);
            return await ops.WriteRawStructureAsync(
                tagName, tagInfo.RawTypeCode, encoded, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return TagResult.Failure(tagName, ex.Message);
        }
    }

    // --- ITagBrowser ---

    public ValueTask<IReadOnlyList<PlcTagInfo>> GetTagsAsync(CancellationToken ct = default)
    {
        var db = EnsureDatabase();
        return new ValueTask<IReadOnlyList<PlcTagInfo>>(db.ControllerTags);
    }

    public ValueTask<IReadOnlyList<PlcTagInfo>> GetProgramTagsAsync(
        string programName, CancellationToken ct = default)
    {
        var db = EnsureDatabase();
        return new ValueTask<IReadOnlyList<PlcTagInfo>>(db.GetProgramTags(programName));
    }

    public ValueTask<IReadOnlyList<string>> GetProgramsAsync(CancellationToken ct = default)
    {
        var db = EnsureDatabase();
        return new ValueTask<IReadOnlyList<string>>(db.Programs);
    }

    public ValueTask<UdtDefinition?> GetUdtDefinitionAsync(
        string typeName, CancellationToken ct = default)
    {
        var db = EnsureDatabase();
        return new ValueTask<UdtDefinition?>(db.GetUdtByName(typeName));
    }

    public ValueTask<IReadOnlyList<UdtDefinition>> GetAllUdtDefinitionsAsync(
        CancellationToken ct = default)
    {
        var db = EnsureDatabase();
        return new ValueTask<IReadOnlyList<UdtDefinition>>(db.UdtDefinitions);
    }

    // --- Disposal ---

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await DisconnectAsync().ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    // --- Helpers ---

    private TagOperations EnsureConnected()
    {
        return _tagOperations
            ?? throw new InvalidOperationException("Not connected. Call ConnectAsync() first.");
    }

    private TagDatabase EnsureDatabase()
    {
        return _tagDatabase
            ?? throw new InvalidOperationException("Not connected. Call ConnectAsync() first.");
    }

    /// <summary>
    /// If a TagResult contains raw structure data, decode it.
    /// STRING structures are decoded as text. Other structures become dictionaries.
    /// </summary>
    private TagResult TryDecodeStructureResult(TagResult result)
    {
        if (_structureDecoder == null || _tagDatabase == null)
            return result;

        // Check if the value is raw structure bytes
        if (result.Value.RawValue is byte[] rawBytes && result.Value.DataType == PlcDataType.Structure)
        {
            var tagInfo = _tagDatabase.LookupTag(result.TagName);
            if (tagInfo is { IsStructure: true, TemplateInstanceId: > 0 })
            {
                try
                {
                    // StructureDecoder.Decode handles STRING detection internally —
                    // STRING structures return decoded text, others return dictionaries
                    var decoded = _structureDecoder.Decode(rawBytes, tagInfo.TemplateInstanceId);
                    var typeName = decoded.DataType == PlcDataType.String
                        ? "STRING"
                        : result.TypeName;
                    return TagResult.Success(result.TagName, decoded, typeName);
                }
                catch
                {
                    // If decoding fails, fall through to pattern detection
                }
            }

            // Fallback: detect STRING from data pattern when the tag is not in
            // the database (e.g., database is empty or tag list upload failed).
            // Standard STRING (ASCIISTRING82) is 88 bytes with a valid LEN prefix.
            if (tagInfo == null && CipDataTypes.IsLikelyStringData(rawBytes))
            {
                var decoded = CipTypeCodec.DecodeString(rawBytes);
                return TagResult.Success(result.TagName, decoded, "STRING");
            }
        }

        return result;
    }
}
