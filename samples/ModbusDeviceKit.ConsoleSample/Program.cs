using System.Globalization;
using Microsoft.Extensions.Logging;
using ModbusDeviceKit;
using ModbusDeviceKit.ConsoleSample;
using ModbusDeviceKit.Profiles;

// Usage:
//   ModbusDeviceKit.ConsoleSample [profile.json] [--interval <seconds>] [--simulate]
//
//   profile.json   Device profile (default: device-profile.json next to the executable)
//   --interval     Seconds between readings (default: 5)
//   --simulate     Start a built-in Modbus TCP simulator for the profile and read from it (no hardware needed)
//
// Keys while running:  T = tare (average of 5 samples)   C = clear tare   Q / Esc = quit

if (args.Any(a => a is "-h" or "--help" or "/?"))
{
    PrintUsage();
    return 0;
}

string profilePath = Path.Combine(AppContext.BaseDirectory, "device-profile.json");
TimeSpan interval = TimeSpan.FromSeconds(5);
bool simulate = false;

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--simulate" or "-s":
            simulate = true;
            break;
        case "--interval" or "-i" when i + 1 < args.Length
            && double.TryParse(args[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds)
            && seconds > 0:
            interval = TimeSpan.FromSeconds(seconds);
            i++;
            break;
        case var arg when !arg.StartsWith('-'):
            profilePath = Path.GetFullPath(arg);
            break;
        default:
            Console.Error.WriteLine($"Unknown or incomplete argument: {args[i]}");
            PrintUsage();
            return 1;
    }
}

using var loggerFactory = LoggerFactory.Create(builder => builder
    .AddSimpleConsole(o =>
    {
        o.SingleLine = true;
        o.TimestampFormat = "HH:mm:ss ";
    })
    .SetMinimumLevel(LogLevel.Information));
var logger = loggerFactory.CreateLogger("ModbusDeviceKit");

// 1) Load the profile.
DeviceProfile profile;
try
{
    profile = await DeviceProfile.LoadFromFileAsync(profilePath);
}
catch (Exception ex) when (ex is DeviceProfileException or IOException or UnauthorizedAccessException)
{
    Console.Error.WriteLine($"Could not load profile: {ex.Message}");
    return 2;
}

// Optional: serve the profile from an in-process simulator instead of real hardware.
ProfileSimulator? simulator = null;
if (simulate)
{
    simulator = ProfileSimulator.Start(profile);
    profile.Protocol = ModbusProtocol.ModbusTCP;
    profile.Connection.Tcp = new TcpSettings { Host = "127.0.0.1", Port = simulator.Port };
}

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

try
{
    // 2) Create the reader (transport is chosen from the profile's protocol).
    await using var reader = DeviceReader.Create(profile, logger);

    Console.WriteLine($"Device   : {reader.DeviceName} (slave {reader.SlaveId})");
    Console.WriteLine($"Endpoint : {reader.Transport.Description}{(simulate ? "  [simulator]" : string.Empty)}");
    Console.WriteLine($"Registers: {string.Join(", ", profile.Registers.Select(r => r.Name))}");
    Console.WriteLine($"Interval : {interval.TotalSeconds:0.###} s   Keys: T = tare, C = clear tare, Q = quit");
    Console.WriteLine();

    // 3) Connect. A failure is not fatal: every read reconnects automatically.
    try
    {
        await reader.ConnectAsync(cts.Token);
    }
    catch (DeviceConnectionException ex)
    {
        logger.LogError("{Message} Will keep retrying on every read.", ex.Message);
    }

    var keyboardTask = HandleKeysAsync(reader, logger, cts);

    // 4) Read every interval until Ctrl+C / Q.
    using var timer = new PeriodicTimer(interval);
    do
    {
        try
        {
            var reading = await reader.ReadAsync(cts.Token);
            PrintReading(reading);
        }
        catch (DeviceTimeoutException ex)
        {
            logger.LogError("Timeout: {Message}", ex.Message);
        }
        catch (DeviceConnectionException ex)
        {
            logger.LogError("Connection: {Message}", ex.Message);
        }
        catch (DeviceCommunicationException ex)
        {
            logger.LogError("Communication: {Message}", ex.Message);
        }
    }
    while (await timer.WaitForNextTickAsync(cts.Token));

    await keyboardTask;
}
catch (OperationCanceledException) when (cts.IsCancellationRequested)
{
    // Normal shutdown.
}
finally
{
    if (simulator is not null)
        await simulator.DisposeAsync();
}

Console.WriteLine("Stopped.");
return 0;

static void PrintReading(DeviceReading reading)
{
    Console.WriteLine($"[{reading.Timestamp:HH:mm:ss}] {reading.DeviceName}  ({reading.Duration.TotalMilliseconds:0} ms)");
    foreach (var value in reading.Registers)
    {
        string number = value.DataType == RegisterDataType.Bool
            ? (value.AsBoolean ? "ON" : "OFF")
            : value.Value.ToString("0.###", CultureInfo.InvariantCulture);
        string tare = value.Tare != 0
            ? $"   (tare {value.Tare.ToString("0.###", CultureInfo.InvariantCulture)})"
            : string.Empty;
        Console.WriteLine($"  {value.Name,-14} {number,14} {value.Unit,-7}{tare}");
    }

    Console.WriteLine();
}

static async Task HandleKeysAsync(DeviceReader reader, ILogger logger, CancellationTokenSource cts)
{
    if (Console.IsInputRedirected)
        return;

    try
    {
        while (!cts.IsCancellationRequested)
        {
            if (!Console.KeyAvailable)
            {
                await Task.Delay(100, cts.Token);
                continue;
            }

            switch (Console.ReadKey(intercept: true).Key)
            {
                case ConsoleKey.T:
                    try
                    {
                        var tares = await reader.ApplyTareAsync(samples: 5, cts.Token);
                        Console.WriteLine($"Tare applied: {string.Join(", ", tares.Select(t => $"{t.Key} = {t.Value.ToString("0.###", CultureInfo.InvariantCulture)}"))}");
                    }
                    catch (Exception ex) when (ex is ModbusDeviceKitException or InvalidOperationException)
                    {
                        logger.LogError("Tare failed: {Message}", ex.Message);
                    }

                    break;
                case ConsoleKey.C:
                    reader.ClearAllTares();
                    Console.WriteLine("Tare cleared.");
                    break;
                case ConsoleKey.Q:
                case ConsoleKey.Escape:
                    cts.Cancel();
                    break;
            }
        }
    }
    catch (OperationCanceledException)
    {
    }
}

static void PrintUsage()
{
    Console.WriteLine("Usage: ModbusDeviceKit.ConsoleSample [profile.json] [--interval <seconds>] [--simulate]");
    Console.WriteLine();
    Console.WriteLine("  profile.json   Device profile (default: device-profile.json next to the executable)");
    Console.WriteLine("  --interval     Seconds between readings (default: 5)");
    Console.WriteLine("  --simulate     Read from a built-in Modbus TCP simulator instead of real hardware");
    Console.WriteLine();
    Console.WriteLine("Keys: T = tare (5 samples), C = clear tare, Q / Esc = quit");
}
