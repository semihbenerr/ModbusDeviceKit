using ModbusDeviceKit.Profiles;

namespace ModbusDeviceKit.Tests;

public class DeviceProfileTests
{
    private const string MinimalJson = """
        {
          "deviceName": "LoadCell_XYZ123",
          "protocol": "ModbusRTU",
          "slaveId": 1,
          "registers": [
            { "name": "Force", "address": 100, "dataType": "Float32",
              "scale": 0.01, "unit": "N" },
            { "name": "Temperature", "address": 102, "dataType": "Int16",
              "scale": 0.1, "unit": "C" }
          ]
        }
        """;

    [Fact]
    public void Parses_the_minimal_profile_with_defaults()
    {
        var profile = DeviceProfile.FromJson(MinimalJson);

        Assert.Equal("LoadCell_XYZ123", profile.DeviceName);
        Assert.Equal(ModbusProtocol.ModbusRTU, profile.Protocol);
        Assert.Equal(1, profile.SlaveId);
        Assert.Equal(2, profile.Registers.Count);

        var force = profile.GetRegister("force");
        Assert.Equal(100, force.Address);
        Assert.Equal(RegisterDataType.Float32, force.DataType);
        Assert.Equal(RegisterType.Holding, force.RegisterType);
        Assert.Equal(0.01, force.Scale);
        Assert.Equal("N", force.Unit);
        Assert.Equal(2, force.RegisterCount);
        Assert.Equal(3, profile.Retry.MaxRetries);
    }

    [Fact]
    public void Loads_the_sample_profile_file()
    {
        var profile = DeviceProfile.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "device-profile.json"));

        Assert.Equal("LoadCell_XYZ123", profile.DeviceName);
        Assert.Equal("COM3", profile.Connection.Serial!.PortName);
        Assert.True(profile.GetRegister("Force").AllowTare);
        Assert.Equal(ByteOrder.CDAB, profile.GetRegister("AdcCounts").ByteOrder);
        Assert.Equal(RegisterType.DiscreteInput, profile.GetRegister("Overload").RegisterType);
    }

    [Fact]
    public void Enum_values_are_case_insensitive()
    {
        var profile = DeviceProfile.FromJson("""
            { "deviceName": "D", "protocol": "modbustcp", "slaveId": 1,
              "registers": [ { "name": "A", "address": 0, "dataType": "float32", "registerType": "input" } ] }
            """);

        Assert.Equal(ModbusProtocol.ModbusTCP, profile.Protocol);
        Assert.Equal(RegisterType.Input, profile.Registers[0].RegisterType);
    }

    [Fact]
    public void Unknown_properties_are_rejected()
    {
        var ex = Assert.Throws<DeviceProfileException>(() => DeviceProfile.FromJson("""
            { "deviceName": "D", "slaveId": 1,
              "registers": [ { "name": "A", "address": 0, "scael": 0.1 } ] }
            """));

        Assert.Contains("scael", ex.Message);
    }

    [Fact]
    public void Reports_all_validation_errors()
    {
        var ex = Assert.Throws<DeviceProfileException>(() => DeviceProfile.FromJson("""
            { "deviceName": "", "protocol": "ModbusRTU", "slaveId": 0,
              "registers": [
                { "name": "A", "address": 0, "dataType": "Bool" },
                { "name": "a", "address": 65535, "dataType": "Float32", "scale": 0 },
                { "name": "C", "address": 0, "registerType": "Coil", "dataType": "Int16" }
              ] }
            """));

        Assert.Contains(ex.Errors, e => e.Contains("deviceName"));
        Assert.Contains(ex.Errors, e => e.Contains("slaveId"));
        Assert.Contains(ex.Errors, e => e.Contains("'Bool' is only valid"));
        Assert.Contains(ex.Errors, e => e.Contains("duplicate"));
        Assert.Contains(ex.Errors, e => e.Contains("address space"));
        Assert.Contains(ex.Errors, e => e.Contains("'scale'"));
        Assert.Contains(ex.Errors, e => e.Contains("must use dataType 'Bool'"));
    }

    [Fact]
    public void Round_trips_through_json()
    {
        var profile = DeviceProfile.FromJson(MinimalJson);
        profile.Registers[0].AllowTare = true;
        profile.Registers[0].ByteOrder = ByteOrder.CDAB;

        var copy = DeviceProfile.FromJson(profile.ToJson());

        Assert.True(copy.Registers[0].AllowTare);
        Assert.Equal(ByteOrder.CDAB, copy.Registers[0].ByteOrder);
        Assert.Equal(profile.Registers[1].Scale, copy.Registers[1].Scale);
    }

    [Fact]
    public void Transport_factory_requires_connection_settings()
    {
        var profile = DeviceProfile.FromJson(MinimalJson);

        Assert.Throws<DeviceProfileException>(() => Transport.ModbusTransportFactory.Create(profile));

        profile.Protocol = ModbusProtocol.ModbusTCP;
        profile.Connection.Tcp = new TcpSettings { Host = "127.0.0.1", Port = 1502 };
        using var transport = Transport.ModbusTransportFactory.Create(profile);
        Assert.IsType<Transport.ModbusTcpTransport>(transport);
        Assert.False(transport.IsConnected);
    }
}
