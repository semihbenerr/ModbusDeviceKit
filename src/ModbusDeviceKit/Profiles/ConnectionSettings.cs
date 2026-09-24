using System.IO.Ports;

namespace ModbusDeviceKit.Profiles;

/// <summary>Connection parameters stored in a profile. Used by <see cref="Transport.ModbusTransportFactory"/>.</summary>
public sealed class ConnectionSettings
{
    /// <summary>Serial line settings; required for <see cref="ModbusProtocol.ModbusRTU"/>.</summary>
    public SerialSettings? Serial { get; set; }

    /// <summary>TCP endpoint; required for <see cref="ModbusProtocol.ModbusTCP"/> and <see cref="ModbusProtocol.ModbusRtuOverTcp"/>.</summary>
    public TcpSettings? Tcp { get; set; }

    /// <summary>Maximum time to open the connection, in milliseconds. Defaults to 3000.</summary>
    public int ConnectTimeoutMs { get; set; } = 3000;

    /// <summary>Maximum time to wait for a response, in milliseconds. Defaults to 1000.</summary>
    public int ReadTimeoutMs { get; set; } = 1000;

    /// <summary>Maximum time to send a request, in milliseconds. Defaults to 1000.</summary>
    public int WriteTimeoutMs { get; set; } = 1000;
}

/// <summary>Serial port parameters for Modbus RTU.</summary>
public sealed class SerialSettings
{
    /// <summary>Port name, e.g. <c>"COM3"</c> on Windows or <c>"/dev/ttyUSB0"</c> on Linux.</summary>
    public string PortName { get; set; } = string.Empty;

    /// <summary>Baud rate. Defaults to 9600.</summary>
    public int BaudRate { get; set; } = 9600;

    /// <summary>Parity. Defaults to <see cref="System.IO.Ports.Parity.None"/>.</summary>
    public Parity Parity { get; set; } = Parity.None;

    /// <summary>Data bits (5–8). Defaults to 8.</summary>
    public int DataBits { get; set; } = 8;

    /// <summary>Stop bits. Defaults to <see cref="System.IO.Ports.StopBits.One"/>.</summary>
    public StopBits StopBits { get; set; } = StopBits.One;
}

/// <summary>TCP endpoint for Modbus TCP or RTU-over-TCP.</summary>
public sealed class TcpSettings
{
    /// <summary>Host name or IP address.</summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>TCP port. Defaults to 502.</summary>
    public int Port { get; set; } = 502;
}
