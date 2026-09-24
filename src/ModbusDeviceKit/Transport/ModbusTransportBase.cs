using System.Net.Sockets;
using ModbusDeviceKit.Internal;
using NModbus;

namespace ModbusDeviceKit.Transport;

/// <summary>
/// Common NModbus-based implementation of <see cref="IModbusTransport"/>: request serialisation,
/// connection state, timeouts and translation of NModbus/IO errors into ModbusDeviceKit exceptions.
/// </summary>
public abstract class ModbusTransportBase : IModbusTransport
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IModbusMaster? _master;
    private volatile bool _disposed;

    /// <summary>Creates the transport.</summary>
    /// <param name="readTimeoutMs">Response timeout in milliseconds.</param>
    /// <param name="writeTimeoutMs">Send timeout in milliseconds.</param>
    protected ModbusTransportBase(int readTimeoutMs, int writeTimeoutMs)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(readTimeoutMs);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(writeTimeoutMs);
        ReadTimeoutMs = readTimeoutMs;
        WriteTimeoutMs = writeTimeoutMs;
    }

    /// <inheritdoc />
    public abstract string Description { get; }

    /// <summary>Response timeout in milliseconds.</summary>
    public int ReadTimeoutMs { get; }

    /// <summary>Send timeout in milliseconds.</summary>
    public int WriteTimeoutMs { get; }

    /// <inheritdoc />
    public bool IsConnected => !_disposed && Volatile.Read(ref _master) is not null && IsChannelOpen;

    /// <summary><c>true</c> while the underlying port/socket is open.</summary>
    protected abstract bool IsChannelOpen { get; }

    /// <summary>Opens the port/socket and creates the NModbus master for it.</summary>
    protected abstract Task<IModbusMaster> OpenChannelAsync(IModbusFactory factory, CancellationToken cancellationToken);

    /// <summary>Releases the port/socket. Must not throw and must tolerate being called when nothing is open.</summary>
    protected abstract void CloseChannel();

    /// <summary>
    /// Called after a failed request. Return <c>true</c> to close the channel (it is reopened on the next connect),
    /// <c>false</c> to keep it open (after <see cref="RecoverAfterFault"/>). Default: close.
    /// </summary>
    protected virtual bool ShouldResetChannel(Exception exception) => true;

    /// <summary>Cleans up a channel that stays open after a failed request (e.g. drop partial frames).</summary>
    protected virtual void RecoverAfterFault()
    {
    }

    /// <inheritdoc />
    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            if (IsConnected)
                return;

            CloseCore();
            IModbusMaster master;
            try
            {
                master = await OpenChannelAsync(new ModbusFactory(), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                CloseCore();
                throw;
            }
            catch (Exception ex)
            {
                CloseCore();
                throw new DeviceConnectionException($"Could not connect to {Description}: {ex.Message}", Description, 1, ex);
            }

            // Retries are handled by DeviceReader (Polly); NModbus must fail fast.
            master.Transport.Retries = 0;
            master.Transport.SlaveBusyUsesRetryCount = true;
            master.Transport.ReadTimeout = ReadTimeoutMs;
            master.Transport.WriteTimeout = WriteTimeoutMs;
            Volatile.Write(ref _master, master);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed)
            return;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            CloseCore();
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public Task<ushort[]> ReadHoldingRegistersAsync(byte slaveId, ushort startAddress, ushort count, CancellationToken cancellationToken = default) =>
        ExecuteAsync(slaveId, m => m.ReadHoldingRegistersAsync(slaveId, startAddress, count), cancellationToken);

    /// <inheritdoc />
    public Task<ushort[]> ReadInputRegistersAsync(byte slaveId, ushort startAddress, ushort count, CancellationToken cancellationToken = default) =>
        ExecuteAsync(slaveId, m => m.ReadInputRegistersAsync(slaveId, startAddress, count), cancellationToken);

    /// <inheritdoc />
    public Task<bool[]> ReadCoilsAsync(byte slaveId, ushort startAddress, ushort count, CancellationToken cancellationToken = default) =>
        ExecuteAsync(slaveId, m => m.ReadCoilsAsync(slaveId, startAddress, count), cancellationToken);

    /// <inheritdoc />
    public Task<bool[]> ReadDiscreteInputsAsync(byte slaveId, ushort startAddress, ushort count, CancellationToken cancellationToken = default) =>
        ExecuteAsync(slaveId, m => m.ReadInputsAsync(slaveId, startAddress, count), cancellationToken);

    /// <inheritdoc />
    public Task WriteSingleRegisterAsync(byte slaveId, ushort address, ushort value, CancellationToken cancellationToken = default) =>
        ExecuteAsync(slaveId, async m => { await m.WriteSingleRegisterAsync(slaveId, address, value).ConfigureAwait(false); return true; }, cancellationToken);

    /// <inheritdoc />
    public Task WriteMultipleRegistersAsync(byte slaveId, ushort startAddress, ushort[] values, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(values);
        return ExecuteAsync(slaveId, async m => { await m.WriteMultipleRegistersAsync(slaveId, startAddress, values).ConfigureAwait(false); return true; }, cancellationToken);
    }

    /// <inheritdoc />
    public Task WriteSingleCoilAsync(byte slaveId, ushort address, bool value, CancellationToken cancellationToken = default) =>
        ExecuteAsync(slaveId, async m => { await m.WriteSingleCoilAsync(slaveId, address, value).ConfigureAwait(false); return true; }, cancellationToken);

    /// <summary>
    /// Runs one request under the transport lock. Cancellation is honoured while waiting for the lock;
    /// a request already on the wire is bounded by <see cref="ReadTimeoutMs"/>.
    /// </summary>
    protected async Task<T> ExecuteAsync<T>(byte slaveId, Func<IModbusMaster, Task<T>> operation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ThrowIfDisposed();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            var master = Volatile.Read(ref _master);
            if (master is null || !IsChannelOpen)
                throw new DeviceConnectionException($"{Description} is not connected.", Description);

            try
            {
                return await operation(master).ConfigureAwait(false);
            }
            catch (SlaveException ex)
            {
                // The device answered, so the channel itself is healthy.
                throw new DeviceSlaveException(
                    $"Slave {slaveId} on {Description} returned Modbus exception {ex.SlaveExceptionCode} " +
                    $"({DeviceSlaveException.DescribeExceptionCode(ex.SlaveExceptionCode)}) for function code {ex.FunctionCode}.",
                    slaveId, ex.FunctionCode, ex.SlaveExceptionCode, innerException: ex);
            }
            catch (Exception ex) when (IsChannelFailure(ex))
            {
                HandleFault(ex);
                throw Translate(ex, slaveId);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
            return;

        _gate.Wait();
        try
        {
            if (_disposed)
                return;
            _disposed = true;
            CloseCore();
        }
        finally
        {
            _gate.Release();
        }

        GC.SuppressFinalize(this);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed)
                return;
            _disposed = true;
            CloseCore();
        }
        finally
        {
            _gate.Release();
        }

        GC.SuppressFinalize(this);
    }

    /// <inheritdoc />
    public override string ToString() => Description;

    /// <summary>Throws <see cref="ObjectDisposedException"/> after disposal.</summary>
    protected void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    private static bool IsChannelFailure(Exception ex) =>
        ex is TimeoutException or IOException or SocketException or FormatException or ObjectDisposedException or InvalidOperationException;

    private void HandleFault(Exception ex)
    {
        try
        {
            if (ShouldResetChannel(ex))
                CloseCore();
            else
                RecoverAfterFault();
        }
        catch (Exception)
        {
            // Recovery failed: make sure the next attempt starts from a fresh channel.
            CloseCore();
        }
    }

    private Exception Translate(Exception ex, byte slaveId)
    {
        bool timedOut = ex is TimeoutException
            || (ex is IOException io && ExceptionClassifier.IsSocketTimeout(io))
            || ex is SocketException { SocketErrorCode: SocketError.TimedOut };

        return timedOut
            ? new DeviceTimeoutException($"Slave {slaveId} on {Description} did not respond within {ReadTimeoutMs} ms.", slaveId, innerException: ex)
            : new DeviceCommunicationException($"Communication error with slave {slaveId} on {Description}: {ex.Message}", slaveId, innerException: ex);
    }

    private void CloseCore()
    {
        var master = Interlocked.Exchange(ref _master, null);
        try
        {
            master?.Dispose();
        }
        catch (Exception)
        {
            // Disposal errors of a broken channel carry no useful information.
        }

        try
        {
            CloseChannel();
        }
        catch (Exception)
        {
            // CloseChannel implementations should not throw; never let cleanup mask the real failure.
        }
    }
}
