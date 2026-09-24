using System.Net;
using System.Net.Sockets;
using ModbusDeviceKit.Profiles;
using NModbus;
using NModbus.Data;

namespace ModbusDeviceKit.ConsoleSample;

/// <summary>
/// In-process Modbus TCP slave that serves plausible, slowly changing values for every register of a profile.
/// Lets the sample run without hardware (<c>--simulate</c>).
/// </summary>
internal sealed class ProfileSimulator : IAsyncDisposable
{
    private readonly DeviceProfile _profile;
    private readonly TcpListener _listener;
    private readonly IModbusSlaveNetwork _network;
    private readonly DefaultSlaveDataStore _store = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly Random _noise = new();
    private readonly DateTime _startedAt = DateTime.UtcNow;
    private readonly Task _listenTask;
    private readonly Task _updateTask;

    private ProfileSimulator(DeviceProfile profile, int port)
    {
        _profile = profile;
        _listener = new TcpListener(IPAddress.Loopback, port);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;

        var factory = new ModbusFactory();
        _network = factory.CreateSlaveNetwork(_listener);
        _network.AddSlave(factory.CreateSlave(profile.SlaveId, _store));

        UpdateValues();
        _listenTask = _network.ListenAsync(_stop.Token);
        _updateTask = UpdateLoopAsync(_stop.Token);
    }

    /// <summary>TCP port the simulator listens on (127.0.0.1).</summary>
    public int Port { get; }

    /// <summary>Starts a simulator for <paramref name="profile"/> on 127.0.0.1:<paramref name="port"/> (0 = any free port).</summary>
    public static ProfileSimulator Start(DeviceProfile profile, int port = 0) => new(profile, port);

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        _listener.Stop(); // unblocks the pending AcceptTcpClientAsync inside ListenAsync
        (_network as IDisposable)?.Dispose();

        try
        {
            await Task.WhenAll(_listenTask, _updateTask).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Stopping the listener faults ListenAsync with ObjectDisposed/Socket exceptions; that is expected here.
        }

        _stop.Dispose();
    }

    private async Task UpdateLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(200));
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
                UpdateValues();
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void UpdateValues()
    {
        double t = (DateTime.UtcNow - _startedAt).TotalSeconds;

        foreach (var register in _profile.Registers)
        {
            switch (register.RegisterType)
            {
                case RegisterType.Coil:
                    _store.CoilDiscretes.WritePoints(register.Address, new[] { SimulateBit(t) });
                    continue;
                case RegisterType.DiscreteInput:
                    _store.CoilInputs.WritePoints(register.Address, new[] { SimulateBit(t) });
                    continue;
            }

            double engineering = SimulateEngineeringValue(register, t);
            double raw = Clamp((engineering - register.Offset) / register.Scale, register.DataType);
            ushort[] words = RegisterDecoder.Encode(raw, register.DataType, register.GetEffectiveByteOrder(_profile));

            var table = register.RegisterType == RegisterType.Holding ? _store.HoldingRegisters : _store.InputRegisters;
            table.WritePoints(register.Address, words);
        }
    }

    // Pick a realistic value range from the engineering unit.
    private double SimulateEngineeringValue(RegisterDefinition register, double t)
    {
        double wave = Math.Sin(2 * Math.PI * t / 30);
        double noise = _noise.NextDouble() - 0.5;

        return register.Unit.Trim().ToLowerInvariant() switch
        {
            "n" => 1250 + 400 * wave + 2 * noise,
            "kn" => 1.25 + 0.4 * wave + 0.002 * noise,
            "kg" => 127 + 40 * wave + 0.2 * noise,
            "nm" => 35 + 10 * wave + 0.1 * noise,
            "c" or "°c" or "degc" => 24.5 + 0.8 * wave + 0.1 * noise,
            "bar" => 6 + 0.5 * wave + 0.01 * noise,
            "counts" => 800_000 + 250_000 * wave + 200 * noise,
            _ => register.DataType is RegisterDataType.UInt16 ? 1 : 100 + 10 * wave,
        };
    }

    private static bool SimulateBit(double t) => (int)(t / 20) % 2 == 1;

    private static double Clamp(double raw, RegisterDataType dataType) => dataType switch
    {
        RegisterDataType.Int16 => Math.Clamp(raw, short.MinValue, short.MaxValue),
        RegisterDataType.UInt16 => Math.Clamp(raw, ushort.MinValue, ushort.MaxValue),
        RegisterDataType.Int32 => Math.Clamp(raw, int.MinValue, int.MaxValue),
        RegisterDataType.UInt32 => Math.Clamp(raw, uint.MinValue, uint.MaxValue),
        RegisterDataType.Int64 => Math.Clamp(raw, -9.2e18, 9.2e18),
        RegisterDataType.UInt64 => Math.Clamp(raw, 0, 1.8e19),
        _ => raw,
    };
}
