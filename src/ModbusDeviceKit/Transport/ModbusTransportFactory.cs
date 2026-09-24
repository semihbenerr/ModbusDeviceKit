using ModbusDeviceKit.Profiles;

namespace ModbusDeviceKit.Transport;

/// <summary>Creates the right <see cref="IModbusTransport"/> from a profile's protocol and connection settings.</summary>
public static class ModbusTransportFactory
{
    /// <summary>Creates a (not yet connected) transport for <paramref name="profile"/>.</summary>
    /// <exception cref="DeviceProfileException">The connection section required by the protocol is missing or invalid.</exception>
    public static IModbusTransport Create(DeviceProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var connection = profile.Connection ?? new ConnectionSettings();
        string name = string.IsNullOrWhiteSpace(profile.DeviceName) ? "<unnamed>" : profile.DeviceName;

        switch (profile.Protocol)
        {
            case ModbusProtocol.ModbusRTU:
            {
                var serial = connection.Serial;
                if (serial is null || string.IsNullOrWhiteSpace(serial.PortName))
                    throw new DeviceProfileException($"Profile '{name}' uses ModbusRTU but 'connection.serial.portName' is missing.");

                return Guard(name, () => new ModbusRtuTransport(serial.PortName, serial.BaudRate, serial.Parity, serial.DataBits,
                    serial.StopBits, connection.ReadTimeoutMs, connection.WriteTimeoutMs));
            }

            case ModbusProtocol.ModbusTCP:
            case ModbusProtocol.ModbusRtuOverTcp:
            {
                var tcp = connection.Tcp;
                if (tcp is null || string.IsNullOrWhiteSpace(tcp.Host))
                    throw new DeviceProfileException($"Profile '{name}' uses {profile.Protocol} but 'connection.tcp.host' is missing.");

                return profile.Protocol == ModbusProtocol.ModbusTCP
                    ? Guard(name, () => new ModbusTcpTransport(tcp.Host, tcp.Port, connection.ConnectTimeoutMs,
                        connection.ReadTimeoutMs, connection.WriteTimeoutMs))
                    : Guard(name, () => new ModbusRtuOverTcpTransport(tcp.Host, tcp.Port, connection.ConnectTimeoutMs,
                        connection.ReadTimeoutMs, connection.WriteTimeoutMs));
            }

            default:
                throw new DeviceProfileException($"Profile '{name}': protocol {profile.Protocol} is not supported.");
        }
    }

    private static IModbusTransport Guard(string profileName, Func<IModbusTransport> create)
    {
        try
        {
            return create();
        }
        catch (ArgumentException ex)
        {
            throw new DeviceProfileException($"Profile '{profileName}' has invalid connection settings: {ex.Message}", ex);
        }
    }
}
