namespace ModbusDeviceKit.Profiles;

/// <summary>Physical/framing protocol used to talk to the device.</summary>
public enum ModbusProtocol
{
    /// <summary>Modbus RTU over a serial line (RS-485 / RS-232 / USB-serial).</summary>
    ModbusRTU,

    /// <summary>Modbus TCP (MBAP header) over Ethernet.</summary>
    ModbusTCP,

    /// <summary>Modbus RTU frames tunnelled over a raw TCP socket (serial-to-Ethernet gateways in transparent mode).</summary>
    ModbusRtuOverTcp,
}

/// <summary>Modbus data table a register lives in.</summary>
public enum RegisterType
{
    /// <summary>Holding registers (function code 03). 16-bit, read/write.</summary>
    Holding,

    /// <summary>Input registers (function code 04). 16-bit, read-only.</summary>
    Input,

    /// <summary>Coils (function code 01). Single bit, read/write.</summary>
    Coil,

    /// <summary>Discrete inputs (function code 02). Single bit, read-only.</summary>
    DiscreteInput,
}

/// <summary>How the raw register content is interpreted.</summary>
public enum RegisterDataType
{
    /// <summary>Single bit. Only valid for <see cref="RegisterType.Coil"/> and <see cref="RegisterType.DiscreteInput"/>.</summary>
    Bool,

    /// <summary>Signed 16-bit integer (1 register).</summary>
    Int16,

    /// <summary>Unsigned 16-bit integer (1 register).</summary>
    UInt16,

    /// <summary>Signed 32-bit integer (2 registers).</summary>
    Int32,

    /// <summary>Unsigned 32-bit integer (2 registers).</summary>
    UInt32,

    /// <summary>IEEE-754 single precision float (2 registers).</summary>
    Float32,

    /// <summary>Signed 64-bit integer (4 registers). Values above 2^53 lose precision when converted to <see cref="double"/>.</summary>
    Int64,

    /// <summary>Unsigned 64-bit integer (4 registers). Values above 2^53 lose precision when converted to <see cref="double"/>.</summary>
    UInt64,

    /// <summary>IEEE-754 double precision float (4 registers).</summary>
    Float64,
}

/// <summary>
/// Byte order of multi-byte values. Letters describe how the big-endian bytes <c>A B C D</c>
/// of a value appear on the wire. For 64-bit values the word/byte swapping applies to all four words.
/// For 16-bit values only the byte swap (<see cref="BADC"/>, <see cref="DCBA"/>) has an effect.
/// </summary>
public enum ByteOrder
{
    /// <summary>Big-endian, the Modbus standard ("AB CD").</summary>
    ABCD,

    /// <summary>Word swapped ("CD AB"), common on many PLCs and meters.</summary>
    CDAB,

    /// <summary>Byte swapped inside each word ("BA DC").</summary>
    BADC,

    /// <summary>Fully little-endian ("DC BA").</summary>
    DCBA,
}

/// <summary>Delay growth between retry attempts.</summary>
public enum RetryBackoff
{
    /// <summary>Same delay before every retry.</summary>
    Constant,

    /// <summary>Delay grows linearly (delay, 2×delay, 3×delay…).</summary>
    Linear,

    /// <summary>Delay doubles on every retry (delay, 2×delay, 4×delay…).</summary>
    Exponential,
}
