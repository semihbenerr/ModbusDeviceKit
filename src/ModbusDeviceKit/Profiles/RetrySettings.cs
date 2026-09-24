namespace ModbusDeviceKit.Profiles;

/// <summary>Retry policy applied to every connect/read operation of a <see cref="DeviceReader"/>.</summary>
public sealed class RetrySettings
{
    /// <summary>Number of retries after the first failed attempt (0 = no retry). Defaults to 3.</summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>Base delay between attempts, in milliseconds. Defaults to 200.</summary>
    public int DelayMs { get; set; } = 200;

    /// <summary>Upper bound for the delay, in milliseconds (0 = unbounded). Defaults to 5000.</summary>
    public int MaxDelayMs { get; set; } = 5000;

    /// <summary>How the delay grows between retries. Defaults to <see cref="RetryBackoff.Exponential"/>.</summary>
    public RetryBackoff Backoff { get; set; } = RetryBackoff.Exponential;

    /// <summary>Adds random jitter to the delay so that several readers on a bus do not retry in lock-step.</summary>
    public bool UseJitter { get; set; }
}
