namespace ModbusDeviceKit.Profiles;

/// <summary>Controls how registers are grouped into Modbus requests.</summary>
public sealed class ReadOptions
{
    /// <summary>Maximum number of 16-bit registers per request (Modbus limit: 125). Defaults to 120.</summary>
    public int MaxRegistersPerRead { get; set; } = 120;

    /// <summary>
    /// Maximum number of unused addresses allowed between two registers that are still read in one request.
    /// 0 (default) merges only adjacent registers. Increase it only if the device tolerates reading the gap addresses.
    /// </summary>
    public int MaxAddressGap { get; set; }

    /// <summary>Pause between consecutive requests of one reading, in milliseconds. Some RS-485 devices need it. Defaults to 0.</summary>
    public int InterRequestDelayMs { get; set; }
}
