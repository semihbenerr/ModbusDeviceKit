using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModbusDeviceKit.Internal;
using ModbusDeviceKit.Profiles;
using ModbusDeviceKit.Transport;
using Polly;

namespace ModbusDeviceKit;

/// <summary>
/// Reads every register of a <see cref="DeviceProfile"/> through an <see cref="IModbusTransport"/>
/// and returns named, scaled and tared engineering values.
/// </summary>
/// <remarks>
/// <para>Registers are grouped into as few Modbus requests as possible (see <see cref="ReadOptions"/>).</para>
/// <para>Every connect/read is retried according to <see cref="DeviceProfile.Retry"/>. When all attempts fail a
/// <see cref="DeviceTimeoutException"/>, <see cref="DeviceConnectionException"/>, <see cref="DeviceSlaveException"/>
/// or <see cref="DeviceCommunicationException"/> is thrown.</para>
/// <para>If the transport is not connected (or dropped), it is (re)connected automatically before a request.</para>
/// <para>The instance is thread-safe. Several readers may share one transport (e.g. several slaves on one RS-485 bus).</para>
/// </remarks>
public sealed class DeviceReader : IDisposable, IAsyncDisposable
{
    private readonly ResiliencePipeline _pipeline;
    private readonly ILogger _logger;
    private readonly bool _ownsTransport;
    private readonly Dictionary<string, RegisterDefinition> _registersByName;
    private readonly IReadOnlyList<ReadBlock> _fullReadPlan;
    private readonly ConcurrentDictionary<string, double> _tares = new(StringComparer.OrdinalIgnoreCase);
    private DeviceReading? _lastReading;
    private int _disposed;

    /// <summary>Creates a reader.</summary>
    /// <param name="profile">Device profile. It is validated and copied; later changes to it have no effect.</param>
    /// <param name="transport">Transport to use.</param>
    /// <param name="logger">Optional logger (retries are logged as warnings).</param>
    /// <param name="ownsTransport"><c>true</c> to dispose <paramref name="transport"/> together with the reader.</param>
    /// <exception cref="DeviceProfileException">The profile is invalid.</exception>
    public DeviceReader(DeviceProfile profile, IModbusTransport transport, ILogger? logger = null, bool ownsTransport = false)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(transport);
        profile.Validate();

        Profile = profile.Clone();
        Transport = transport;
        _logger = logger ?? NullLogger.Instance;
        _ownsTransport = ownsTransport;
        _registersByName = Profile.Registers.ToDictionary(r => r.Name, StringComparer.OrdinalIgnoreCase);
        _fullReadPlan = ReadBlockPlanner.Plan(Profile.Registers, Profile.ReadOptions);
        foreach (var reg in Profile.Registers.Where(r => r.Tare != 0))
            _tares[reg.Name] = reg.Tare;
        _pipeline = RetryPipelineFactory.Create(Profile.Retry, _logger, Profile.DeviceName);
    }

    /// <summary>
    /// Creates a reader together with the transport described by the profile's protocol and connection settings.
    /// The transport is disposed with the reader.
    /// </summary>
    /// <exception cref="DeviceProfileException">The profile is invalid or lacks the required connection settings.</exception>
    public static DeviceReader Create(DeviceProfile profile, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(profile);
        profile.Validate();
        var transport = ModbusTransportFactory.Create(profile);
        return new DeviceReader(profile, transport, logger, ownsTransport: true);
    }

    /// <summary>Loads a JSON profile and creates a reader with its transport (see <see cref="Create"/>).</summary>
    public static async Task<DeviceReader> CreateFromFileAsync(string profilePath, ILogger? logger = null, CancellationToken cancellationToken = default)
    {
        var profile = await DeviceProfile.LoadFromFileAsync(profilePath, cancellationToken).ConfigureAwait(false);
        return Create(profile, logger);
    }

    /// <summary>Copy of the profile this reader works with.</summary>
    public DeviceProfile Profile { get; }

    /// <summary>Transport used for communication.</summary>
    public IModbusTransport Transport { get; }

    /// <summary>Device name from the profile.</summary>
    public string DeviceName => Profile.DeviceName;

    /// <summary>Slave id from the profile.</summary>
    public byte SlaveId => Profile.SlaveId;

    /// <summary><c>true</c> while the transport is connected.</summary>
    public bool IsConnected => Transport.IsConnected;

    /// <summary>Most recent successful full reading (<see cref="ReadAsync(CancellationToken)"/>), or <c>null</c>.</summary>
    public DeviceReading? LastReading => Volatile.Read(ref _lastReading);

    /// <summary>Snapshot of the current tare values (only registers with a non-zero tare).</summary>
    public IReadOnlyDictionary<string, double> TareValues =>
        new Dictionary<string, double>(_tares, StringComparer.OrdinalIgnoreCase);

    /// <summary>Connects the transport, retrying according to the profile.</summary>
    /// <exception cref="DeviceConnectionException">The connection could not be opened after all attempts.</exception>
    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        // Every attempt connects the transport when needed, so the operation itself has nothing left to do.
        await ExecuteWithRetryAsync(static _ => Task.FromResult(true), "connect", cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("{Device}: connected via {Transport}.", DeviceName, Transport.Description);
    }

    /// <summary>Disconnects the transport. The next read reconnects automatically.</summary>
    public Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return Transport.DisconnectAsync(cancellationToken);
    }

    /// <summary>Reads every register of the profile.</summary>
    /// <exception cref="DeviceTimeoutException">The device did not answer after all retries.</exception>
    /// <exception cref="DeviceConnectionException">The transport could not be (re)connected after all retries.</exception>
    /// <exception cref="DeviceSlaveException">The device rejected a request (e.g. illegal data address).</exception>
    /// <exception cref="DeviceCommunicationException">Another communication error persisted after all retries.</exception>
    public async Task<DeviceReading> ReadAsync(CancellationToken cancellationToken = default)
    {
        var reading = await ReadBlocksAsync(_fullReadPlan, cancellationToken).ConfigureAwait(false);
        Volatile.Write(ref _lastReading, reading);
        return reading;
    }

    /// <summary>Reads only the named registers (does not update <see cref="LastReading"/>).</summary>
    /// <exception cref="ArgumentException">A name is not defined in the profile.</exception>
    public Task<DeviceReading> ReadAsync(IEnumerable<string> registerNames, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(registerNames);
        var registers = registerNames.Distinct(StringComparer.OrdinalIgnoreCase).Select(GetDefinition).ToList();
        if (registers.Count == 0)
            throw new ArgumentException("At least one register name is required.", nameof(registerNames));

        return ReadBlocksAsync(ReadBlockPlanner.Plan(registers, Profile.ReadOptions), cancellationToken);
    }

    /// <summary>Reads a single register and returns its final engineering value.</summary>
    /// <exception cref="ArgumentException">The name is not defined in the profile.</exception>
    public async Task<double> ReadValueAsync(string registerName, CancellationToken cancellationToken = default)
    {
        var reading = await ReadAsync(new[] { registerName }, cancellationToken).ConfigureAwait(false);
        return reading[registerName];
    }

    /// <summary>
    /// Reads the current values of every register with <c>"allowTare": true</c> and stores them as tare,
    /// so that subsequent readings of these registers start from zero.
    /// </summary>
    /// <param name="samples">Number of readings to average (≥ 1). Averaging reduces the effect of noise.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The new tare value of each register.</returns>
    /// <exception cref="InvalidOperationException">No register in the profile allows tare.</exception>
    public Task<IReadOnlyDictionary<string, double>> ApplyTareAsync(int samples = 1, CancellationToken cancellationToken = default) =>
        TareCoreAsync(GetTareAllTargets(), samples, cancellationToken);

    /// <summary>Reads the current value of one register and stores it as its tare.</summary>
    /// <returns>The new tare value.</returns>
    /// <exception cref="ArgumentException">The name is not defined in the profile.</exception>
    /// <exception cref="InvalidOperationException">The register is a Bool register.</exception>
    public async Task<double> ApplyTareAsync(string registerName, int samples = 1, CancellationToken cancellationToken = default)
    {
        var register = GetTareableDefinition(registerName);
        var tares = await TareCoreAsync(new[] { register }, samples, cancellationToken).ConfigureAwait(false);
        return tares[register.Name];
    }

    /// <summary>
    /// Tares every register with <c>"allowTare": true</c> using <see cref="LastReading"/> (no communication).
    /// </summary>
    /// <exception cref="InvalidOperationException">No reading is available yet, or no register allows tare.</exception>
    public void ApplyTare()
    {
        var last = LastReading ?? throw new InvalidOperationException(
            $"{DeviceName}: no reading available yet. Call ReadAsync() first or use ApplyTareAsync().");

        foreach (var register in GetTareAllTargets())
            StoreTare(register, last.GetRegister(register.Name).CalibratedValue);
    }

    /// <summary>Tares one register using <see cref="LastReading"/> (no communication).</summary>
    /// <exception cref="ArgumentException">The name is not defined in the profile.</exception>
    /// <exception cref="InvalidOperationException">No reading is available yet, or the register is a Bool register.</exception>
    public void ApplyTare(string registerName)
    {
        var register = GetTareableDefinition(registerName);
        var last = LastReading ?? throw new InvalidOperationException(
            $"{DeviceName}: no reading available yet. Call ReadAsync() first or use ApplyTareAsync().");

        StoreTare(register, last.GetRegister(register.Name).CalibratedValue);
    }

    /// <summary>Sets the tare of a register explicitly (engineering units, e.g. restored from a calibration file).</summary>
    public void SetTare(string registerName, double tare)
    {
        if (!double.IsFinite(tare))
            throw new ArgumentOutOfRangeException(nameof(tare), tare, "Tare must be a finite number.");

        StoreTare(GetTareableDefinition(registerName), tare);
    }

    /// <summary>Returns the current tare of a register (0 when none).</summary>
    public double GetTare(string registerName)
    {
        var register = GetDefinition(registerName);
        return _tares.TryGetValue(register.Name, out double tare) ? tare : 0;
    }

    /// <summary>Removes the tare of one register (its tare becomes 0).</summary>
    public void ClearTare(string registerName)
    {
        var register = GetDefinition(registerName);
        _tares.TryRemove(register.Name, out _);
        _logger.LogInformation("{Device}: tare of {Register} cleared.", DeviceName, register.Name);
    }

    /// <summary>Removes the tare of every register.</summary>
    public void ClearAllTares()
    {
        _tares.Clear();
        _logger.LogInformation("{Device}: all tares cleared.", DeviceName);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        if (_ownsTransport)
            Transport.Dispose();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        if (_ownsTransport)
            await Transport.DisposeAsync().ConfigureAwait(false);
    }

    private async Task<IReadOnlyDictionary<string, double>> TareCoreAsync(
        IReadOnlyList<RegisterDefinition> targets, int samples, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(samples, 1);

        var plan = ReadBlockPlanner.Plan(targets, Profile.ReadOptions);
        var sums = targets.ToDictionary(r => r.Name, _ => 0.0, StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < samples; i++)
        {
            var reading = await ReadBlocksAsync(plan, cancellationToken).ConfigureAwait(false);
            foreach (var register in targets)
                sums[register.Name] += reading.GetRegister(register.Name).CalibratedValue;
        }

        var result = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var register in targets)
        {
            double tare = sums[register.Name] / samples;
            StoreTare(register, tare);
            result[register.Name] = tare;
        }

        return result;
    }

    private void StoreTare(RegisterDefinition register, double tare)
    {
        _tares[register.Name] = tare;
        _logger.LogInformation("{Device}: tare of {Register} set to {Tare} {Unit}.", DeviceName, register.Name, tare, register.Unit);
    }

    private async Task<DeviceReading> ReadBlocksAsync(IReadOnlyList<ReadBlock> plan, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        var timestamp = DateTimeOffset.Now;
        var stopwatch = Stopwatch.StartNew();
        var values = new Dictionary<RegisterDefinition, RegisterValue>(ReferenceEqualityComparer.Instance);
        int interRequestDelay = Profile.ReadOptions.InterRequestDelayMs;

        for (int i = 0; i < plan.Count; i++)
        {
            if (i > 0 && interRequestDelay > 0)
                await Task.Delay(interRequestDelay, cancellationToken).ConfigureAwait(false);

            var block = plan[i];
            if (block.IsBitBlock)
            {
                bool[] bits = await ExecuteWithRetryAsync(
                    ct => block.RegisterType == RegisterType.Coil
                        ? Transport.ReadCoilsAsync(SlaveId, block.StartAddress, block.Count, ct)
                        : Transport.ReadDiscreteInputsAsync(SlaveId, block.StartAddress, block.Count, ct),
                    block.ToString(), cancellationToken).ConfigureAwait(false);
                EnsureLength(bits.Length, block);

                foreach (var register in block.Registers)
                    values[register] = CreateBitValue(register, bits[register.Address - block.StartAddress]);
            }
            else
            {
                ushort[] words = await ExecuteWithRetryAsync(
                    ct => block.RegisterType == RegisterType.Holding
                        ? Transport.ReadHoldingRegistersAsync(SlaveId, block.StartAddress, block.Count, ct)
                        : Transport.ReadInputRegistersAsync(SlaveId, block.StartAddress, block.Count, ct),
                    block.ToString(), cancellationToken).ConfigureAwait(false);
                EnsureLength(words.Length, block);

                foreach (var register in block.Registers)
                    values[register] = CreateRegisterValue(register, words, register.Address - block.StartAddress);
            }
        }

        stopwatch.Stop();
        var ordered = Profile.Registers.Where(values.ContainsKey).Select(r => values[r]);
        return new DeviceReading(DeviceName, timestamp, stopwatch.Elapsed, ordered);
    }

    private void EnsureLength(int received, ReadBlock block)
    {
        if (received < block.Count)
        {
            throw new DeviceCommunicationException(
                $"Device '{DeviceName}' returned {received} value(s) for '{block}' but {block.Count} were requested.",
                SlaveId, DeviceName);
        }
    }

    private RegisterValue CreateRegisterValue(RegisterDefinition register, ushort[] blockWords, int offset)
    {
        var raw = blockWords.AsSpan(offset, register.RegisterCount);
        double rawValue = RegisterDecoder.Decode(raw, register.DataType, register.GetEffectiveByteOrder(Profile));
        double calibrated = rawValue * register.Scale + register.Offset;
        double tare = _tares.TryGetValue(register.Name, out double t) ? t : 0;

        return new RegisterValue
        {
            Name = register.Name,
            Value = calibrated - tare,
            Unit = register.Unit,
            RawValue = rawValue,
            CalibratedValue = calibrated,
            Tare = tare,
            DataType = register.DataType,
            RegisterType = register.RegisterType,
            Address = register.Address,
            RawRegisters = raw.ToArray(),
        };
    }

    private static RegisterValue CreateBitValue(RegisterDefinition register, bool bit)
    {
        double value = bit ? 1 : 0;
        return new RegisterValue
        {
            Name = register.Name,
            Value = value,
            Unit = register.Unit,
            RawValue = value,
            CalibratedValue = value,
            DataType = register.DataType,
            RegisterType = register.RegisterType,
            Address = register.Address,
            RawRegisters = new[] { (ushort)value },
        };
    }

    /// <summary>
    /// Runs <paramref name="operation"/> through the retry pipeline. Each attempt (re)connects the transport first
    /// when needed. Failures that survive all attempts are wrapped with device context.
    /// </summary>
    private async Task<T> ExecuteWithRetryAsync<T>(Func<CancellationToken, Task<T>> operation, string operationName, CancellationToken cancellationToken)
    {
        var context = ResilienceContextPool.Shared.Get(cancellationToken);
        context.Properties.Set(RetryPipelineFactory.OperationKey, operationName);
        int attempts = 0;
        try
        {
            return await _pipeline.ExecuteAsync(async ctx =>
            {
                Interlocked.Increment(ref attempts);
                if (!Transport.IsConnected)
                    await Transport.ConnectAsync(ctx.CancellationToken).ConfigureAwait(false);
                return await operation(ctx.CancellationToken).ConfigureAwait(false);
            }, context).ConfigureAwait(false);
        }
        catch (Exception ex) when (ExceptionClassifier.Classify(ex) != FailureKind.None)
        {
            throw WrapFailure(ex, operationName, Volatile.Read(ref attempts));
        }
        finally
        {
            ResilienceContextPool.Shared.Return(context);
        }
    }

    private Exception WrapFailure(Exception ex, string operation, int attempts)
    {
        string device = $"Device '{DeviceName}' (slave {SlaveId}, {Transport.Description})";
        string tries = attempts == 1 ? "1 attempt" : $"{attempts} attempts";

        switch (ExceptionClassifier.Classify(ex))
        {
            case FailureKind.Timeout:
                return new DeviceTimeoutException(
                    $"{device} did not respond to '{operation}' after {tries}.", SlaveId, DeviceName, attempts, ex);

            case FailureKind.Connection:
                return new DeviceConnectionException(
                    $"{device}: connection failed after {tries}. {ex.Message}", Transport.Description, attempts, ex);

            case FailureKind.SlaveTransient or FailureKind.SlaveRejected
                when ExceptionClassifier.TryGetSlaveError(ex, out byte functionCode, out byte exceptionCode):
                return new DeviceSlaveException(
                    $"{device} rejected '{operation}' with Modbus exception {exceptionCode} " +
                    $"({DeviceSlaveException.DescribeExceptionCode(exceptionCode)}) after {tries}.",
                    SlaveId, functionCode, exceptionCode, DeviceName, attempts, ex);

            default:
                return new DeviceCommunicationException(
                    $"{device}: '{operation}' failed after {tries}. {ex.Message}", SlaveId, DeviceName, attempts, ex);
        }
    }

    private IReadOnlyList<RegisterDefinition> GetTareAllTargets()
    {
        var targets = Profile.Registers.Where(r => r.AllowTare).ToList();
        if (targets.Count == 0)
        {
            throw new InvalidOperationException(
                $"Profile '{DeviceName}' has no register with \"allowTare\": true. Tare a register by name instead.");
        }

        return targets;
    }

    private RegisterDefinition GetDefinition(string registerName)
    {
        ArgumentNullException.ThrowIfNull(registerName);
        return _registersByName.TryGetValue(registerName, out var register)
            ? register
            : throw new ArgumentException($"Register '{registerName}' is not defined in profile '{DeviceName}'.", nameof(registerName));
    }

    private RegisterDefinition GetTareableDefinition(string registerName)
    {
        var register = GetDefinition(registerName);
        if (register.DataType == RegisterDataType.Bool)
            throw new InvalidOperationException($"Register '{register.Name}' is a Bool register and cannot be tared.");
        return register;
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
}
