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

    /// <summary>
    /// When <c>true</c>, a request that still fails after all retries does not fail the whole reading:
    /// its registers are returned with <see cref="RegisterValue.IsValid"/> = <c>false</c> and a <c>NaN</c> value,
    /// and the other registers are read normally. An exception is thrown only if no register could be read.
    /// Defaults to <c>false</c> (any failure throws).
    /// </summary>
    public bool PartialReads { get; set; }

    /// <summary>
    /// When <c>true</c> (default) and the device rejects a multi-register request with
    /// "Illegal Data Address" or "Illegal Data Value", the registers of that request are read one by one
    /// from then on. Useful for devices with holes in their register map.
    /// </summary>
    public bool SplitRejectedBlocks { get; set; } = true;
}
