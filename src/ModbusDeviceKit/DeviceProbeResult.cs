namespace ModbusDeviceKit;

/// <summary>
/// Result of <see cref="DeviceReader.ProbeAsync"/> / <see cref="ModbusBusScanner.ScanAsync"/>:
/// whether a slave is present on the line, with a human readable diagnosis.
/// </summary>
public sealed record DeviceProbeResult
{
    /// <summary>Probed slave / unit id.</summary>
    public required byte SlaveId { get; init; }

    /// <summary>
    /// <c>true</c> when the device answered — with data or with a Modbus exception response.
    /// An exception response still proves that the device is online and correctly wired.
    /// </summary>
    public required bool Responded { get; init; }

    /// <summary><c>true</c> when the device answered with data (the probed address exists).</summary>
    public bool AddressAccepted { get; init; }

    /// <summary>Modbus exception code when the device rejected the probed address.</summary>
    public byte? ExceptionCode { get; init; }

    /// <summary>Time from the request to the answer or the final failure, including retries.</summary>
    public TimeSpan ResponseTime { get; init; }

    /// <summary>Human readable diagnosis, including what to check when the device did not respond.</summary>
    public required string Message { get; init; }

    /// <summary>The failure, when the device did not answer with data.</summary>
    public Exception? Error { get; init; }

    /// <inheritdoc />
    public override string ToString() => Message;
}
