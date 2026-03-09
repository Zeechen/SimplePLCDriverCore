using System.Buffers.Binary;
using System.Text;
using SimplePLCDriverCore.Abstractions;
using SimplePLCDriverCore.Common;
using SimplePLCDriverCore.Common.Transport;
using SimplePLCDriverCore.Protocols.Modbus;

namespace SimplePLCDriverCore.Drivers;

/// <summary>
/// High-level driver for Modbus TCP devices.
/// Supports any device that speaks Modbus TCP (port 502).
///
/// Addressing uses prefix notation:
///   HR100    - Holding Register 100 (read/write, 16-bit)
///   IR100    - Input Register 100 (read-only, 16-bit)
///   C100     - Coil 100 (read/write, 1-bit)
///   DI100    - Discrete Input 100 (read-only, 1-bit)
///
///   Classic Modbus numeric addresses:
///   400001   - Holding Register 0
///   300001   - Input Register 0
///   100001   - Discrete Input 0
///   1        - Coil 0
///
/// Usage:
///   await using var device = new ModbusDriver("192.168.1.50");
///   await device.ConnectAsync();
///   var result = await device.ReadAsync("HR100");
///   await device.WriteAsync("HR100", (short)42);
/// </summary>
public sealed class ModbusDriver : IPlcDriver
{
    private readonly string _host;
    private readonly int _port;
    private readonly byte _unitId;
    private readonly ModbusByteOrder _defaultByteOrder;
    private readonly ConnectionOptions _options;
    private readonly Func<ITransport>? _transportFactory;

    private ITransport? _transport;
    private ModbusSession? _session;
    private bool _disposed;

    public bool IsConnected => _session?.IsConnected == true;

    /// <summary>
    /// The default byte order used for multi-register typed reads/writes.
    /// </summary>
    public ModbusByteOrder DefaultByteOrder => _defaultByteOrder;

    /// <summary>
    /// Create a ModbusDriver for the specified device.
    /// </summary>
    /// <param name="host">Device IP address or hostname.</param>
    /// <param name="port">Modbus TCP port. Default 502.</param>
    /// <param name="unitId">Modbus unit/slave ID. Default 1.</param>
    /// <param name="options">Connection options. If null, defaults are used.</param>
    /// <param name="byteOrder">Default byte order for multi-register data types. Default ABCD (big-endian).</param>
    public ModbusDriver(string host, int port = 502, byte unitId = 1,
        ConnectionOptions? options = null, ModbusByteOrder byteOrder = ModbusByteOrder.ABCD)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _port = port;
        _unitId = unitId;
        _options = options ?? new ConnectionOptions();
        _defaultByteOrder = byteOrder;
    }

    /// <summary>Internal constructor for testing with a custom transport factory.</summary>
    internal ModbusDriver(string host, int port, byte unitId,
        ConnectionOptions? options, Func<ITransport>? transportFactory,
        ModbusByteOrder byteOrder = ModbusByteOrder.ABCD)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _port = port;
        _unitId = unitId;
        _options = options ?? new ConnectionOptions();
        _transportFactory = transportFactory;
        _defaultByteOrder = byteOrder;
    }

    /// <summary>
    /// Connect to the Modbus TCP device. No application-level handshake needed.
    /// </summary>
    public async ValueTask ConnectAsync(CancellationToken ct = default)
    {
        if (IsConnected)
            return;

        _transport = _transportFactory != null
            ? _transportFactory()
            : new TcpTransport(_host, _port, _options.ConnectTimeout);

        await _transport.ConnectAsync(ct).ConfigureAwait(false);

        _session = new ModbusSession(_transport, _unitId);
        _session.MarkConnected();
    }

    /// <summary>
    /// Disconnect from the device.
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
    /// Read a single Modbus address.
    /// </summary>
    public async ValueTask<TagResult> ReadAsync(string tagName, CancellationToken ct = default)
    {
        EnsureConnected();

        try
        {
            var address = ModbusAddress.Parse(tagName);
            var txId = _session!.GetNextTransactionId();

            var request = address.RegisterType switch
            {
                ModbusRegisterType.Coil =>
                    ModbusMessage.BuildReadCoils(txId, _unitId, (ushort)address.Address, 1),
                ModbusRegisterType.DiscreteInput =>
                    ModbusMessage.BuildReadDiscreteInputs(txId, _unitId, (ushort)address.Address, 1),
                ModbusRegisterType.HoldingRegister =>
                    ModbusMessage.BuildReadHoldingRegisters(txId, _unitId, (ushort)address.Address, 1),
                ModbusRegisterType.InputRegister =>
                    ModbusMessage.BuildReadInputRegisters(txId, _unitId, (ushort)address.Address, 1),
                _ => throw new InvalidOperationException($"Unsupported register type: {address.RegisterType}")
            };

            var response = await _session.SendAsync(request, ct).ConfigureAwait(false);

            if (!response.IsSuccess)
                return TagResult.Failure(tagName, "Modbus read failed", response.GetErrorMessage());

            if (response.Data.Length == 0)
                return TagResult.Failure(tagName, "Modbus read returned no data");

            var value = ModbusTypes.DecodeValue(response.Data.Span, address);
            var typeName = ModbusTypes.GetTypeName(address);

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
    /// Write a single Modbus address.
    /// </summary>
    public async ValueTask<TagResult> WriteAsync(
        string tagName, object value, CancellationToken ct = default)
    {
        EnsureConnected();

        try
        {
            var address = ModbusAddress.Parse(tagName);
            var txId = _session!.GetNextTransactionId();

            byte[] request;

            if (address.IsBitRegister)
            {
                if (address.RegisterType == ModbusRegisterType.DiscreteInput)
                    return TagResult.Failure(tagName, "Cannot write to Discrete Inputs (read-only)");

                var boolValue = Convert.ToBoolean(value);
                request = ModbusMessage.BuildWriteSingleCoil(txId, _unitId,
                    (ushort)address.Address, boolValue);
            }
            else
            {
                if (address.RegisterType == ModbusRegisterType.InputRegister)
                    return TagResult.Failure(tagName, "Cannot write to Input Registers (read-only)");

                var regValue = ModbusTypes.EncodeRegister(value);
                request = ModbusMessage.BuildWriteSingleRegister(txId, _unitId,
                    (ushort)address.Address, regValue);
            }

            var response = await _session.SendAsync(request, ct).ConfigureAwait(false);

            if (!response.IsSuccess)
                return TagResult.Failure(tagName, "Modbus write failed", response.GetErrorMessage());

            var typeName = ModbusTypes.GetTypeName(address);
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
    /// Read multiple Modbus addresses. Each is sent individually.
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
    /// Write multiple Modbus addresses. Each is sent individually.
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

    // --- IPlcDriver: JSON/Typed operations (not supported for Modbus) ---

    public ValueTask<TagResult<string>> ReadJsonAsync(string tagName, CancellationToken ct = default)
    {
        return new ValueTask<TagResult<string>>(
            TagResult<string>.Failure(tagName,
                "ReadJsonAsync is not supported for Modbus devices. Use ReadAsync instead."));
    }

    public ValueTask<TagResult<T>> ReadAsync<T>(string tagName, CancellationToken ct = default) where T : class, new()
    {
        return new ValueTask<TagResult<T>>(
            TagResult<T>.Failure(tagName,
                "Typed ReadAsync<T> is not supported for Modbus devices. Use ReadAsync instead."));
    }

    public ValueTask<TagResult> WriteAsync<T>(string tagName, T value, CancellationToken ct = default) where T : class
    {
        if (value is string)
            return WriteAsync(tagName, (object)value, ct);

        return new ValueTask<TagResult>(
            TagResult.Failure(tagName,
                "Typed WriteAsync<T> is not supported for Modbus devices. Use WriteAsync instead."));
    }

    public ValueTask<TagResult> WriteJsonAsync(string tagName, string json, CancellationToken ct = default)
    {
        return new ValueTask<TagResult>(
            TagResult.Failure(tagName,
                "WriteJsonAsync is not supported for Modbus devices. Use WriteAsync instead."));
    }

    // ===== Phase 5: Advanced Function Codes =====

    /// <summary>
    /// FC 22 — Mask Write Register. Atomically modifies bits in a single holding register.
    /// Result = (Current AND andMask) OR (orMask AND NOT(andMask))
    /// </summary>
    public async ValueTask<TagResult> MaskWriteRegisterAsync(
        string address, ushort andMask, ushort orMask, CancellationToken ct = default)
    {
        EnsureConnected();
        try
        {
            var addr = ModbusAddress.Parse(address);
            if (addr.RegisterType != ModbusRegisterType.HoldingRegister)
                return TagResult.Failure(address, "Mask Write only applies to Holding Registers.");

            var txId = _session!.GetNextTransactionId();
            var request = ModbusMessage.BuildMaskWriteRegister(txId, _unitId,
                (ushort)addr.Address, andMask, orMask);
            var response = await _session.SendAsync(request, ct).ConfigureAwait(false);

            return response.IsSuccess
                ? TagResult.Success(address, PlcTagValue.Null, "HOLDING_REGISTER")
                : TagResult.Failure(address, "Mask write failed", response.GetErrorMessage());
        }
        catch (Exception ex)
        {
            return TagResult.Failure(address, ex.Message);
        }
    }

    /// <summary>
    /// Convenience: Set a single bit in a holding register using FC 22.
    /// </summary>
    public ValueTask<TagResult> SetBitAsync(
        string address, int bitPosition, CancellationToken ct = default)
    {
        if (bitPosition < 0 || bitPosition > 15)
            return new ValueTask<TagResult>(
                TagResult.Failure(address, "Bit position must be 0-15."));

        // AND mask = 0xFFFF (keep all bits), OR mask = bit to set
        return MaskWriteRegisterAsync(address, 0xFFFF, (ushort)(1 << bitPosition), ct);
    }

    /// <summary>
    /// Convenience: Clear a single bit in a holding register using FC 22.
    /// </summary>
    public ValueTask<TagResult> ClearBitAsync(
        string address, int bitPosition, CancellationToken ct = default)
    {
        if (bitPosition < 0 || bitPosition > 15)
            return new ValueTask<TagResult>(
                TagResult.Failure(address, "Bit position must be 0-15."));

        // AND mask = all bits except the one to clear, OR mask = 0x0000
        return MaskWriteRegisterAsync(address, (ushort)~(1 << bitPosition), 0x0000, ct);
    }

    /// <summary>
    /// FC 23 — Read/Write Multiple Registers in a single atomic transaction.
    /// The write is performed before the read.
    /// </summary>
    public async ValueTask<ModbusReadWriteResult> ReadWriteMultipleRegistersAsync(
        string readAddress, ushort readCount,
        string writeAddress, ReadOnlyMemory<short> writeValues,
        CancellationToken ct = default)
    {
        EnsureConnected();
        try
        {
            var rAddr = ModbusAddress.Parse(readAddress);
            var wAddr = ModbusAddress.Parse(writeAddress);

            if (rAddr.RegisterType != ModbusRegisterType.HoldingRegister)
                return ModbusReadWriteResult.Failure("Read address must be a Holding Register.");
            if (wAddr.RegisterType != ModbusRegisterType.HoldingRegister)
                return ModbusReadWriteResult.Failure("Write address must be a Holding Register.");

            var writeArray = writeValues.ToArray();
            var writeRegs = new ushort[writeArray.Length];
            for (int i = 0; i < writeArray.Length; i++)
                writeRegs[i] = (ushort)writeArray[i];

            var txId = _session!.GetNextTransactionId();
            var request = ModbusMessage.BuildReadWriteMultipleRegisters(txId, _unitId,
                (ushort)rAddr.Address, readCount,
                (ushort)wAddr.Address, writeRegs);

            var response = await _session.SendAsync(request, ct).ConfigureAwait(false);

            if (!response.IsSuccess)
                return ModbusReadWriteResult.Failure(response.GetErrorMessage());

            return ParseReadWriteResponse(response.Data, readCount);
        }
        catch (Exception ex)
        {
            return ModbusReadWriteResult.Failure(ex.Message);
        }
    }

    /// <summary>
    /// FC 08 — Diagnostics. Send a diagnostic sub-function request.
    /// </summary>
    public async ValueTask<ModbusDiagnosticResult> DiagnosticsAsync(
        ModbusDiagnosticSubFunction subFunction, ushort data = 0,
        CancellationToken ct = default)
    {
        EnsureConnected();
        try
        {
            var txId = _session!.GetNextTransactionId();
            var request = ModbusMessage.BuildDiagnostics(txId, _unitId, (ushort)subFunction, data);
            var response = await _session.SendAsync(request, ct).ConfigureAwait(false);

            if (!response.IsSuccess)
                return ModbusDiagnosticResult.Failure(subFunction, response.GetErrorMessage());

            return ParseDiagnosticResponse(response.Data, subFunction);
        }
        catch (Exception ex)
        {
            return ModbusDiagnosticResult.Failure(subFunction, ex.Message);
        }
    }

    /// <summary>
    /// FC 43/14 — Read Device Identification.
    /// </summary>
    public async ValueTask<ModbusDeviceIdentification> ReadDeviceIdentificationAsync(
        ModbusDeviceIdLevel level = ModbusDeviceIdLevel.Basic,
        CancellationToken ct = default)
    {
        EnsureConnected();

        var allObjects = new Dictionary<int, string>();
        byte nextObjectId = 0x00;
        bool moreFollows = true;

        while (moreFollows)
        {
            var txId = _session!.GetNextTransactionId();
            var request = ModbusMessage.BuildReadDeviceIdentification(txId, _unitId,
                (byte)level, nextObjectId);
            var response = await _session.SendAsync(request, ct).ConfigureAwait(false);

            if (!response.IsSuccess)
                throw new InvalidOperationException(
                    $"Read Device Identification failed: {response.GetErrorMessage()}");

            var dataArr = response.Data.ToArray();
            ParseDeviceIdResponse(dataArr, allObjects, out moreFollows, out nextObjectId);

            if (!moreFollows) break;
        }

        return new ModbusDeviceIdentification(
            ConformityLevel: (byte)level,
            VendorName: allObjects.GetValueOrDefault(0x00),
            ProductCode: allObjects.GetValueOrDefault(0x01),
            MajorMinorRevision: allObjects.GetValueOrDefault(0x02),
            VendorUrl: allObjects.GetValueOrDefault(0x03),
            ProductName: allObjects.GetValueOrDefault(0x04),
            ModelName: allObjects.GetValueOrDefault(0x05),
            UserApplicationName: allObjects.GetValueOrDefault(0x06),
            AllObjects: allObjects);
    }

    /// <summary>
    /// FC 20 — Read File Record.
    /// </summary>
    public async ValueTask<byte[][]> ReadFileRecordAsync(
        ushort fileNumber, ushort recordNumber, ushort recordLength,
        CancellationToken ct = default)
    {
        EnsureConnected();

        var txId = _session!.GetNextTransactionId();
        var request = ModbusMessage.BuildReadFileRecord(txId, _unitId,
            fileNumber, recordNumber, recordLength);
        var response = await _session.SendAsync(request, ct).ConfigureAwait(false);

        if (!response.IsSuccess)
            throw new InvalidOperationException(
                $"Read file record failed: {response.GetErrorMessage()}");

        return ParseFileRecordResponse(response.Data.ToArray());
    }

    /// <summary>
    /// FC 21 — Write File Record.
    /// </summary>
    public async ValueTask<TagResult> WriteFileRecordAsync(
        ushort fileNumber, ushort recordNumber, ReadOnlyMemory<byte> data,
        CancellationToken ct = default)
    {
        EnsureConnected();
        try
        {
            var txId = _session!.GetNextTransactionId();
            var dataArray = data.ToArray();
            var request = ModbusMessage.BuildWriteFileRecord(txId, _unitId,
                fileNumber, recordNumber, dataArray);
            var response = await _session.SendAsync(request, ct).ConfigureAwait(false);

            return response.IsSuccess
                ? TagResult.Success($"File:{fileNumber}:{recordNumber}", PlcTagValue.Null, "FILE_RECORD")
                : TagResult.Failure($"File:{fileNumber}:{recordNumber}",
                    "Write file record failed", response.GetErrorMessage());
        }
        catch (Exception ex)
        {
            return TagResult.Failure($"File:{fileNumber}:{recordNumber}", ex.Message);
        }
    }

    /// <summary>
    /// FC 24 — Read FIFO Queue.
    /// </summary>
    public async ValueTask<short[]> ReadFifoQueueAsync(
        string pointerAddress, CancellationToken ct = default)
    {
        EnsureConnected();

        var addr = ModbusAddress.Parse(pointerAddress);
        var txId = _session!.GetNextTransactionId();
        var request = ModbusMessage.BuildReadFifoQueue(txId, _unitId, (ushort)addr.Address);
        var response = await _session.SendAsync(request, ct).ConfigureAwait(false);

        if (!response.IsSuccess)
            throw new InvalidOperationException(
                $"Read FIFO queue failed: {response.GetErrorMessage()}");

        return ParseFifoResponse(response.Data.ToArray());
    }

    // ===== Phase 5: Multi-Register Typed Access =====

    /// <summary>
    /// Read a 32-bit float from 2 consecutive holding registers.
    /// </summary>
    public async ValueTask<float> ReadFloat32Async(
        string address, ModbusByteOrder? byteOrder = null, CancellationToken ct = default)
    {
        var data = await ReadRegistersRawAsync(address, 2, ct).ConfigureAwait(false);
        return ModbusTypes.DecodeFloat32(data, byteOrder ?? _defaultByteOrder);
    }

    /// <summary>
    /// Read a 64-bit double from 4 consecutive holding registers.
    /// </summary>
    public async ValueTask<double> ReadFloat64Async(
        string address, ModbusByteOrder? byteOrder = null, CancellationToken ct = default)
    {
        var data = await ReadRegistersRawAsync(address, 4, ct).ConfigureAwait(false);
        return ModbusTypes.DecodeFloat64(data, byteOrder ?? _defaultByteOrder);
    }

    /// <summary>
    /// Read a 32-bit signed integer from 2 consecutive holding registers.
    /// </summary>
    public async ValueTask<int> ReadInt32Async(
        string address, ModbusByteOrder? byteOrder = null, CancellationToken ct = default)
    {
        var data = await ReadRegistersRawAsync(address, 2, ct).ConfigureAwait(false);
        return ModbusTypes.DecodeInt32(data, byteOrder ?? _defaultByteOrder);
    }

    /// <summary>
    /// Read a 32-bit unsigned integer from 2 consecutive holding registers.
    /// </summary>
    public async ValueTask<uint> ReadUInt32Async(
        string address, ModbusByteOrder? byteOrder = null, CancellationToken ct = default)
    {
        var data = await ReadRegistersRawAsync(address, 2, ct).ConfigureAwait(false);
        return ModbusTypes.DecodeUInt32(data, byteOrder ?? _defaultByteOrder);
    }

    /// <summary>
    /// Read a 64-bit signed integer from 4 consecutive holding registers.
    /// </summary>
    public async ValueTask<long> ReadInt64Async(
        string address, ModbusByteOrder? byteOrder = null, CancellationToken ct = default)
    {
        var data = await ReadRegistersRawAsync(address, 4, ct).ConfigureAwait(false);
        return ModbusTypes.DecodeInt64(data, byteOrder ?? _defaultByteOrder);
    }

    /// <summary>
    /// Read a string from consecutive holding registers (2 chars per register, ASCII).
    /// </summary>
    public async ValueTask<string> ReadStringAsync(
        string address, ushort registerCount, CancellationToken ct = default)
    {
        var data = await ReadRegistersRawAsync(address, registerCount, ct).ConfigureAwait(false);
        return ModbusTypes.DecodeString(data, registerCount);
    }

    /// <summary>
    /// Write a 32-bit float to 2 consecutive holding registers.
    /// </summary>
    public async ValueTask<TagResult> WriteFloat32Async(
        string address, float value, ModbusByteOrder? byteOrder = null,
        CancellationToken ct = default)
    {
        return await WriteMultipleRegistersAsync(address,
            ModbusTypes.EncodeFloat32(value, byteOrder ?? _defaultByteOrder), ct)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Write a 64-bit double to 4 consecutive holding registers.
    /// </summary>
    public async ValueTask<TagResult> WriteFloat64Async(
        string address, double value, ModbusByteOrder? byteOrder = null,
        CancellationToken ct = default)
    {
        return await WriteMultipleRegistersAsync(address,
            ModbusTypes.EncodeFloat64(value, byteOrder ?? _defaultByteOrder), ct)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Write a 32-bit integer to 2 consecutive holding registers.
    /// </summary>
    public async ValueTask<TagResult> WriteInt32Async(
        string address, int value, ModbusByteOrder? byteOrder = null,
        CancellationToken ct = default)
    {
        return await WriteMultipleRegistersAsync(address,
            ModbusTypes.EncodeInt32(value, byteOrder ?? _defaultByteOrder), ct)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Write a string to consecutive holding registers (2 chars per register, ASCII).
    /// </summary>
    public async ValueTask<TagResult> WriteStringAsync(
        string address, string value, ushort registerCount,
        CancellationToken ct = default)
    {
        return await WriteMultipleRegistersAsync(address,
            ModbusTypes.EncodeString(value, registerCount), ct)
            .ConfigureAwait(false);
    }

    // ===== Phase 5: Raw Command API =====

    /// <summary>
    /// Send any Modbus function code with an arbitrary payload.
    /// The MBAP header is built automatically.
    /// </summary>
    public async ValueTask<ModbusRawResponse> SendRawAsync(
        byte functionCode, ReadOnlyMemory<byte> payload,
        CancellationToken ct = default)
    {
        EnsureConnected();
        try
        {
            var txId = _session!.GetNextTransactionId();
            var payloadArray = payload.ToArray();
            var request = ModbusMessage.BuildRaw(txId, _unitId, functionCode, payloadArray);
            var response = await _session.SendAsync(request, ct).ConfigureAwait(false);

            if (!response.IsSuccess)
                return ModbusRawResponse.Failure(response.FunctionCode,
                    response.ExceptionCode, response.GetErrorMessage());

            return ModbusRawResponse.Success(response.FunctionCode, response.Data);
        }
        catch (Exception ex)
        {
            return new ModbusRawResponse(functionCode, ReadOnlyMemory<byte>.Empty,
                false, null, ex.Message);
        }
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

    // --- Static parse helpers (non-async, so Span is allowed) ---

    private static ModbusReadWriteResult ParseReadWriteResponse(ReadOnlyMemory<byte> memory, ushort readCount)
    {
        var data = memory.Span;
        if (data.Length < 1)
            return ModbusReadWriteResult.Failure("Empty response.");

        var byteCount = data[0];
        var regData = data.Slice(1, byteCount);
        var values = new short[readCount];
        for (int i = 0; i < readCount && i * 2 + 1 < regData.Length; i++)
            values[i] = BinaryPrimitives.ReadInt16BigEndian(regData.Slice(i * 2, 2));

        return ModbusReadWriteResult.Success(values);
    }

    private static ModbusDiagnosticResult ParseDiagnosticResponse(
        ReadOnlyMemory<byte> memory, ModbusDiagnosticSubFunction subFunction)
    {
        var data = memory.Span;
        ushort responseData = data.Length >= 4
            ? BinaryPrimitives.ReadUInt16BigEndian(data.Slice(2, 2))
            : (ushort)0;
        return ModbusDiagnosticResult.Success(subFunction, responseData);
    }

    private static void ParseDeviceIdResponse(byte[] data, Dictionary<int, string> allObjects,
        out bool moreFollows, out byte nextObjectId)
    {
        if (data.Length < 6)
            throw new InvalidOperationException("Device identification response too short.");

        moreFollows = data[3] != 0x00;
        nextObjectId = data[4];
        var numObjects = data[5];
        var offset = 6;

        for (int i = 0; i < numObjects && offset + 2 <= data.Length; i++)
        {
            var objectId = data[offset++];
            var objectLength = data[offset++];
            if (offset + objectLength > data.Length) break;
            var objectValue = Encoding.ASCII.GetString(data, offset, objectLength);
            offset += objectLength;
            allObjects[objectId] = objectValue;
        }
    }

    private static byte[][] ParseFileRecordResponse(byte[] data)
    {
        if (data.Length < 1)
            return Array.Empty<byte[]>();

        var totalLen = data[0];
        var results = new List<byte[]>();
        var offset = 1;

        while (offset < 1 + totalLen && offset + 2 <= data.Length)
        {
            var groupLen = data[offset++];
            var refType = data[offset++]; // reference type (0x06)
            var recordDataLen = groupLen - 1;
            if (offset + recordDataLen > data.Length) break;
            var record = new byte[recordDataLen];
            Array.Copy(data, offset, record, 0, recordDataLen);
            results.Add(record);
            offset += recordDataLen;
        }

        return results.ToArray();
    }

    private static short[] ParseFifoResponse(byte[] data)
    {
        if (data.Length < 4)
            return Array.Empty<short>();

        var fifoCount = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(2, 2));
        var values = new short[fifoCount];
        var offset = 4;
        for (int i = 0; i < fifoCount && offset + 1 < data.Length; i++)
        {
            values[i] = BinaryPrimitives.ReadInt16BigEndian(data.AsSpan(offset, 2));
            offset += 2;
        }

        return values;
    }

    private void EnsureConnected()
    {
        if (!IsConnected)
            throw new InvalidOperationException("Not connected. Call ConnectAsync() first.");
    }

    /// <summary>
    /// Read N raw register bytes (no byte-count prefix) from consecutive holding/input registers.
    /// </summary>
    private async ValueTask<byte[]> ReadRegistersRawAsync(
        string address, ushort registerCount, CancellationToken ct)
    {
        EnsureConnected();

        var addr = ModbusAddress.Parse(address);
        var txId = _session!.GetNextTransactionId();

        var request = addr.RegisterType switch
        {
            ModbusRegisterType.HoldingRegister =>
                ModbusMessage.BuildReadHoldingRegisters(txId, _unitId, (ushort)addr.Address, registerCount),
            ModbusRegisterType.InputRegister =>
                ModbusMessage.BuildReadInputRegisters(txId, _unitId, (ushort)addr.Address, registerCount),
            _ => throw new InvalidOperationException(
                "Multi-register typed reads only apply to Holding Registers (HR) and Input Registers (IR).")
        };

        var response = await _session.SendAsync(request, ct).ConfigureAwait(false);

        if (!response.IsSuccess)
            throw new InvalidOperationException($"Modbus read failed: {response.GetErrorMessage()}");

        var dataArr = response.Data.ToArray();
        if (dataArr.Length < 1)
            throw new InvalidOperationException("Modbus read returned no data.");

        var byteCount = dataArr[0];
        if (dataArr.Length < 1 + byteCount)
            throw new InvalidOperationException("Modbus response data truncated.");

        var result = new byte[byteCount];
        Array.Copy(dataArr, 1, result, 0, byteCount);
        return result;
    }

    /// <summary>
    /// Write multiple register values to consecutive holding registers.
    /// </summary>
    private async ValueTask<TagResult> WriteMultipleRegistersAsync(
        string address, ushort[] values, CancellationToken ct)
    {
        EnsureConnected();
        try
        {
            var addr = ModbusAddress.Parse(address);
            if (addr.RegisterType != ModbusRegisterType.HoldingRegister)
                return TagResult.Failure(address, "Multi-register writes only apply to Holding Registers.");

            var txId = _session!.GetNextTransactionId();
            var request = ModbusMessage.BuildWriteMultipleRegisters(txId, _unitId,
                (ushort)addr.Address, values);
            var response = await _session.SendAsync(request, ct).ConfigureAwait(false);

            return response.IsSuccess
                ? TagResult.Success(address, PlcTagValue.Null, "HOLDING_REGISTER")
                : TagResult.Failure(address, "Modbus write failed", response.GetErrorMessage());
        }
        catch (Exception ex)
        {
            return TagResult.Failure(address, ex.Message);
        }
    }
}
