# ModbusDeviceKit

🇬🇧 **English** | [🇹🇷 Türkçe](https://github.com/semihbenerr/ModbusDeviceKit/blob/main/README.tr.md)

[![NuGet](https://img.shields.io/nuget/v/ModbusDeviceKit.svg)](https://www.nuget.org/packages/ModbusDeviceKit)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](https://github.com/semihbenerr/ModbusDeviceKit/blob/main/LICENSE)

```bash
dotnet add package ModbusDeviceKit
```

A .NET 8 library that reads Modbus RTU/TCP devices (load cells, torque sensors, temperature/pressure transmitters…)
described by **JSON profile files**. When a new device brand arrives, you add a JSON profile instead of hand-coding
its register map.

- Reads every register in one call and merges adjacent registers into **a single Modbus request**
- `Int16/UInt16/Int32/UInt32/Float32/Int64/UInt64/Float64/Bool` with `ABCD/CDAB/BADC/DCBA` byte orders
- Named, scaled results: `value = raw × scale + offset − tare`
- Tare/calibration: `ApplyTareAsync()` (averaged samples), `ApplyTare()`, `SetTare()`, `ClearTare()`
- Retries with Polly (constant/linear/exponential backoff, jitter) and automatic reconnect when the link drops
- Meaningful exceptions: `DeviceTimeoutException`, `DeviceConnectionException`, `DeviceSlaveException`…
- Modbus RTU (serial port), Modbus TCP and RTU-over-TCP (serial-to-Ethernet converters), built on NModbus
- Fully async and thread-safe; several devices can share one RS-485 line (transport)
- Field-ready: partial reads, automatic splitting of rejected blocks, opt-in writes, device probing and bus scanning

## Quick start

```csharp
using ModbusDeviceKit;

await using var reader = await DeviceReader.CreateFromFileAsync("device-profile.json");
await reader.ConnectAsync();

DeviceReading reading = await reader.ReadAsync();
double force = reading["Force"];                     // scaled value with tare applied
RegisterValue temp = reading.GetRegister("Temperature");
Console.WriteLine($"{temp.Value} {temp.Unit} (raw: {temp.RawValue})");

Dictionary<string, double> values = reading.ToDictionary();

await reader.ApplyTareAsync(samples: 10);            // zero every register with "allowTare": true
```

To create the transport yourself (e.g. several devices on the same RS-485 line):

```csharp
using System.IO.Ports;
using ModbusDeviceKit.Profiles;
using ModbusDeviceKit.Transport;

await using var bus = new ModbusRtuTransport("COM3", 19200, Parity.Even);
using var loadCell = new DeviceReader(DeviceProfile.LoadFromFile("loadcell.json"), bus);
using var torque   = new DeviceReader(DeviceProfile.LoadFromFile("torque.json"), bus);
```

## Profile format

```jsonc
{
  "deviceName": "LoadCell_XYZ123",       // required
  "protocol": "ModbusRTU",               // ModbusRTU | ModbusTCP | ModbusRtuOverTcp
  "slaveId": 1,
  "byteOrder": "ABCD",                   // default byte order: ABCD | CDAB | BADC | DCBA

  "connection": {                        // used by DeviceReader.Create / ModbusTransportFactory
    "serial": { "portName": "COM3", "baudRate": 9600, "parity": "None", "dataBits": 8, "stopBits": "One" },
    "tcp":    { "host": "192.168.1.50", "port": 502 },
    "connectTimeoutMs": 3000, "readTimeoutMs": 500, "writeTimeoutMs": 500
  },

  "retry": { "maxRetries": 3, "delayMs": 100, "maxDelayMs": 2000, "backoff": "Exponential", "useJitter": true },

  "readOptions": { "maxRegistersPerRead": 120, "maxAddressGap": 0, "interRequestDelayMs": 5,
                   "partialReads": true, "splitRejectedBlocks": true },

  "registers": [
    { "name": "Force", "address": 100, "dataType": "Float32", "scale": 0.01, "unit": "N", "allowTare": true },
    { "name": "Temperature", "address": 102, "dataType": "Int16", "scale": 0.1, "offset": -0.5, "unit": "C" },
    { "name": "AdcCounts", "address": 200, "registerType": "Input", "dataType": "Int32", "byteOrder": "CDAB" },
    { "name": "Overload", "address": 0, "registerType": "DiscreteInput", "dataType": "Bool" },
    { "name": "Setpoint", "address": 300, "dataType": "Int16", "scale": 0.1, "unit": "N", "writable": true }
  ]
}
```

| Register field | Default | Description |
|---|---|---|
| `name` | — | Unique name (case-insensitive) |
| `address` | — | **Zero-based** protocol address (40101 → 100) |
| `registerType` | `Holding` | `Holding` (FC03), `Input` (FC04), `Coil` (FC01), `DiscreteInput` (FC02) |
| `dataType` | `UInt16` | `Bool` is required for Coil/DiscreteInput |
| `byteOrder` | profile value | Per-register byte order |
| `scale` / `offset` | 1 / 0 | Calibration: `raw × scale + offset` |
| `tare` / `allowTare` | 0 / false | Initial tare; include the register in `ApplyTare()` (tare all) |
| `writable` | false | Allow `WriteAsync` (Holding and Coil only) |
| `unit`, `description` | — | Informational |

Profiles are validated on load. Unknown fields (e.g. a `"scael"` typo) and inconsistent values are reported
together in a single `DeviceProfileException` with the full error list. Comments are allowed in the JSON.

## Error handling

Every connect/read operation is retried according to the `retry` settings. Timeouts, CRC/IO errors, dropped
connections, "Slave Device Busy" (6), "Acknowledge" (5) and "Gateway Target Failed To Respond" (11) are treated as
transient. Requests the device explicitly rejects, such as "Illegal Data Address", are not retried.

| Exception | When |
|---|---|
| `DeviceTimeoutException` | The device did not answer in any attempt |
| `DeviceConnectionException` | The port/socket could not be opened |
| `DeviceSlaveException` | The device returned a Modbus exception response (`ExceptionCode`, `ExceptionName`) |
| `DeviceCommunicationException` | CRC/framing/IO error (also the base class of the timeout and slave exceptions) |
| `DeviceProfileException` | The profile is invalid (`Errors` list) |

All of them derive from `ModbusDeviceKitException`. Communication errors carry `DeviceName`, `SlaveId` and
`Attempts`; connection errors carry `Endpoint` and `Attempts`.

## Field features

**Partial reads.** With `"partialReads": true`, a request that still fails after all retries no longer fails the
whole reading: its registers come back with `IsValid = false`, a `NaN` value and an `Error` text, while the rest is
read normally. An exception is thrown only when nothing at all could be read.

```csharp
var reading = await reader.ReadAsync();
if (!reading.IsComplete)
    foreach (var failed in reading.FailedRegisters)
        Console.WriteLine($"{failed.Name}: {failed.Error}");
```

**Rejected blocks.** Many devices have holes in their register map and refuse a multi-register request that covers
them. With `"splitRejectedBlocks": true` (default), when the device answers "Illegal Data Address" or "Illegal Data
Value" to such a request, its registers are read one by one — and from then on without asking for the range again.

**Writes.** Registers marked `"writable": true` can be written with engineering values; the library converts back to
raw units (`(value − offset) / scale`) and picks function 06, 16 or 05. Writes are opt-in, so a typo can never change
a device setting.

```csharp
await reader.WriteAsync("Setpoint", 125.5);   // N → raw 1255 → FC06
```

**Probing and bus scan.** `ProbeAsync` checks whether a device answers and explains what to check when it does not.
A Modbus exception answer counts as "responded": the device is online, only the address is wrong.
`ModbusBusScanner` does the same for a range of slave ids — handy when commissioning an RS-485 line.

```csharp
DeviceProbeResult probe = await reader.ProbeAsync();
Console.WriteLine(probe.Message);

await using var bus = new ModbusRtuTransport("COM3", 19200, Parity.Even);
var found = await ModbusBusScanner.ScanAsync(bus, Enumerable.Range(1, 10).Select(i => (byte)i));
foreach (var result in found.Where(r => r.Responded))
    Console.WriteLine($"Slave {result.SlaveId} is online");
```

## Console sample

```bash
dotnet run --project samples/ModbusDeviceKit.ConsoleSample                       # device-profile.json, every 5 s
dotnet run --project samples/ModbusDeviceKit.ConsoleSample -- my-device.json --interval 2
dotnet run --project samples/ModbusDeviceKit.ConsoleSample -- --simulate         # no hardware, built-in simulator
```

While running: `T` = tare (average of 5 samples), `C` = clear tare, `Q`/`Esc` = quit.

## Build, test, pack

```bash
dotnet build
dotnet test
dotnet pack src/ModbusDeviceKit -c Release -o artifacts
```

## License

[MIT](https://github.com/semihbenerr/ModbusDeviceKit/blob/main/LICENSE) © Semih Bener
