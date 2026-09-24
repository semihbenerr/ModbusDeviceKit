using System.IO.Ports;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ModbusDeviceKit.Profiles;

/// <summary>
/// Describes a Modbus device: how to reach it and which registers to read.
/// Normally loaded from a JSON file with <see cref="LoadFromFile"/> / <see cref="LoadFromFileAsync"/>.
/// </summary>
/// <example>
/// <code>
/// {
///   "deviceName": "LoadCell_XYZ123",
///   "protocol": "ModbusRTU",
///   "slaveId": 1,
///   "registers": [
///     { "name": "Force", "address": 100, "dataType": "Float32", "scale": 0.01, "unit": "N" }
///   ]
/// }
/// </code>
/// </example>
public sealed class DeviceProfile
{
    /// <summary>Maximum number of registers a single Modbus read request may contain.</summary>
    public const int ModbusMaxRegistersPerRead = 125;

    private static readonly JsonSerializerOptions ReadOptionsJson = CreateJsonOptions(forWriting: false);
    private static readonly JsonSerializerOptions WriteOptionsJson = CreateJsonOptions(forWriting: true);

    /// <summary>Human readable device name, used in logs and exceptions.</summary>
    public string DeviceName { get; set; } = string.Empty;

    /// <summary>Optional free-text description.</summary>
    public string? Description { get; set; }

    /// <summary>Protocol used to reach the device. Defaults to <see cref="ModbusProtocol.ModbusRTU"/>.</summary>
    public ModbusProtocol Protocol { get; set; } = ModbusProtocol.ModbusRTU;

    /// <summary>Modbus slave / unit id. Defaults to 1.</summary>
    public byte SlaveId { get; set; } = 1;

    /// <summary>Default byte order of multi-register values. Defaults to <see cref="Profiles.ByteOrder.ABCD"/> (big-endian).</summary>
    public ByteOrder ByteOrder { get; set; } = ByteOrder.ABCD;

    /// <summary>Connection parameters (serial port or TCP endpoint and timeouts).</summary>
    public ConnectionSettings Connection { get; set; } = new();

    /// <summary>Retry policy for communication failures.</summary>
    public RetrySettings Retry { get; set; } = new();

    /// <summary>Request grouping options.</summary>
    public ReadOptions ReadOptions { get; set; } = new();

    /// <summary>Register map.</summary>
    public List<RegisterDefinition> Registers { get; set; } = new();

    /// <summary>Parses and validates a profile from a JSON string.</summary>
    /// <exception cref="DeviceProfileException">The JSON is malformed or the profile is invalid.</exception>
    public static DeviceProfile FromJson(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        DeviceProfile? profile;
        try
        {
            profile = JsonSerializer.Deserialize<DeviceProfile>(json, ReadOptionsJson);
        }
        catch (JsonException ex)
        {
            throw new DeviceProfileException($"Invalid device profile JSON: {ex.Message}", ex);
        }

        return Finish(profile);
    }

    /// <summary>Reads and validates a profile from a stream containing UTF-8 JSON.</summary>
    /// <exception cref="DeviceProfileException">The JSON is malformed or the profile is invalid.</exception>
    public static async Task<DeviceProfile> LoadAsync(Stream utf8Json, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(utf8Json);
        DeviceProfile? profile;
        try
        {
            profile = await JsonSerializer.DeserializeAsync<DeviceProfile>(utf8Json, ReadOptionsJson, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (JsonException ex)
        {
            throw new DeviceProfileException($"Invalid device profile JSON: {ex.Message}", ex);
        }

        return Finish(profile);
    }

    /// <summary>Reads and validates a profile from a JSON file.</summary>
    /// <exception cref="FileNotFoundException">The file does not exist.</exception>
    /// <exception cref="DeviceProfileException">The JSON is malformed or the profile is invalid.</exception>
    public static DeviceProfile LoadFromFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        try
        {
            return FromJson(File.ReadAllText(path));
        }
        catch (DeviceProfileException ex)
        {
            throw new DeviceProfileException($"Profile '{path}': {ex.Message}", ex.Errors, ex.InnerException);
        }
    }

    /// <summary>Reads and validates a profile from a JSON file asynchronously.</summary>
    /// <exception cref="FileNotFoundException">The file does not exist.</exception>
    /// <exception cref="DeviceProfileException">The JSON is malformed or the profile is invalid.</exception>
    public static async Task<DeviceProfile> LoadFromFileAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
        try
        {
            return await LoadAsync(stream, cancellationToken).ConfigureAwait(false);
        }
        catch (DeviceProfileException ex)
        {
            throw new DeviceProfileException($"Profile '{path}': {ex.Message}", ex.Errors, ex.InnerException);
        }
    }

    /// <summary>Serialises the profile to indented camelCase JSON.</summary>
    public string ToJson() => JsonSerializer.Serialize(this, WriteOptionsJson);

    /// <summary>Writes the profile as JSON to <paramref name="path"/>.</summary>
    public async Task SaveToFileAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        await using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true);
        await JsonSerializer.SerializeAsync(stream, this, WriteOptionsJson, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Creates a deep copy of the profile.</summary>
    public DeviceProfile Clone()
    {
        var copy = JsonSerializer.Deserialize<DeviceProfile>(ToJson(), ReadOptionsJson)!;
        copy.Normalize();
        return copy;
    }

    /// <summary>Returns the register with the given (case-insensitive) name.</summary>
    /// <exception cref="KeyNotFoundException">No register has that name.</exception>
    public RegisterDefinition GetRegister(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return Registers.FirstOrDefault(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase))
            ?? throw new KeyNotFoundException($"Register '{name}' is not defined in profile '{DeviceName}'.");
    }

    /// <summary>Checks the profile and returns every problem found (empty when valid).</summary>
    public IReadOnlyList<string> GetValidationErrors()
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(DeviceName))
            errors.Add("'deviceName' is required.");
        if (!Enum.IsDefined(Protocol))
            errors.Add($"'protocol' value {(int)Protocol} is not supported.");
        if (Protocol is ModbusProtocol.ModbusRTU or ModbusProtocol.ModbusRtuOverTcp && SlaveId is 0 or > 247)
            errors.Add($"'slaveId' must be between 1 and 247 for {Protocol} (was {SlaveId}).");
        if (!Enum.IsDefined(ByteOrder))
            errors.Add($"'byteOrder' value {(int)ByteOrder} is not supported.");

        ValidateConnection(errors);
        ValidateRetry(errors);
        ValidateReadOptions(errors);
        ValidateRegisters(errors);

        return errors;
    }

    /// <summary>Validates the profile.</summary>
    /// <exception cref="DeviceProfileException">The profile contains one or more errors (see <see cref="DeviceProfileException.Errors"/>).</exception>
    public void Validate()
    {
        var errors = GetValidationErrors();
        if (errors.Count > 0)
        {
            string name = string.IsNullOrWhiteSpace(DeviceName) ? "<unnamed>" : DeviceName;
            throw new DeviceProfileException(
                $"Device profile '{name}' is invalid:{Environment.NewLine} - {string.Join(Environment.NewLine + " - ", errors)}",
                errors);
        }
    }

    private void ValidateConnection(List<string> errors)
    {
        var c = Connection;
        if (c is null)
        {
            errors.Add("'connection' must not be null.");
            return;
        }

        if (c.ConnectTimeoutMs <= 0) errors.Add("'connection.connectTimeoutMs' must be greater than 0.");
        if (c.ReadTimeoutMs <= 0) errors.Add("'connection.readTimeoutMs' must be greater than 0.");
        if (c.WriteTimeoutMs <= 0) errors.Add("'connection.writeTimeoutMs' must be greater than 0.");

        if (c.Serial is { } s)
        {
            if (string.IsNullOrWhiteSpace(s.PortName)) errors.Add("'connection.serial.portName' is required.");
            if (s.BaudRate <= 0) errors.Add("'connection.serial.baudRate' must be greater than 0.");
            if (s.DataBits is < 5 or > 8) errors.Add("'connection.serial.dataBits' must be between 5 and 8.");
            if (!Enum.IsDefined(s.Parity)) errors.Add($"'connection.serial.parity' value {(int)s.Parity} is not supported.");
            if (!Enum.IsDefined(s.StopBits) || s.StopBits == StopBits.None)
                errors.Add("'connection.serial.stopBits' must be One, OnePointFive or Two.");
        }

        if (c.Tcp is { } t)
        {
            if (string.IsNullOrWhiteSpace(t.Host)) errors.Add("'connection.tcp.host' is required.");
            if (t.Port is < 1 or > 65535) errors.Add("'connection.tcp.port' must be between 1 and 65535.");
        }
    }

    private void ValidateRetry(List<string> errors)
    {
        var r = Retry;
        if (r is null)
        {
            errors.Add("'retry' must not be null.");
            return;
        }

        if (r.MaxRetries is < 0 or > 100) errors.Add("'retry.maxRetries' must be between 0 and 100.");
        if (r.DelayMs is < 0 or > 60_000) errors.Add("'retry.delayMs' must be between 0 and 60000.");
        if (r.MaxDelayMs < 0) errors.Add("'retry.maxDelayMs' must not be negative.");
        if (r.MaxDelayMs > 0 && r.MaxDelayMs < r.DelayMs) errors.Add("'retry.maxDelayMs' must be 0 (unbounded) or at least 'retry.delayMs'.");
        if (!Enum.IsDefined(r.Backoff)) errors.Add($"'retry.backoff' value {(int)r.Backoff} is not supported.");
    }

    private void ValidateReadOptions(List<string> errors)
    {
        var o = ReadOptions;
        if (o is null)
        {
            errors.Add("'readOptions' must not be null.");
            return;
        }

        if (o.MaxRegistersPerRead is < 1 or > ModbusMaxRegistersPerRead)
            errors.Add($"'readOptions.maxRegistersPerRead' must be between 1 and {ModbusMaxRegistersPerRead}.");
        if (o.MaxAddressGap < 0) errors.Add("'readOptions.maxAddressGap' must not be negative.");
        if (o.InterRequestDelayMs is < 0 or > 10_000) errors.Add("'readOptions.interRequestDelayMs' must be between 0 and 10000.");
    }

    private void ValidateRegisters(List<string> errors)
    {
        if (Registers is null || Registers.Count == 0)
        {
            errors.Add("'registers' must contain at least one register.");
            return;
        }

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < Registers.Count; i++)
        {
            var reg = Registers[i];
            if (reg is null)
            {
                errors.Add($"registers[{i}] must not be null.");
                continue;
            }

            string label = string.IsNullOrWhiteSpace(reg.Name) ? $"registers[{i}]" : $"Register '{reg.Name}'";

            if (string.IsNullOrWhiteSpace(reg.Name))
                errors.Add($"registers[{i}]: 'name' is required.");
            else if (!names.Add(reg.Name))
                errors.Add($"{label}: duplicate register name (names are case-insensitive).");

            if (!Enum.IsDefined(reg.RegisterType))
            {
                errors.Add($"{label}: 'registerType' value {(int)reg.RegisterType} is not supported.");
                continue;
            }

            if (!Enum.IsDefined(reg.DataType))
            {
                errors.Add($"{label}: 'dataType' value {(int)reg.DataType} is not supported.");
                continue;
            }

            if (reg.ByteOrder is { } order && !Enum.IsDefined(order))
                errors.Add($"{label}: 'byteOrder' value {(int)order} is not supported.");

            bool bitTable = reg.RegisterType.IsBitType();
            if (bitTable && reg.DataType != RegisterDataType.Bool)
                errors.Add($"{label}: {reg.RegisterType} registers must use dataType 'Bool'.");
            if (!bitTable && reg.DataType == RegisterDataType.Bool)
                errors.Add($"{label}: dataType 'Bool' is only valid for Coil or DiscreteInput registers.");

            if (reg.Address + reg.RegisterCount - 1 > ushort.MaxValue)
                errors.Add($"{label}: address {reg.Address} with {reg.RegisterCount} register(s) exceeds the Modbus address space.");

            if (!double.IsFinite(reg.Scale) || reg.Scale == 0)
                errors.Add($"{label}: 'scale' must be a finite, non-zero number.");
            if (!double.IsFinite(reg.Offset))
                errors.Add($"{label}: 'offset' must be a finite number.");
            if (!double.IsFinite(reg.Tare))
                errors.Add($"{label}: 'tare' must be a finite number.");
            if (reg.DataType == RegisterDataType.Bool && (reg.AllowTare || reg.Tare != 0))
                errors.Add($"{label}: Bool registers cannot be tared.");
        }
    }

    private static DeviceProfile Finish(DeviceProfile? profile)
    {
        if (profile is null)
            throw new DeviceProfileException("Device profile JSON is empty (null).");

        profile.Normalize();
        profile.Validate();
        return profile;
    }

    /// <summary>Replaces sections that were explicitly set to <c>null</c> in JSON with their defaults.</summary>
    private void Normalize()
    {
        Connection ??= new ConnectionSettings();
        Retry ??= new RetrySettings();
        ReadOptions ??= new ReadOptions();
        Registers ??= new List<RegisterDefinition>();
        foreach (var reg in Registers)
        {
            if (reg is not null)
                reg.Unit ??= string.Empty;
        }
    }

    private static JsonSerializerOptions CreateJsonOptions(bool forWriting)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            WriteIndented = forWriting,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            // Typos such as "scael" must not be silently ignored in a register map.
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        };
        options.Converters.Add(new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: true));
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }
}
