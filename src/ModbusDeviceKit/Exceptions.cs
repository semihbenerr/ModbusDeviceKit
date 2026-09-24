namespace ModbusDeviceKit;

/// <summary>Base class of every exception thrown by ModbusDeviceKit.</summary>
public class ModbusDeviceKitException : Exception
{
    /// <summary>Creates the exception.</summary>
    public ModbusDeviceKitException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

/// <summary>A device profile is malformed or contains invalid values.</summary>
public sealed class DeviceProfileException : ModbusDeviceKitException
{
    /// <summary>Creates the exception for a single problem.</summary>
    public DeviceProfileException(string message, Exception? innerException = null)
        : this(message, new[] { message }, innerException)
    {
    }

    /// <summary>Creates the exception with the complete list of validation errors.</summary>
    public DeviceProfileException(string message, IReadOnlyList<string> errors, Exception? innerException = null)
        : base(message, innerException)
    {
        Errors = errors ?? Array.Empty<string>();
    }

    /// <summary>Individual validation errors.</summary>
    public IReadOnlyList<string> Errors { get; }
}

/// <summary>The connection (serial port or TCP socket) could not be opened or is not open.</summary>
public class DeviceConnectionException : ModbusDeviceKitException
{
    /// <summary>Creates the exception.</summary>
    public DeviceConnectionException(string message, string? endpoint = null, int attempts = 1, Exception? innerException = null)
        : base(message, innerException)
    {
        Endpoint = endpoint;
        Attempts = attempts;
    }

    /// <summary>Description of the endpoint, e.g. <c>"Modbus TCP 192.168.1.50:502"</c>.</summary>
    public string? Endpoint { get; }

    /// <summary>Number of attempts made before giving up.</summary>
    public int Attempts { get; }
}

/// <summary>A request to the device failed (CRC error, malformed response, I/O error…).</summary>
public class DeviceCommunicationException : ModbusDeviceKitException
{
    /// <summary>Creates the exception.</summary>
    public DeviceCommunicationException(string message, byte slaveId, string? deviceName = null, int attempts = 1, Exception? innerException = null)
        : base(message, innerException)
    {
        SlaveId = slaveId;
        DeviceName = deviceName;
        Attempts = attempts;
    }

    /// <summary>Device name from the profile, when known.</summary>
    public string? DeviceName { get; }

    /// <summary>Modbus slave / unit id of the request.</summary>
    public byte SlaveId { get; }

    /// <summary>Number of attempts made before giving up.</summary>
    public int Attempts { get; }
}

/// <summary>The device did not answer within the configured timeout (after all retries, when thrown by <see cref="DeviceReader"/>).</summary>
public sealed class DeviceTimeoutException : DeviceCommunicationException
{
    /// <summary>Creates the exception.</summary>
    public DeviceTimeoutException(string message, byte slaveId, string? deviceName = null, int attempts = 1, Exception? innerException = null)
        : base(message, slaveId, deviceName, attempts, innerException)
    {
    }
}

/// <summary>The device answered with a Modbus exception response (e.g. illegal data address).</summary>
public sealed class DeviceSlaveException : DeviceCommunicationException
{
    /// <summary>Creates the exception.</summary>
    public DeviceSlaveException(string message, byte slaveId, byte functionCode, byte exceptionCode,
        string? deviceName = null, int attempts = 1, Exception? innerException = null)
        : base(message, slaveId, deviceName, attempts, innerException)
    {
        FunctionCode = functionCode;
        ExceptionCode = exceptionCode;
    }

    /// <summary>Function code of the failed request.</summary>
    public byte FunctionCode { get; }

    /// <summary>Modbus exception code returned by the device.</summary>
    public byte ExceptionCode { get; }

    /// <summary>Standard name of <see cref="ExceptionCode"/>.</summary>
    public string ExceptionName => DescribeExceptionCode(ExceptionCode);

    /// <summary>
    /// <c>true</c> when the condition is expected to clear by itself
    /// (Acknowledge, Slave Device Busy, Gateway Target Device Failed To Respond).
    /// </summary>
    public bool IsTransient => IsTransientCode(ExceptionCode);

    /// <summary>Returns the standard name of a Modbus exception code.</summary>
    public static string DescribeExceptionCode(byte exceptionCode) => exceptionCode switch
    {
        1 => "Illegal Function",
        2 => "Illegal Data Address",
        3 => "Illegal Data Value",
        4 => "Slave Device Failure",
        5 => "Acknowledge",
        6 => "Slave Device Busy",
        8 => "Memory Parity Error",
        10 => "Gateway Path Unavailable",
        11 => "Gateway Target Device Failed To Respond",
        _ => "Unknown Exception Code",
    };

    internal static bool IsTransientCode(byte exceptionCode) => exceptionCode is 5 or 6 or 11;
}
