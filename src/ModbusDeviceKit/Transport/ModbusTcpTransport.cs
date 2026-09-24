using System.Net.Sockets;
using NModbus;

namespace ModbusDeviceKit.Transport;

/// <summary>Modbus TCP transport (MBAP framing over a TCP socket).</summary>
public class ModbusTcpTransport : ModbusTransportBase
{
    private TcpClient? _client;

    /// <summary>Creates a Modbus TCP transport.</summary>
    /// <param name="host">Host name or IP address of the device or gateway.</param>
    /// <param name="port">TCP port (default 502).</param>
    /// <param name="connectTimeoutMs">Connection timeout in milliseconds.</param>
    /// <param name="readTimeoutMs">Response timeout in milliseconds.</param>
    /// <param name="writeTimeoutMs">Send timeout in milliseconds.</param>
    public ModbusTcpTransport(string host, int port = 502, int connectTimeoutMs = 3000, int readTimeoutMs = 1000, int writeTimeoutMs = 1000)
        : base(readTimeoutMs, writeTimeoutMs)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        ArgumentOutOfRangeException.ThrowIfLessThan(port, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(port, 65535);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(connectTimeoutMs);
        Host = host;
        Port = port;
        ConnectTimeoutMs = connectTimeoutMs;
    }

    /// <summary>Host name or IP address.</summary>
    public string Host { get; }

    /// <summary>TCP port.</summary>
    public int Port { get; }

    /// <summary>Connection timeout in milliseconds.</summary>
    public int ConnectTimeoutMs { get; }

    /// <inheritdoc />
    public override string Description => $"Modbus TCP {Host}:{Port}";

    /// <inheritdoc />
    protected override bool IsChannelOpen => _client is { Connected: true };

    /// <inheritdoc />
    protected override async Task<IModbusMaster> OpenChannelAsync(IModbusFactory factory, CancellationToken cancellationToken)
    {
        var client = new TcpClient { NoDelay = true };
        try
        {
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                timeout.CancelAfter(ConnectTimeoutMs);
                try
                {
                    await client.ConnectAsync(Host, Port, timeout.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    throw new TimeoutException($"Connection to {Host}:{Port} timed out after {ConnectTimeoutMs} ms.");
                }
            }

            _client = client;
            return CreateMaster(factory, client);
        }
        catch
        {
            _client = null;
            client.Dispose();
            throw;
        }
    }

    /// <summary>Creates the NModbus master for a connected socket. Override to change the framing.</summary>
    protected virtual IModbusMaster CreateMaster(IModbusFactory factory, TcpClient client) => factory.CreateMaster(client);

    /// <inheritdoc />
    protected override void CloseChannel()
    {
        var client = Interlocked.Exchange(ref _client, null);
        client?.Dispose();
    }
}
