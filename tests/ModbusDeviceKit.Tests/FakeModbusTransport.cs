using ModbusDeviceKit.Transport;

namespace ModbusDeviceKit.Tests;

/// <summary>In-memory transport with scriptable failures.</summary>
internal sealed class FakeModbusTransport : IModbusTransport
{
    public Dictionary<ushort, ushort> HoldingRegisters { get; } = new();
    public Dictionary<ushort, ushort> InputRegisters { get; } = new();
    public Dictionary<ushort, bool> Coils { get; } = new();
    public Dictionary<ushort, bool> DiscreteInputs { get; } = new();

    /// <summary>Exceptions thrown (one per call) by the next read requests.</summary>
    public Queue<Exception> ReadFailures { get; } = new();

    /// <summary>Exceptions thrown (one per call) by the next connect calls.</summary>
    public Queue<Exception> ConnectFailures { get; } = new();

    public List<string> Requests { get; } = new();
    public int ConnectCalls { get; private set; }

    public string Description => "Fake transport";
    public bool IsConnected { get; private set; }

    public void SetHolding(ushort address, params ushort[] words)
    {
        for (int i = 0; i < words.Length; i++)
            HoldingRegisters[(ushort)(address + i)] = words[i];
    }

    public void SetInput(ushort address, params ushort[] words)
    {
        for (int i = 0; i < words.Length; i++)
            InputRegisters[(ushort)(address + i)] = words[i];
    }

    public Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        ConnectCalls++;
        if (ConnectFailures.TryDequeue(out var failure))
            throw failure;
        IsConnected = true;
        return Task.CompletedTask;
    }

    public Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        IsConnected = false;
        return Task.CompletedTask;
    }

    public Task<ushort[]> ReadHoldingRegistersAsync(byte slaveId, ushort startAddress, ushort count, CancellationToken cancellationToken = default) =>
        Read("FC03", HoldingRegisters, startAddress, count);

    public Task<ushort[]> ReadInputRegistersAsync(byte slaveId, ushort startAddress, ushort count, CancellationToken cancellationToken = default) =>
        Read("FC04", InputRegisters, startAddress, count);

    public Task<bool[]> ReadCoilsAsync(byte slaveId, ushort startAddress, ushort count, CancellationToken cancellationToken = default) =>
        Read("FC01", Coils, startAddress, count);

    public Task<bool[]> ReadDiscreteInputsAsync(byte slaveId, ushort startAddress, ushort count, CancellationToken cancellationToken = default) =>
        Read("FC02", DiscreteInputs, startAddress, count);

    public Task WriteSingleRegisterAsync(byte slaveId, ushort address, ushort value, CancellationToken cancellationToken = default)
    {
        HoldingRegisters[address] = value;
        return Task.CompletedTask;
    }

    public Task WriteMultipleRegistersAsync(byte slaveId, ushort startAddress, ushort[] values, CancellationToken cancellationToken = default)
    {
        SetHolding(startAddress, values);
        return Task.CompletedTask;
    }

    public Task WriteSingleCoilAsync(byte slaveId, ushort address, bool value, CancellationToken cancellationToken = default)
    {
        Coils[address] = value;
        return Task.CompletedTask;
    }

    public void Dispose() => IsConnected = false;

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    private Task<T[]> Read<T>(string function, Dictionary<ushort, T> table, ushort start, ushort count)
    {
        if (!IsConnected)
            throw new DeviceConnectionException("Fake transport is not connected.");

        Requests.Add($"{function} {start} x{count}");
        if (ReadFailures.TryDequeue(out var failure))
            throw failure;

        var result = new T[count];
        for (int i = 0; i < count; i++)
            result[i] = table.TryGetValue((ushort)(start + i), out var value) ? value : default!;
        return Task.FromResult(result);
    }
}
