using System.Text.Json.Serialization;

namespace ModbusDeviceKit.Profiles;

/// <summary>
/// A single named value in a device's register map.
/// The engineering value is computed as <c>raw × scale + offset − tare</c>.
/// </summary>
public sealed class RegisterDefinition
{
    /// <summary>Unique (case-insensitive) name used to access the value, e.g. <c>"Force"</c>.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Zero-based protocol address (e.g. holding register 40101 → address 100).</summary>
    public ushort Address { get; set; }

    /// <summary>Data table of the register. Defaults to <see cref="Profiles.RegisterType.Holding"/>.</summary>
    public RegisterType RegisterType { get; set; } = RegisterType.Holding;

    /// <summary>How the raw register words are decoded. Defaults to <see cref="RegisterDataType.UInt16"/>.</summary>
    public RegisterDataType DataType { get; set; } = RegisterDataType.UInt16;

    /// <summary>Byte order for this register; when <c>null</c> the profile's <see cref="DeviceProfile.ByteOrder"/> is used.</summary>
    public ByteOrder? ByteOrder { get; set; }

    /// <summary>Multiplier applied to the decoded raw value. Defaults to 1.</summary>
    public double Scale { get; set; } = 1.0;

    /// <summary>Calibration offset added after scaling (engineering units). Defaults to 0.</summary>
    public double Offset { get; set; }

    /// <summary>Initial tare subtracted from the calibrated value (engineering units). Defaults to 0.</summary>
    public double Tare { get; set; }

    /// <summary>
    /// When <c>true</c> the register is included by <see cref="DeviceReader.ApplyTare()"/> /
    /// <see cref="DeviceReader.ApplyTareAsync(int, CancellationToken)"/> (tare all).
    /// Registers can still be tared individually by name regardless of this flag.
    /// </summary>
    public bool AllowTare { get; set; }

    /// <summary>Engineering unit shown next to the value, e.g. <c>"N"</c>, <c>"C"</c>, <c>"bar"</c>.</summary>
    public string Unit { get; set; } = string.Empty;

    /// <summary>Optional free-text description.</summary>
    public string? Description { get; set; }

    /// <summary>Number of 16-bit registers (or bits for coils/discrete inputs) this value occupies.</summary>
    [JsonIgnore]
    public int RegisterCount => DataType.GetRegisterCount();

    /// <summary>Byte order that is effectively used for this register.</summary>
    public ByteOrder GetEffectiveByteOrder(DeviceProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return ByteOrder ?? profile.ByteOrder;
    }

    /// <inheritdoc />
    public override string ToString() => $"{Name} ({RegisterType} {Address}, {DataType})";
}
