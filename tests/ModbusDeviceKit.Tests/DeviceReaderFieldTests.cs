using ModbusDeviceKit.Profiles;

namespace ModbusDeviceKit.Tests;

/// <summary>Partial reads, rejected-block splitting, writes and probing.</summary>
public class DeviceReaderFieldTests
{
    private static DeviceProfile CreateProfile(bool partialReads = false, bool splitRejectedBlocks = true) => DeviceProfile.FromJson($$"""
        {
          "deviceName": "Transmitter",
          "slaveId": 3,
          "retry": { "maxRetries": 1, "delayMs": 0 },
          "readOptions": { "partialReads": {{(partialReads ? "true" : "false")}}, "splitRejectedBlocks": {{(splitRejectedBlocks ? "true" : "false")}} },
          "registers": [
            { "name": "Pressure", "address": 0, "dataType": "Float32", "unit": "bar" },
            { "name": "Temperature", "address": 2, "dataType": "Int16", "scale": 0.1, "unit": "C" },
            { "name": "Counter", "address": 10, "registerType": "Input", "dataType": "UInt32" },
            { "name": "Setpoint", "address": 20, "dataType": "Int16", "scale": 0.1, "offset": 5, "unit": "bar", "writable": true },
            { "name": "Limit", "address": 22, "dataType": "Float32", "byteOrder": "CDAB", "writable": true },
            { "name": "Relay", "address": 4, "registerType": "Coil", "dataType": "Bool", "writable": true }
          ]
        }
        """);

    private static FakeModbusTransport CreateTransport()
    {
        var transport = new FakeModbusTransport();
        transport.SetHolding(0, RegisterDecoder.Encode(6.25, RegisterDataType.Float32));
        transport.SetHolding(2, RegisterDecoder.Encode(215, RegisterDataType.Int16));
        transport.SetInput(10, RegisterDecoder.Encode(123456, RegisterDataType.UInt32));
        return transport;
    }

    [Fact]
    public async Task Partial_reads_mark_only_the_failed_request_invalid()
    {
        var transport = CreateTransport();
        transport.ReadInterceptor = (fc, _, _) => fc == "FC04" ? new DeviceTimeoutException("no answer", 3) : null;
        await using var reader = new DeviceReader(CreateProfile(partialReads: true), transport);

        var reading = await reader.ReadAsync();

        Assert.False(reading.IsComplete);
        var counter = Assert.Single(reading.FailedRegisters);
        Assert.Equal("Counter", counter.Name);
        Assert.True(double.IsNaN(reading["Counter"]));
        Assert.Contains("did not respond", counter.Error);
        Assert.Equal(6.25, reading["Pressure"]);
        Assert.Equal(21.5, reading["Temperature"], 6);
        Assert.Same(reading, reader.LastReading);
    }

    [Fact]
    public async Task Partial_reads_still_throw_when_nothing_can_be_read()
    {
        var transport = CreateTransport();
        transport.ReadInterceptor = (_, _, _) => new TimeoutException("silent");
        await using var reader = new DeviceReader(CreateProfile(partialReads: true), transport);

        await Assert.ThrowsAsync<DeviceTimeoutException>(() => reader.ReadAsync());
    }

    [Fact]
    public async Task Without_partial_reads_any_failure_throws()
    {
        var transport = CreateTransport();
        transport.ReadInterceptor = (fc, _, _) => fc == "FC04" ? new DeviceTimeoutException("no answer", 3) : null;
        await using var reader = new DeviceReader(CreateProfile(), transport);

        await Assert.ThrowsAsync<DeviceTimeoutException>(() => reader.ReadAsync());
    }

    [Fact]
    public async Task Tare_is_refused_for_a_register_that_was_not_read()
    {
        var transport = CreateTransport();
        transport.ReadInterceptor = (fc, _, _) => fc == "FC04" ? new DeviceTimeoutException("no answer", 3) : null;
        await using var reader = new DeviceReader(CreateProfile(partialReads: true), transport);
        await reader.ReadAsync();

        Assert.Throws<InvalidOperationException>(() => reader.ApplyTare("Counter"));
        reader.ApplyTare("Pressure");
        Assert.Equal(6.25, reader.GetTare("Pressure"));
    }

    [Fact]
    public async Task Rejected_block_is_split_into_single_requests_and_remembered()
    {
        var transport = CreateTransport();
        // The device serves single values but refuses the combined 0..2 range.
        transport.ReadInterceptor = (fc, start, count) =>
            fc == "FC03" && start == 0 && count == 3 ? new DeviceSlaveException("illegal address", 3, 3, 2) : null;
        await using var reader = new DeviceReader(CreateProfile(), transport);

        var first = await reader.ReadAsync();
        transport.Requests.Clear();
        var second = await reader.ReadAsync();

        Assert.Equal(6.25, first["Pressure"]);
        Assert.Equal(21.5, first["Temperature"], 6);
        Assert.Equal(21.5, second["Temperature"], 6);
        // Second cycle goes straight to single requests: no retry of the rejected range.
        Assert.DoesNotContain("FC03 0 x3", transport.Requests);
        Assert.Contains("FC03 0 x2", transport.Requests);
        Assert.Contains("FC03 2 x1", transport.Requests);
    }

    [Fact]
    public async Task Rejected_block_throws_when_splitting_is_disabled()
    {
        var transport = CreateTransport();
        transport.ReadInterceptor = (fc, start, count) =>
            fc == "FC03" && start == 0 && count == 3 ? new DeviceSlaveException("illegal address", 3, 3, 2) : null;
        await using var reader = new DeviceReader(CreateProfile(splitRejectedBlocks: false), transport);

        var ex = await Assert.ThrowsAsync<DeviceSlaveException>(() => reader.ReadAsync());
        Assert.Equal(2, ex.ExceptionCode);
    }

    [Fact]
    public async Task Split_block_with_partial_reads_isolates_the_bad_register()
    {
        var transport = CreateTransport();
        transport.ReadInterceptor = (fc, start, _) =>
            fc == "FC03" && start is 0 ? new DeviceSlaveException("illegal address", 3, 3, 2) : null;
        await using var reader = new DeviceReader(CreateProfile(partialReads: true), transport);

        var reading = await reader.ReadAsync();

        Assert.False(reading.GetRegister("Pressure").IsValid);
        Assert.Equal(21.5, reading["Temperature"], 6);
        Assert.Equal(123456, reading["Counter"]);
    }

    [Fact]
    public async Task WriteAsync_converts_engineering_values_to_raw_registers()
    {
        var transport = CreateTransport();
        await using var reader = new DeviceReader(CreateProfile(), transport);

        await reader.WriteAsync("Setpoint", 17.5);   // raw = (17.5 - 5) / 0.1 = 125
        await reader.WriteAsync("Limit", 99.5);
        await reader.WriteAsync("Relay", 1);

        Assert.Equal(new[] { "FC06 20", "FC16 22 x2", "FC05 4" }, transport.Writes);
        Assert.Equal(125, transport.HoldingRegisters[20]);
        Assert.Equal(99.5, RegisterDecoder.Decode(new[] { transport.HoldingRegisters[22], transport.HoldingRegisters[23] },
            RegisterDataType.Float32, ByteOrder.CDAB));
        Assert.True(transport.Coils[4]);
        Assert.Equal(17.5, await reader.ReadValueAsync("Setpoint"), 6);
    }

    [Fact]
    public async Task WriteAsync_rejects_non_writable_registers_and_out_of_range_values()
    {
        var transport = CreateTransport();
        await using var reader = new DeviceReader(CreateProfile(), transport);

        await Assert.ThrowsAsync<InvalidOperationException>(() => reader.WriteAsync("Pressure", 1));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => reader.WriteAsync("Setpoint", 100_000));
        await Assert.ThrowsAsync<ArgumentException>(() => reader.WriteAsync("Missing", 1));
        Assert.Empty(transport.Writes);
    }

    [Fact]
    public void Read_only_registers_cannot_be_writable()
    {
        var ex = Assert.Throws<DeviceProfileException>(() => DeviceProfile.FromJson("""
            { "deviceName": "D", "registers": [ { "name": "A", "address": 0, "registerType": "Input", "writable": true } ] }
            """));

        Assert.Contains(ex.Errors, e => e.Contains("read-only"));
    }

    [Fact]
    public async Task ProbeAsync_reports_answer_rejection_and_silence()
    {
        var transport = CreateTransport();
        await using var reader = new DeviceReader(CreateProfile(), transport);

        var ok = await reader.ProbeAsync();
        Assert.True(ok.Responded);
        Assert.True(ok.AddressAccepted);

        transport.ReadInterceptor = (_, _, _) => new DeviceSlaveException("illegal address", 3, 3, 2);
        var rejected = await reader.ProbeAsync();
        Assert.True(rejected.Responded);
        Assert.False(rejected.AddressAccepted);
        Assert.Equal((byte)2, rejected.ExceptionCode);
        Assert.Contains("online", rejected.Message);

        transport.ReadInterceptor = (_, _, _) => new TimeoutException("silent");
        var silent = await reader.ProbeAsync();
        Assert.False(silent.Responded);
        Assert.Contains("wiring", silent.Message);
        Assert.IsType<DeviceTimeoutException>(silent.Error);
    }
}
