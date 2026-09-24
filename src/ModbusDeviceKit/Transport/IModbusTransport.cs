namespace ModbusDeviceKit.Transport;

/// <summary>
/// A Modbus master channel (serial RTU, TCP, …). Implementations must be safe for concurrent use:
/// requests are serialised because Modbus is a strict request/response protocol.
/// </summary>
/// <remarks>
/// Failures are reported as <see cref="DeviceTimeoutException"/> (no answer),
/// <see cref="DeviceSlaveException"/> (Modbus exception response),
/// <see cref="DeviceCommunicationException"/> (CRC / framing / I/O error) or
/// <see cref="DeviceConnectionException"/> (channel cannot be opened or is closed).
/// A transport does not retry by itself; retries are done by <see cref="DeviceReader"/>.
/// </remarks>
public interface IModbusTransport : IDisposable, IAsyncDisposable
{
    /// <summary>Human readable endpoint description, e.g. <c>"Modbus RTU COM3 (9600 8N1)"</c>.</summary>
    string Description { get; }

    /// <summary><c>true</c> while the channel is open and usable.</summary>
    bool IsConnected { get; }

    /// <summary>Opens the channel. Does nothing when already connected.</summary>
    /// <exception cref="DeviceConnectionException">The channel could not be opened.</exception>
    Task ConnectAsync(CancellationToken cancellationToken = default);

    /// <summary>Closes the channel. It can be reopened with <see cref="ConnectAsync"/>.</summary>
    Task DisconnectAsync(CancellationToken cancellationToken = default);

    /// <summary>Reads holding registers (function code 03).</summary>
    Task<ushort[]> ReadHoldingRegistersAsync(byte slaveId, ushort startAddress, ushort count, CancellationToken cancellationToken = default);

    /// <summary>Reads input registers (function code 04).</summary>
    Task<ushort[]> ReadInputRegistersAsync(byte slaveId, ushort startAddress, ushort count, CancellationToken cancellationToken = default);

    /// <summary>Reads coils (function code 01).</summary>
    Task<bool[]> ReadCoilsAsync(byte slaveId, ushort startAddress, ushort count, CancellationToken cancellationToken = default);

    /// <summary>Reads discrete inputs (function code 02).</summary>
    Task<bool[]> ReadDiscreteInputsAsync(byte slaveId, ushort startAddress, ushort count, CancellationToken cancellationToken = default);

    /// <summary>Writes one holding register (function code 06).</summary>
    Task WriteSingleRegisterAsync(byte slaveId, ushort address, ushort value, CancellationToken cancellationToken = default);

    /// <summary>Writes consecutive holding registers (function code 16).</summary>
    Task WriteMultipleRegistersAsync(byte slaveId, ushort startAddress, ushort[] values, CancellationToken cancellationToken = default);

    /// <summary>Writes one coil (function code 05).</summary>
    Task WriteSingleCoilAsync(byte slaveId, ushort address, bool value, CancellationToken cancellationToken = default);
}
