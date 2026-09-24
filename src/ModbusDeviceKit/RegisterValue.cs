using System.Globalization;
using ModbusDeviceKit.Profiles;

namespace ModbusDeviceKit;

/// <summary>One value of a <see cref="DeviceReading"/>, with every intermediate step of the conversion.</summary>
public sealed record RegisterValue
{
    /// <summary>Register name from the profile.</summary>
    public required string Name { get; init; }

    /// <summary>Final engineering value: <c>raw × scale + offset − tare</c> (0/1 for Bool registers).</summary>
    public required double Value { get; init; }

    /// <summary>Engineering unit from the profile.</summary>
    public string Unit { get; init; } = string.Empty;

    /// <summary>Decoded value before scaling.</summary>
    public required double RawValue { get; init; }

    /// <summary>Value after scale and offset, before the tare is subtracted.</summary>
    public required double CalibratedValue { get; init; }

    /// <summary>Tare subtracted from <see cref="CalibratedValue"/>.</summary>
    public double Tare { get; init; }

    /// <summary>Data type used to decode the value.</summary>
    public RegisterDataType DataType { get; init; }

    /// <summary>Data table the value was read from.</summary>
    public RegisterType RegisterType { get; init; }

    /// <summary>Protocol address of the (first) register.</summary>
    public ushort Address { get; init; }

    /// <summary>Registers exactly as received from the device (for Bool registers: a single 0/1).</summary>
    public IReadOnlyList<ushort> RawRegisters { get; init; } = Array.Empty<ushort>();

    /// <summary>The value as a boolean (non-zero = <c>true</c>). Mainly for coils / discrete inputs.</summary>
    public bool AsBoolean => IsValid && Value != 0;

    /// <summary>
    /// <c>false</c> when the register could not be read in this cycle (only with <see cref="Profiles.ReadOptions.PartialReads"/>).
    /// <see cref="Value"/>, <see cref="RawValue"/> and <see cref="CalibratedValue"/> are then <c>NaN</c>.
    /// </summary>
    public bool IsValid { get; init; } = true;

    /// <summary>Why the register could not be read, when <see cref="IsValid"/> is <c>false</c>.</summary>
    public string? Error { get; init; }

    /// <inheritdoc />
    public override string ToString() =>
        !IsValid
            ? $"{Name} = <{Error ?? "not read"}>"
            : string.IsNullOrEmpty(Unit)
            ? string.Create(CultureInfo.InvariantCulture, $"{Name} = {Value:G6}")
            : string.Create(CultureInfo.InvariantCulture, $"{Name} = {Value:G6} {Unit}");
}
