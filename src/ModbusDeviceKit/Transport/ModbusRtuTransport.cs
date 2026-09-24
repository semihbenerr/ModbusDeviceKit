using System.IO.Ports;
using NModbus;
using NModbus.IO;

namespace ModbusDeviceKit.Transport;

/// <summary>Modbus RTU transport over a serial port (RS-485 / RS-232 / USB-serial adapter).</summary>
public sealed class ModbusRtuTransport : ModbusTransportBase
{
    private SerialPort? _port;

    /// <summary>Creates a Modbus RTU transport.</summary>
    /// <param name="portName">Port name, e.g. <c>"COM3"</c> or <c>"/dev/ttyUSB0"</c>.</param>
    /// <param name="baudRate">Baud rate.</param>
    /// <param name="parity">Parity.</param>
    /// <param name="dataBits">Data bits (5–8).</param>
    /// <param name="stopBits">Stop bits.</param>
    /// <param name="readTimeoutMs">Response timeout in milliseconds.</param>
    /// <param name="writeTimeoutMs">Send timeout in milliseconds.</param>
    public ModbusRtuTransport(string portName, int baudRate = 9600, Parity parity = Parity.None, int dataBits = 8,
        StopBits stopBits = StopBits.One, int readTimeoutMs = 1000, int writeTimeoutMs = 1000)
        : base(readTimeoutMs, writeTimeoutMs)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(portName);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(baudRate);
        ArgumentOutOfRangeException.ThrowIfLessThan(dataBits, 5);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(dataBits, 8);
        if (stopBits == StopBits.None)
            throw new ArgumentOutOfRangeException(nameof(stopBits), stopBits, "StopBits.None is not supported by serial ports.");

        PortName = portName;
        BaudRate = baudRate;
        Parity = parity;
        DataBits = dataBits;
        StopBits = stopBits;
    }

    /// <summary>Serial port name.</summary>
    public string PortName { get; }

    /// <summary>Baud rate.</summary>
    public int BaudRate { get; }

    /// <summary>Parity.</summary>
    public Parity Parity { get; }

    /// <summary>Data bits.</summary>
    public int DataBits { get; }

    /// <summary>Stop bits.</summary>
    public StopBits StopBits { get; }

    /// <inheritdoc />
    public override string Description
    {
        get
        {
            char parity = Parity switch
            {
                Parity.Even => 'E',
                Parity.Odd => 'O',
                Parity.Mark => 'M',
                Parity.Space => 'S',
                _ => 'N',
            };
            string stop = StopBits switch
            {
                StopBits.Two => "2",
                StopBits.OnePointFive => "1.5",
                _ => "1",
            };
            return $"Modbus RTU {PortName} ({BaudRate} {DataBits}{parity}{stop})";
        }
    }

    /// <inheritdoc />
    protected override bool IsChannelOpen => _port is { IsOpen: true };

    /// <inheritdoc />
    protected override async Task<IModbusMaster> OpenChannelAsync(IModbusFactory factory, CancellationToken cancellationToken)
    {
        var port = new SerialPort(PortName, BaudRate, Parity, DataBits, StopBits)
        {
            Handshake = Handshake.None,
            ReadTimeout = ReadTimeoutMs,
            WriteTimeout = WriteTimeoutMs,
        };

        try
        {
            // SerialPort.Open is blocking (and can be slow on USB adapters).
            await Task.Run(port.Open, cancellationToken).ConfigureAwait(false);
            port.DiscardInBuffer();
            port.DiscardOutBuffer();
            _port = port;
            return factory.CreateRtuMaster(new SerialPortStreamResource(port));
        }
        catch
        {
            _port = null;
            port.Dispose();
            throw;
        }
    }

    /// <summary>
    /// A timeout or CRC error on a serial line does not mean the port is broken:
    /// keep it open and just drop any partial frame. Anything else (port removed…) reopens the port.
    /// </summary>
    protected override bool ShouldResetChannel(Exception exception) =>
        !(exception is TimeoutException or IOException && _port is { IsOpen: true });

    /// <inheritdoc />
    protected override void RecoverAfterFault() => _port?.DiscardInBuffer();

    /// <inheritdoc />
    protected override void CloseChannel()
    {
        var port = Interlocked.Exchange(ref _port, null);
        port?.Dispose();
    }

    /// <summary>Adapts <see cref="SerialPort"/> to NModbus' stream abstraction.</summary>
    private sealed class SerialPortStreamResource : IStreamResource
    {
        private readonly SerialPort _port;

        public SerialPortStreamResource(SerialPort port) => _port = port;

        public int InfiniteTimeout => SerialPort.InfiniteTimeout;

        public int ReadTimeout
        {
            get => _port.ReadTimeout;
            set => _port.ReadTimeout = value;
        }

        public int WriteTimeout
        {
            get => _port.WriteTimeout;
            set => _port.WriteTimeout = value;
        }

        public void DiscardInBuffer() => _port.DiscardInBuffer();

        public int Read(byte[] buffer, int offset, int count) => _port.Read(buffer, offset, count);

        public void Write(byte[] buffer, int offset, int count) => _port.Write(buffer, offset, count);

        public void Dispose() => _port.Dispose();
    }
}
