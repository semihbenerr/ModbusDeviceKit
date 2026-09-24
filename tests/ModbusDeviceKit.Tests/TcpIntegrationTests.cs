using System.Net;
using System.Net.Sockets;
using ModbusDeviceKit.Profiles;
using ModbusDeviceKit.Transport;
using NModbus;
using NModbus.Data;

namespace ModbusDeviceKit.Tests;

/// <summary>End-to-end tests against a real NModbus TCP slave on the loopback interface.</summary>
public sealed class TcpIntegrationTests : IAsyncLifetime
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly DefaultSlaveDataStore _store = new();
    private readonly CancellationTokenSource _stop = new();
    private IModbusSlaveNetwork? _network;
    private Task? _listenTask;

    private int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    public Task InitializeAsync()
    {
        _listener.Start();
        var factory = new ModbusFactory();
        _network = factory.CreateSlaveNetwork(_listener);
        _network.AddSlave(factory.CreateSlave(1, _store));
        _listenTask = _network.ListenAsync(_stop.Token);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        _stop.Cancel();
        _listener.Stop();
        (_network as IDisposable)?.Dispose();
        try
        {
            await _listenTask!;
        }
        catch (Exception)
        {
            // ListenAsync faults when the listener is stopped.
        }
    }

    private DeviceProfile CreateProfile() => DeviceProfile.FromJson($$"""
        {
          "deviceName": "SimulatedLoadCell",
          "protocol": "ModbusTCP",
          "slaveId": 1,
          "connection": { "tcp": { "host": "127.0.0.1", "port": {{Port}} }, "readTimeoutMs": 1000 },
          "retry": { "maxRetries": 1, "delayMs": 10 },
          "registers": [
            { "name": "Force", "address": 100, "dataType": "Float32", "scale": 0.01, "unit": "N", "allowTare": true },
            { "name": "Temperature", "address": 102, "dataType": "Int16", "scale": 0.1, "unit": "C" },
            { "name": "Adc", "address": 200, "registerType": "Input", "dataType": "UInt32", "byteOrder": "CDAB" },
            { "name": "Enabled", "address": 5, "registerType": "Coil", "dataType": "Bool" },
            { "name": "Overload", "address": 0, "registerType": "DiscreteInput", "dataType": "Bool" }
          ]
        }
        """);

    [Fact]
    public async Task Reads_all_register_tables_from_a_real_modbus_tcp_slave()
    {
        _store.HoldingRegisters.WritePoints(100, RegisterDecoder.Encode(98765.0, RegisterDataType.Float32));
        _store.HoldingRegisters.WritePoints(102, RegisterDecoder.Encode(215, RegisterDataType.Int16));
        _store.InputRegisters.WritePoints(200, RegisterDecoder.Encode(3_000_000_000, RegisterDataType.UInt32, ByteOrder.CDAB));
        _store.CoilDiscretes.WritePoints(5, new[] { true });
        _store.CoilInputs.WritePoints(0, new[] { true });

        await using var reader = DeviceReader.Create(CreateProfile());
        await reader.ConnectAsync();
        var reading = await reader.ReadAsync();

        Assert.Equal(987.65, reading["Force"], 2);
        Assert.Equal(21.5, reading["Temperature"], 6);
        Assert.Equal(3_000_000_000, reading["Adc"]);
        Assert.Equal(1, reading["Enabled"]);
        Assert.Equal(1, reading["Overload"]);

        await reader.ApplyTareAsync();
        _store.HoldingRegisters.WritePoints(100, RegisterDecoder.Encode(99765.0, RegisterDataType.Float32));
        Assert.Equal(10.0, await reader.ReadValueAsync("Force"), 2);
    }

    [Fact]
    public async Task Reports_modbus_exception_for_unknown_slave_as_timeout_or_slave_error()
    {
        var profile = CreateProfile();
        profile.SlaveId = 9; // no such unit on the simulated network
        await using var reader = DeviceReader.Create(profile);

        var ex = await Assert.ThrowsAnyAsync<DeviceCommunicationException>(() => reader.ReadAsync());

        Assert.Equal(9, ex.SlaveId);
        Assert.Equal(2, ex.Attempts);
    }

    [Fact]
    public async Task Throws_DeviceConnectionException_when_nothing_listens()
    {
        int freePort;
        using (var probe = new TcpListener(IPAddress.Loopback, 0))
        {
            probe.Start();
            freePort = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
        }

        var transport = new ModbusTcpTransport("127.0.0.1", freePort, connectTimeoutMs: 1000);
        var profile = CreateProfile();
        await using var reader = new DeviceReader(profile, transport, ownsTransport: true);

        var ex = await Assert.ThrowsAsync<DeviceConnectionException>(() => reader.ReadAsync());

        Assert.Equal(2, ex.Attempts);
        Assert.False(reader.IsConnected);
    }
}
