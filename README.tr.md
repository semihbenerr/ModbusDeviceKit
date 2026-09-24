# ModbusDeviceKit

[🇬🇧 English](https://github.com/semihbenerr/ModbusDeviceKit/blob/main/README.md) | 🇹🇷 **Türkçe**

[![NuGet](https://img.shields.io/nuget/v/ModbusDeviceKit.svg)](https://www.nuget.org/packages/ModbusDeviceKit)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](https://github.com/semihbenerr/ModbusDeviceKit/blob/main/LICENSE)

```bash
dotnet add package ModbusDeviceKit
```

Modbus RTU/TCP cihazlarını (load cell, tork sensörü, sıcaklık/basınç transmitteri…) **JSON profil dosyalarıyla**
tanımlayıp okuyan bir .NET 8 kütüphanesi. Yeni bir cihaz markası geldiğinde register haritasını koda yazmak yerine
bir JSON profili eklemeniz yeterli.

- Tüm register'ları tek çağrıda okur; bitişik register'ları **tek Modbus isteğinde** birleştirir
- `Int16/UInt16/Int32/UInt32/Float32/Int64/UInt64/Float64/Bool` ve `ABCD/CDAB/BADC/DCBA` byte order
- `değer = ham × scale + offset − tare` ile ölçeklenmiş, isimlendirilmiş sonuç
- Tare/kalibrasyon: `ApplyTareAsync()` (örnek ortalamalı), `ApplyTare()`, `SetTare()`, `ClearTare()`
- Polly ile retry (sabit/lineer/üstel bekleme, jitter), bağlantı kopunca otomatik yeniden bağlanma
- Anlamlı exception'lar: `DeviceTimeoutException`, `DeviceConnectionException`, `DeviceSlaveException`…
- Modbus RTU (seri port), Modbus TCP ve RTU-over-TCP (seri/Ethernet dönüştürücüler); altyapı NModbus
- Tamamen async API, thread-safe; birden fazla cihaz aynı RS-485 hattını (transport'u) paylaşabilir

## Hızlı başlangıç

```csharp
using ModbusDeviceKit;

await using var reader = await DeviceReader.CreateFromFileAsync("device-profile.json");
await reader.ConnectAsync();

DeviceReading reading = await reader.ReadAsync();
double force = reading["Force"];                     // ölçeklenmiş + tare uygulanmış değer
RegisterValue temp = reading.GetRegister("Temperature");
Console.WriteLine($"{temp.Value} {temp.Unit} (ham: {temp.RawValue})");

Dictionary<string, double> values = reading.ToDictionary();

await reader.ApplyTareAsync(samples: 10);            // "allowTare": true olan register'ları sıfırla
```

Transport'u kendiniz oluşturmak isterseniz (ör. aynı RS-485 hattında birden fazla cihaz):

```csharp
using System.IO.Ports;
using ModbusDeviceKit.Profiles;
using ModbusDeviceKit.Transport;

await using var bus = new ModbusRtuTransport("COM3", 19200, Parity.Even);
using var loadCell = new DeviceReader(DeviceProfile.LoadFromFile("loadcell.json"), bus);
using var torque   = new DeviceReader(DeviceProfile.LoadFromFile("torque.json"), bus);
```

## Profil formatı

```jsonc
{
  "deviceName": "LoadCell_XYZ123",       // zorunlu
  "protocol": "ModbusRTU",               // ModbusRTU | ModbusTCP | ModbusRtuOverTcp
  "slaveId": 1,
  "byteOrder": "ABCD",                   // varsayılan byte order: ABCD | CDAB | BADC | DCBA

  "connection": {                        // DeviceReader.Create / ModbusTransportFactory için
    "serial": { "portName": "COM3", "baudRate": 9600, "parity": "None", "dataBits": 8, "stopBits": "One" },
    "tcp":    { "host": "192.168.1.50", "port": 502 },
    "connectTimeoutMs": 3000, "readTimeoutMs": 500, "writeTimeoutMs": 500
  },

  "retry": { "maxRetries": 3, "delayMs": 100, "maxDelayMs": 2000, "backoff": "Exponential", "useJitter": true },

  "readOptions": { "maxRegistersPerRead": 120, "maxAddressGap": 0, "interRequestDelayMs": 5 },

  "registers": [
    { "name": "Force", "address": 100, "dataType": "Float32", "scale": 0.01, "unit": "N", "allowTare": true },
    { "name": "Temperature", "address": 102, "dataType": "Int16", "scale": 0.1, "offset": -0.5, "unit": "C" },
    { "name": "AdcCounts", "address": 200, "registerType": "Input", "dataType": "Int32", "byteOrder": "CDAB" },
    { "name": "Overload", "address": 0, "registerType": "DiscreteInput", "dataType": "Bool" }
  ]
}
```

| Register alanı | Varsayılan | Açıklama |
|---|---|---|
| `name` | — | Benzersiz isim (büyük/küçük harf duyarsız) |
| `address` | — | **0 tabanlı** protokol adresi (40101 → 100) |
| `registerType` | `Holding` | `Holding` (FC03), `Input` (FC04), `Coil` (FC01), `DiscreteInput` (FC02) |
| `dataType` | `UInt16` | Coil/DiscreteInput için `Bool` zorunlu |
| `byteOrder` | profil değeri | Register bazında byte order |
| `scale` / `offset` | 1 / 0 | Kalibrasyon: `ham × scale + offset` |
| `tare` / `allowTare` | 0 / false | Başlangıç darası; `ApplyTare()` ile toplu darada yer alma |
| `unit`, `description` | — | Bilgi amaçlı |

Profil yüklenirken doğrulanır; bilinmeyen alanlar (ör. `"scael"` yazım hatası) ve tutarsız değerler tüm hata
listesiyle birlikte `DeviceProfileException` olarak raporlanır. JSON içinde yorum satırı kullanılabilir.

## Hata yönetimi

Her bağlantı/okuma işlemi `retry` ayarlarına göre tekrar denenir. Timeout, CRC/IO hatası, bağlantı kopması,
"Slave Device Busy" (6), "Acknowledge" (5) ve "Gateway Target Failed To Respond" (11) geçici kabul edilir.
"Illegal Data Address" gibi cihazın açıkça reddettiği istekler tekrar denenmez.

| Exception | Ne zaman |
|---|---|
| `DeviceTimeoutException` | Cihaz tüm denemelerde cevap vermedi |
| `DeviceConnectionException` | Port/soket açılamadı |
| `DeviceSlaveException` | Cihaz Modbus exception cevabı döndü (`ExceptionCode`, `ExceptionName`) |
| `DeviceCommunicationException` | CRC/çerçeve/IO hatası (timeout ve slave hatalarının da temel sınıfı) |
| `DeviceProfileException` | Profil hatalı (`Errors` listesi) |

Tümü `ModbusDeviceKitException`'dan türer. İletişim hataları `DeviceName`, `SlaveId` ve `Attempts`, bağlantı hataları `Endpoint` ve `Attempts` bilgisini taşır.

## Örnek konsol uygulaması

```bash
dotnet run --project samples/ModbusDeviceKit.ConsoleSample                       # device-profile.json, 5 sn aralık
dotnet run --project samples/ModbusDeviceKit.ConsoleSample -- my-device.json --interval 2
dotnet run --project samples/ModbusDeviceKit.ConsoleSample -- --simulate         # donanımsız, dahili simülatör
```

Çalışırken: `T` = dara al (5 örnek ortalaması), `C` = darayı temizle, `Q`/`Esc` = çıkış.

## Derleme, test, paketleme

```bash
dotnet build
dotnet test
dotnet pack src/ModbusDeviceKit -c Release -o artifacts
```

## Lisans

[MIT](https://github.com/semihbenerr/ModbusDeviceKit/blob/main/LICENSE) © Semih Bener
