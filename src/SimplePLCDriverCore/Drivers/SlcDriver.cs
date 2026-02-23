using SimplePLCDriverCore.Abstractions;
using SimplePLCDriverCore.Common;
using SimplePLCDriverCore.Common.Transport;
using SimplePLCDriverCore.Protocols.EtherNetIP;
using SimplePLCDriverCore.Protocols.EtherNetIP.Cip;
using SimplePLCDriverCore.Protocols.EtherNetIP.Pccc;

namespace SimplePLCDriverCore.Drivers;

/// <summary>
/// Supported legacy AB PLC models that determine protocol behavior.
/// </summary>
public enum SlcPlcType
{
    /// <summary>SLC 500 series (5/01, 5/02, 5/03, 5/04, 5/05).</summary>
    Slc500,

    /// <summary>MicroLogix 1000, 1100, 1200, 1400, 1500.</summary>
    MicroLogix,

    /// <summary>PLC-5 (uses 2-address-field variant).</summary>
    Plc5,
}

/// <summary>
/// High-level driver for Allen-Bradley SLC 500, MicroLogix, and PLC-5 PLCs.
/// Uses PCCC (Programmable Controller Communication Commands) over CIP via EtherNet/IP.
///
/// Unlike LogixDriver, SLC/MicroLogix/PLC-5 use file-based addressing (N7:0, F8:1, B3:0/5)
/// instead of symbolic tag names. There is no tag database upload - the user must know
/// the file addresses they want to access.
///
/// Usage:
///   await using var plc = new SlcDriver("192.168.1.50");
///   await plc.ConnectAsync();
///   var result = await plc.ReadAsync("N7:0");     // Integer file 7, element 0
///   var floatVal = await plc.ReadAsync("F8:1");   // Float file 8, element 1
///   var bitVal = await plc.ReadAsync("B3:0/5");   // Bit file 3, word 0, bit 5
///   await plc.WriteAsync("N7:0", 100);
/// </summary>
public sealed class SlcDriver : IPlcDriver
{
    private readonly string _host;
    private readonly ConnectionOptions _options;
    private readonly SlcPlcType _plcType;
    private readonly Func<ITransport>? _transportFactory;

    private ITransport? _transport;
    private EipSession? _session;
    private ushort _transactionId;
    private uint _originatorSerial;
    private bool _disposed;

    public bool IsConnected => _session?.IsConnected == true;

    /// <summary>
    /// Create an SlcDriver for the specified PLC host.
    /// </summary>
    /// <param name="host">PLC IP address or hostname.</param>
    /// <param name="slot">Processor slot number (typically 0 for SLC/MicroLogix).</param>
    /// <param name="plcType">PLC model type (SLC500, MicroLogix, or PLC5).</param>
    /// <param name="options">Connection options. If null, defaults are used.</param>
    public SlcDriver(string host, byte slot = 0, SlcPlcType plcType = SlcPlcType.Slc500,
        ConnectionOptions? options = null)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _plcType = plcType;
        _options = options ?? new ConnectionOptions();
        _options.Slot = slot;
        _originatorSerial = (uint)(Environment.TickCount & 0x7FFFFFFF);
    }

    /// <summary>Internal constructor for testing with a custom transport factory.</summary>
    internal SlcDriver(string host, ConnectionOptions? options,
        Func<ITransport>? transportFactory,
        SlcPlcType plcType = SlcPlcType.Slc500)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _plcType = plcType;
        _options = options ?? new ConnectionOptions();
        _transportFactory = transportFactory;
        _originatorSerial = (uint)(Environment.TickCount & 0x7FFFFFFF);
    }

    /// <summary>
    /// Connect to the PLC: establishes TCP and EIP session.
    /// Unlike Logix, SLC/MicroLogix uses unconnected messaging (no Forward Open).
    /// </summary>
    public async ValueTask ConnectAsync(CancellationToken ct = default)
    {
        if (IsConnected)
            return;

        _transport = _transportFactory != null
            ? _transportFactory()
            : new TcpTransport(_host, EipConstants.DefaultPort, _options.ConnectTimeout);

        await _transport.ConnectAsync(ct).ConfigureAwait(false);

        _session = new EipSession(_transport, _options.Slot);
        await _session.RegisterSessionAsync(ct).ConfigureAwait(false);
        // Note: No Forward Open for PCCC - we use unconnected messaging (SendRRData)
    }

    /// <summary>
    /// Disconnect from the PLC.
    /// </summary>
    public async ValueTask DisconnectAsync(CancellationToken ct = default)
    {
        if (_session != null)
        {
            await _session.DisposeAsync().ConfigureAwait(false);
            _session = null;
        }

        if (_transport != null)
        {
            await _transport.DisconnectAsync(ct).ConfigureAwait(false);
            await _transport.DisposeAsync().ConfigureAwait(false);
            _transport = null;
        }
    }

    // --- IPlcDriver: Single Tag Operations ---

    /// <summary>
    /// Read a single SLC/MicroLogix/PLC-5 address.
    /// Address format: N7:0, F8:1, B3:0/5, T4:0.ACC, etc.
    /// </summary>
    public async ValueTask<TagResult> ReadAsync(string tagName, CancellationToken ct = default)
    {
        EnsureConnected();

        try
        {
            var address = PcccAddress.Parse(tagName);
            var txnId = GetNextTransactionId();

            var request = PcccCommand.BuildReadRequest(
                address, txnId, _originatorSerial);

            // Wrap in Unconnected Send and send via SendRRData
            var response = await _session!.SendRoutedUnconnectedAsync(request, ct)
                .ConfigureAwait(false);

            if (!response.IsSuccess)
                return TagResult.Failure(tagName, response.GetUserFriendlyMessage(), response.GetErrorMessage());

            // Parse PCCC response from CIP data
            var pcccResponse = PcccCommand.ParseResponse(response.Data);

            if (!pcccResponse.IsSuccess)
                return TagResult.Failure(tagName, pcccResponse.GetErrorMessage());

            // Decode the PCCC data into a PlcTagValue
            var value = PcccTypes.DecodeValue(pcccResponse.Data.Span, address);
            var typeName = PcccTypes.GetTypeName(address.PcccFileType);

            if (address.IsBitAddress)
                typeName = "BIT";
            else if (address.HasSubElement)
                typeName = "INT"; // Sub-elements are word-sized

            return TagResult.Success(tagName, value, typeName);
        }
        catch (FormatException ex)
        {
            return TagResult.Failure(tagName, $"Invalid address: {ex.Message}");
        }
        catch (Exception ex)
        {
            return TagResult.Failure(tagName, ex.Message);
        }
    }

    /// <summary>
    /// Write a single SLC/MicroLogix/PLC-5 address.
    /// For bit addresses, a read-modify-write is performed automatically.
    /// </summary>
    public async ValueTask<TagResult> WriteAsync(
        string tagName, object value, CancellationToken ct = default)
    {
        EnsureConnected();

        try
        {
            var address = PcccAddress.Parse(tagName);

            byte[] writeData;

            if (address.IsBitAddress)
            {
                // Bit-level write: read the current word, modify the bit, write back
                writeData = await ReadModifyWriteBit(address, value, ct).ConfigureAwait(false);
            }
            else
            {
                writeData = PcccTypes.EncodeValue(value, address);
            }

            var txnId = GetNextTransactionId();
            var request = PcccCommand.BuildWriteRequest(
                address, writeData, txnId, _originatorSerial);

            var response = await _session!.SendRoutedUnconnectedAsync(request, ct)
                .ConfigureAwait(false);

            if (!response.IsSuccess)
                return TagResult.Failure(tagName, response.GetUserFriendlyMessage(), response.GetErrorMessage());

            var pcccResponse = PcccCommand.ParseResponse(response.Data);

            if (!pcccResponse.IsSuccess)
                return TagResult.Failure(tagName, pcccResponse.GetErrorMessage());

            var typeName = PcccTypes.GetTypeName(address.PcccFileType);
            return TagResult.Success(tagName, PlcTagValue.Null, typeName);
        }
        catch (FormatException ex)
        {
            return TagResult.Failure(tagName, $"Invalid address: {ex.Message}");
        }
        catch (Exception ex)
        {
            return TagResult.Failure(tagName, ex.Message);
        }
    }

    // --- IPlcDriver: Batch Operations ---

    /// <summary>
    /// Read multiple SLC addresses. Each address is sent as a separate PCCC request
    /// since PCCC does not support CIP Multiple Service Packet batching.
    /// </summary>
    public async ValueTask<TagResult[]> ReadAsync(
        IEnumerable<string> tagNames, CancellationToken ct = default)
    {
        var nameList = tagNames as IReadOnlyList<string> ?? tagNames.ToList();
        var results = new TagResult[nameList.Count];

        for (var i = 0; i < nameList.Count; i++)
        {
            results[i] = await ReadAsync(nameList[i], ct).ConfigureAwait(false);
        }

        return results;
    }

    /// <summary>
    /// Write multiple SLC addresses. Each address is sent as a separate PCCC request.
    /// </summary>
    public async ValueTask<TagResult[]> WriteAsync(
        IEnumerable<(string TagName, object Value)> tags, CancellationToken ct = default)
    {
        var tagList = tags as IReadOnlyList<(string TagName, object Value)> ?? tags.ToList();
        var results = new TagResult[tagList.Count];

        for (var i = 0; i < tagList.Count; i++)
        {
            results[i] = await WriteAsync(tagList[i].TagName, tagList[i].Value, ct)
                .ConfigureAwait(false);
        }

        return results;
    }

    // --- IPlcDriver: JSON/Typed UDT operations (not supported for SLC) ---

    public ValueTask<TagResult<string>> ReadJsonAsync(string tagName, CancellationToken ct = default)
    {
        return new ValueTask<TagResult<string>>(
            TagResult<string>.Failure(tagName,
                "ReadJsonAsync is not supported for SLC/MicroLogix/PLC-5 PLCs. Use ReadAsync instead."));
    }

    public ValueTask<TagResult<T>> ReadAsync<T>(string tagName, CancellationToken ct = default) where T : class, new()
    {
        return new ValueTask<TagResult<T>>(
            TagResult<T>.Failure(tagName,
                "Typed ReadAsync<T> is not supported for SLC/MicroLogix/PLC-5 PLCs. Use ReadAsync instead."));
    }

    public ValueTask<TagResult> WriteAsync<T>(string tagName, T value, CancellationToken ct = default) where T : class
    {
        // If value is a string, redirect to the non-generic WriteAsync
        if (value is string)
            return WriteAsync(tagName, (object)value, ct);

        return new ValueTask<TagResult>(
            TagResult.Failure(tagName,
                "Typed WriteAsync<T> is not supported for SLC/MicroLogix/PLC-5 PLCs. Use WriteAsync instead."));
    }

    public ValueTask<TagResult> WriteJsonAsync(string tagName, string json, CancellationToken ct = default)
    {
        return new ValueTask<TagResult>(
            TagResult.Failure(tagName,
                "WriteJsonAsync is not supported for SLC/MicroLogix/PLC-5 PLCs. Use WriteAsync instead."));
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

    private void EnsureConnected()
    {
        if (!IsConnected)
            throw new InvalidOperationException("Not connected. Call ConnectAsync() first.");
    }

    private ushort GetNextTransactionId() => ++_transactionId;

    /// <summary>
    /// Read the current word containing a bit, modify the target bit, and return
    /// the modified word bytes for writing.
    /// </summary>
    private async ValueTask<byte[]> ReadModifyWriteBit(
        PcccAddress address, object value, CancellationToken ct)
    {
        // Build a read address for the word containing the bit
        var wordAddress = new PcccAddress(
            address.FileType, address.FileNumber, address.Element,
            subElement: Math.Max(address.SubElement, 0),
            bitNumber: -1, // Read the whole word
            pcccFileType: address.PcccFileType);

        var txnId = GetNextTransactionId();
        var readRequest = PcccCommand.BuildReadRequest(wordAddress, txnId, _originatorSerial);

        var readResponse = await _session!.SendRoutedUnconnectedAsync(readRequest, ct)
            .ConfigureAwait(false);

        if (!readResponse.IsSuccess)
            throw new IOException($"Read-modify-write failed: {readResponse.GetErrorMessage()}");

        var pcccReadResponse = PcccCommand.ParseResponse(readResponse.Data);
        if (!pcccReadResponse.IsSuccess)
            throw new IOException($"Read-modify-write PCCC failed: {pcccReadResponse.GetErrorMessage()}");

        if (pcccReadResponse.Data.Length < 2)
            throw new IOException("Read-modify-write: response too short");

        // Modify the bit
        var currentWord = System.Buffers.Binary.BinaryPrimitives
            .ReadUInt16LittleEndian(pcccReadResponse.Data.Span);

        var bitMask = PcccTypes.GetBitMask(address.BitNumber);
        var bitValue = Convert.ToBoolean(value);

        ushort newWord;
        if (bitValue)
            newWord = (ushort)(currentWord | bitMask);
        else
            newWord = (ushort)(currentWord & ~bitMask);

        var result = new byte[2];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(result, newWord);
        return result;
    }
}
