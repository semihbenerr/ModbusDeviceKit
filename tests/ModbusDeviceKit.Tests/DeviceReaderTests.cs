using ModbusDeviceKit.Profiles;

namespace ModbusDeviceKit.Tests;

public class DeviceReaderTests
{
    private static DeviceProfile CreateProfile(int maxRetries = 2) => DeviceProfile.FromJson($$"""
        {
          "deviceName": "LoadCell_XYZ123",
          "protocol": "ModbusRTU",
          "slaveId": 7,
          "retry": { "maxRetries": {{maxRetries}}, "delayMs": 0 },
          "registers": [
            { "name": "Force", "address": 100, "dataType": "Float32", "scale": 0.01, "unit": "N", "allowTare": true },
            { "name": "Temperature", "address": 102, "dataType": "Int16", "scale": 0.1, "offset": -0.5, "unit": "C" },
            { "name": "Adc", "address": 10, "registerType": "Input", "dataType": "Int32", "byteOrder": "CDAB" },
            { "name": "Overload", "address": 3, "registerType": "DiscreteInput", "dataType": "Bool" }
          ]
        }
        """);

    private static FakeModbusTransport CreateTransport()
    {
        var transport = new FakeModbusTransport();
        transport.SetHolding(100, RegisterDecoder.Encode(12345.0, RegisterDataType.Float32)); // → 123.45 N
        transport.SetHolding(102, RegisterDecoder.Encode(-123, RegisterDataType.Int16));      // → -12.3 - 0.5 = -12.8 C
        transport.SetInput(10, RegisterDecoder.Encode(-70000, RegisterDataType.Int32, ByteOrder.CDAB));
        transport.DiscreteInputs[3] = true;
        return transport;
    }

    [Fact]
    public async Task Reads_all_registers_scaled_in_as_few_requests_as_possible()
    {
        var transport = CreateTransport();
        await using var reader = new DeviceReader(CreateProfile(), transport);

        var reading = await reader.ReadAsync();

        Assert.Equal(123.45, reading["Force"], 3);
        Assert.Equal(-12.8, reading["temperature"], 6);
        Assert.Equal(-70000, reading["Adc"]);
        Assert.True(reading.GetRegister("Overload").AsBoolean);
        Assert.Equal("N", reading.GetRegister("Force").Unit);
        Assert.Equal(new[] { "Force", "Temperature", "Adc", "Overload" }, reading.Keys);

        // Force + Temperature are adjacent → one FC03 request; plus FC04 and FC02.
        Assert.Equal(new[] { "FC03 100 x3", "FC04 10 x2", "FC02 3 x1" }, transport.Requests);

        var dictionary = reading.ToDictionary();
        Assert.Equal(4, dictionary.Count);
        Assert.Same(reading, reader.LastReading);
    }

    [Fact]
    public async Task Connects_automatically_on_first_read()
    {
        var transport = CreateTransport();
        await using var reader = new DeviceReader(CreateProfile(), transport);

        Assert.False(reader.IsConnected);
        await reader.ReadAsync();

        Assert.True(reader.IsConnected);
        Assert.Equal(1, transport.ConnectCalls);
    }

    [Fact]
    public async Task Retries_transient_failures_and_then_succeeds()
    {
        var transport = CreateTransport();
        transport.ReadFailures.Enqueue(new TimeoutException("no answer"));
        transport.ReadFailures.Enqueue(new DeviceCommunicationException("CRC error", 7));
        await using var reader = new DeviceReader(CreateProfile(maxRetries: 2), transport);

        var reading = await reader.ReadAsync();

        Assert.Equal(123.45, reading["Force"], 3);
        Assert.Equal(3, transport.Requests.Count(r => r.StartsWith("FC03")));
    }

    [Fact]
    public async Task Throws_DeviceTimeoutException_when_all_attempts_time_out()
    {
        var transport = CreateTransport();
        for (int i = 0; i < 10; i++)
            transport.ReadFailures.Enqueue(new DeviceTimeoutException("no answer", 7));
        await using var reader = new DeviceReader(CreateProfile(maxRetries: 2), transport);

        var ex = await Assert.ThrowsAsync<DeviceTimeoutException>(() => reader.ReadAsync());

        Assert.Equal(3, ex.Attempts);
        Assert.Equal("LoadCell_XYZ123", ex.DeviceName);
        Assert.Equal(7, ex.SlaveId);
        Assert.Contains("did not respond", ex.Message);
        Assert.Equal(3, transport.Requests.Count);
        Assert.Null(reader.LastReading);
    }

    [Fact]
    public async Task Does_not_retry_when_the_device_rejects_the_request()
    {
        var transport = CreateTransport();
        transport.ReadFailures.Enqueue(new DeviceSlaveException("illegal address", 7, 3, 2));
        await using var reader = new DeviceReader(CreateProfile(maxRetries: 3), transport);

        var ex = await Assert.ThrowsAsync<DeviceSlaveException>(() => reader.ReadAsync());

        Assert.Equal(2, ex.ExceptionCode);
        Assert.Equal("Illegal Data Address", ex.ExceptionName);
        Assert.Equal(1, ex.Attempts);
        Assert.Single(transport.Requests);
    }

    [Fact]
    public async Task Retries_when_the_slave_is_busy()
    {
        var transport = CreateTransport();
        transport.ReadFailures.Enqueue(new DeviceSlaveException("busy", 7, 3, 6));
        await using var reader = new DeviceReader(CreateProfile(maxRetries: 1), transport);

        var reading = await reader.ReadAsync();

        Assert.Equal(123.45, reading["Force"], 3);
    }

    [Fact]
    public async Task Throws_DeviceConnectionException_when_connection_cannot_be_opened()
    {
        var transport = CreateTransport();
        for (int i = 0; i < 5; i++)
            transport.ConnectFailures.Enqueue(new DeviceConnectionException("port busy"));
        await using var reader = new DeviceReader(CreateProfile(maxRetries: 2), transport);

        var ex = await Assert.ThrowsAsync<DeviceConnectionException>(() => reader.ConnectAsync());

        Assert.Equal(3, ex.Attempts);
        Assert.Equal(3, transport.ConnectCalls);
    }

    [Fact]
    public async Task Reconnects_when_the_connection_was_lost()
    {
        var transport = CreateTransport();
        await using var reader = new DeviceReader(CreateProfile(), transport);
        await reader.ConnectAsync();

        await transport.DisconnectAsync();
        var reading = await reader.ReadAsync();

        Assert.Equal(2, transport.ConnectCalls);
        Assert.Equal(123.45, reading["Force"], 3);
    }

    [Fact]
    public async Task ApplyTareAsync_zeroes_tareable_registers_only()
    {
        var transport = CreateTransport();
        await using var reader = new DeviceReader(CreateProfile(), transport);

        var tares = await reader.ApplyTareAsync(samples: 3);
        Assert.Equal(123.45, tares["Force"], 3);
        Assert.Equal(3, transport.Requests.Count); // only the Force block, three times

        transport.SetHolding(100, RegisterDecoder.Encode(12500.0, RegisterDataType.Float32));
        var reading = await reader.ReadAsync();

        var force = reading.GetRegister("Force");
        Assert.Equal(1.55, force.Value, 3);
        Assert.Equal(125.0, force.CalibratedValue, 3);
        Assert.Equal(123.45, force.Tare, 3);
        Assert.Equal(-12.8, reading["Temperature"], 6); // not tared

        reader.ClearAllTares();
        Assert.Equal(125.0, (await reader.ReadAsync())["Force"], 3);
    }

    [Fact]
    public async Task ApplyTare_uses_the_last_reading_and_supports_single_registers()
    {
        var transport = CreateTransport();
        await using var reader = new DeviceReader(CreateProfile(), transport);

        Assert.Throws<InvalidOperationException>(() => reader.ApplyTare());

        await reader.ReadAsync();
        reader.ApplyTare();
        reader.ApplyTare("Temperature");
        Assert.Throws<InvalidOperationException>(() => reader.ApplyTare("Overload"));
        Assert.Throws<ArgumentException>(() => reader.ApplyTare("Missing"));

        var reading = await reader.ReadAsync();
        Assert.Equal(0, reading["Force"], 6);
        Assert.Equal(0, reading["Temperature"], 6);

        reader.SetTare("Force", 100);
        Assert.Equal(100, reader.GetTare("force"));
        Assert.Equal(23.45, await reader.ReadValueAsync("Force"), 3);

        reader.ClearTare("Temperature");
        Assert.Equal(0, reader.GetTare("Temperature"));
    }

    [Fact]
    public async Task Initial_tare_and_offset_come_from_the_profile()
    {
        var profile = CreateProfile();
        profile.GetRegister("Force").Tare = 23.45;
        profile.GetRegister("Force").Offset = 1;
        var transport = CreateTransport();
        await using var reader = new DeviceReader(profile, transport);

        Assert.Equal(101, await reader.ReadValueAsync("Force"), 3);
    }

    [Fact]
    public async Task Reading_a_subset_only_requests_what_is_needed()
    {
        var transport = CreateTransport();
        await using var reader = new DeviceReader(CreateProfile(), transport);

        var reading = await reader.ReadAsync(new[] { "Adc" });

        Assert.Single(reading.Registers);
        Assert.Equal(new[] { "FC04 10 x2" }, transport.Requests);
        Assert.Null(reader.LastReading);
    }

    [Fact]
    public void Rejects_invalid_profiles()
    {
        var profile = CreateProfile();
        profile.Registers.Add(new RegisterDefinition { Name = "Force", Address = 1 });

        Assert.Throws<DeviceProfileException>(() => new DeviceReader(profile, new FakeModbusTransport()));
    }
}
