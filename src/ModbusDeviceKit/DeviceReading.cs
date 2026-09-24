using System.Collections;
using System.Diagnostics.CodeAnalysis;

namespace ModbusDeviceKit;

/// <summary>
/// Result of one read cycle. Behaves as a <c>name → value</c> dictionary
/// (<c>reading["Force"]</c>) and exposes full details through <see cref="Registers"/>.
/// </summary>
public sealed class DeviceReading : IReadOnlyDictionary<string, double>
{
    private readonly Dictionary<string, RegisterValue> _byName;

    /// <summary>Creates a reading. Register names must be unique (case-insensitive).</summary>
    public DeviceReading(string deviceName, DateTimeOffset timestamp, TimeSpan duration, IEnumerable<RegisterValue> registers)
    {
        ArgumentNullException.ThrowIfNull(registers);
        DeviceName = deviceName ?? string.Empty;
        Timestamp = timestamp;
        Duration = duration;
        Registers = registers.ToArray();
        _byName = new Dictionary<string, RegisterValue>(Registers.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var value in Registers)
        {
            if (!_byName.TryAdd(value.Name, value))
                throw new ArgumentException($"Duplicate register name '{value.Name}'.", nameof(registers));
        }
    }

    /// <summary>Device name from the profile.</summary>
    public string DeviceName { get; }

    /// <summary>Time the read cycle started.</summary>
    public DateTimeOffset Timestamp { get; }

    /// <summary>Time the read cycle took, including retries.</summary>
    public TimeSpan Duration { get; }

    /// <summary>All values in profile order.</summary>
    public IReadOnlyList<RegisterValue> Registers { get; }

    /// <summary><c>true</c> when every register was read successfully.</summary>
    public bool IsComplete => Registers.All(r => r.IsValid);

    /// <summary>Registers that could not be read in this cycle (only with partial reads enabled).</summary>
    public IReadOnlyList<RegisterValue> FailedRegisters => Registers.Where(r => !r.IsValid).ToArray();

    /// <summary>Final engineering value of a register (case-insensitive name).</summary>
    /// <exception cref="KeyNotFoundException">The register is not part of this reading.</exception>
    public double this[string registerName] => GetRegister(registerName).Value;

    /// <summary>Number of values.</summary>
    public int Count => Registers.Count;

    /// <summary>Register names in profile order.</summary>
    public IEnumerable<string> Keys => Registers.Select(r => r.Name);

    IEnumerable<double> IReadOnlyDictionary<string, double>.Values => Registers.Select(r => r.Value);

    /// <summary>Full details of a register (case-insensitive name).</summary>
    /// <exception cref="KeyNotFoundException">The register is not part of this reading.</exception>
    public RegisterValue GetRegister(string registerName)
    {
        ArgumentNullException.ThrowIfNull(registerName);
        return _byName.TryGetValue(registerName, out var value)
            ? value
            : throw new KeyNotFoundException($"Register '{registerName}' is not part of this reading of '{DeviceName}'.");
    }

    /// <summary>Gets full details of a register if it is part of this reading.</summary>
    public bool TryGetRegister(string registerName, [MaybeNullWhen(false)] out RegisterValue value) =>
        _byName.TryGetValue(registerName, out value);

    /// <inheritdoc />
    public bool ContainsKey(string key) => _byName.ContainsKey(key);

    /// <inheritdoc />
    public bool TryGetValue(string key, out double value)
    {
        if (_byName.TryGetValue(key, out var register))
        {
            value = register.Value;
            return true;
        }

        value = 0;
        return false;
    }

    /// <summary>Copies the final values into a new case-insensitive dictionary.</summary>
    public Dictionary<string, double> ToDictionary()
    {
        var result = new Dictionary<string, double>(Registers.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var value in Registers)
            result[value.Name] = value.Value;
        return result;
    }

    /// <inheritdoc />
    public IEnumerator<KeyValuePair<string, double>> GetEnumerator() =>
        Registers.Select(r => new KeyValuePair<string, double>(r.Name, r.Value)).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <inheritdoc />
    public override string ToString() => $"{DeviceName} @ {Timestamp:HH:mm:ss.fff}: {string.Join("; ", Registers)}";
}
