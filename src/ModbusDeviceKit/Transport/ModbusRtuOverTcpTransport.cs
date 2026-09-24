using System.Net.Sockets;
using NModbus;
using NModbus.IO;

namespace ModbusDeviceKit.Transport;

/// <summary>
/// Modbus RTU frames (with CRC) sent over a raw TCP socket — for serial-to-Ethernet
/// converters (Moxa, USR, Waveshare…) configured in transparent / "TCP server" mode.
/// </summary>
public sealed class ModbusRtuOverTcpTransport : ModbusTcpTransport
{
    /// <summary>Creates an RTU-over-TCP transport.</summary>
    public ModbusRtuOverTcpTransport(string host, int port, int connectTimeoutMs = 3000, int readTimeoutMs = 1000, int writeTimeoutMs = 1000)
        : base(host, port, connectTimeoutMs, readTimeoutMs, writeTimeoutMs)
    {
    }

    /// <inheritdoc />
    public override string Description => $"Modbus RTU over TCP {Host}:{Port}";

    /// <inheritdoc />
    protected override IModbusMaster CreateMaster(IModbusFactory factory, TcpClient client) =>
        factory.CreateRtuMaster(new TcpClientAdapter(client));
}
