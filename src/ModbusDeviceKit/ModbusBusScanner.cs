using ModbusDeviceKit.Profiles;
using ModbusDeviceKit.Transport;

namespace ModbusDeviceKit;

/// <summary>Finds which slave ids answer on a bus — handy when commissioning an RS-485 line.</summary>
public static class ModbusBusScanner
{
    /// <summary>
    /// Probes every id in <paramref name="slaveIds"/> by reading one value at <paramref name="address"/>.
    /// Ids are probed one after another; a silent id costs roughly <c>attempts × read timeout</c>.
    /// </summary>
    /// <param name="transport">Transport of the bus. It is connected when needed and left open.</param>
    /// <param name="slaveIds">Slave ids to probe, e.g. <c>Enumerable.Range(1, 10).Select(i => (byte)i)</c>.</param>
    /// <param name="address">Address to read on every device.</param>
    /// <param name="registerType">Data table to read.</param>
    /// <param name="attempts">Attempts per id (≥ 1). The first frame after opening a serial line is sometimes lost.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task<IReadOnlyList<DeviceProbeResult>> ScanAsync(
        IModbusTransport transport,
        IEnumerable<byte> slaveIds,
        ushort address = 0,
        RegisterType registerType = RegisterType.Holding,
        int attempts = 2,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(slaveIds);
        ArgumentOutOfRangeException.ThrowIfLessThan(attempts, 1);

        var results = new List<DeviceProbeResult>();
        foreach (byte slaveId in slaveIds.Distinct())
        {
            var profile = new DeviceProfile
            {
                DeviceName = $"Slave {slaveId}",
                // Only affects profile validation here (the transport decides the framing); TCP rules accept every id.
                Protocol = ModbusProtocol.ModbusTCP,
                SlaveId = slaveId,
                Retry = new RetrySettings { MaxRetries = attempts - 1, DelayMs = 50, Backoff = RetryBackoff.Constant },
                Registers =
                {
                    new RegisterDefinition
                    {
                        Name = "Probe",
                        Address = address,
                        RegisterType = registerType,
                        DataType = registerType.IsBitType() ? RegisterDataType.Bool : RegisterDataType.UInt16,
                    },
                },
            };

            await using var reader = new DeviceReader(profile, transport, ownsTransport: false);
            results.Add(await reader.ProbeAsync(cancellationToken).ConfigureAwait(false));
        }

        return results;
    }
}
