using System.Buffers.Binary;
using ModbusDeviceKit.Profiles;

namespace ModbusDeviceKit;

/// <summary>Converts between raw 16-bit Modbus registers and numeric values.</summary>
public static class RegisterDecoder
{
    /// <summary>Decodes the first <see cref="RegisterDataTypeExtensions.GetRegisterCount"/> words of <paramref name="registers"/>.</summary>
    /// <param name="registers">Registers exactly as received from the device.</param>
    /// <param name="dataType">Type to decode. <see cref="RegisterDataType.Bool"/> is not a register type and is rejected.</param>
    /// <param name="byteOrder">Byte order of the value on the wire.</param>
    public static double Decode(ReadOnlySpan<ushort> registers, RegisterDataType dataType, ByteOrder byteOrder = ByteOrder.ABCD)
    {
        if (dataType == RegisterDataType.Bool)
            throw new ArgumentException("Bool values are read from coils/discrete inputs, not decoded from registers.", nameof(dataType));

        int count = dataType.GetRegisterCount();
        if (registers.Length < count)
            throw new ArgumentException($"{dataType} needs {count} register(s) but only {registers.Length} were supplied.", nameof(registers));

        Span<byte> bytes = stackalloc byte[count * 2];
        ToBigEndianBytes(registers[..count], byteOrder, bytes);

        return dataType switch
        {
            RegisterDataType.Int16 => BinaryPrimitives.ReadInt16BigEndian(bytes),
            RegisterDataType.UInt16 => BinaryPrimitives.ReadUInt16BigEndian(bytes),
            RegisterDataType.Int32 => BinaryPrimitives.ReadInt32BigEndian(bytes),
            RegisterDataType.UInt32 => BinaryPrimitives.ReadUInt32BigEndian(bytes),
            RegisterDataType.Float32 => BinaryPrimitives.ReadSingleBigEndian(bytes),
            RegisterDataType.Int64 => BinaryPrimitives.ReadInt64BigEndian(bytes),
            RegisterDataType.UInt64 => BinaryPrimitives.ReadUInt64BigEndian(bytes),
            RegisterDataType.Float64 => BinaryPrimitives.ReadDoubleBigEndian(bytes),
            _ => throw new ArgumentOutOfRangeException(nameof(dataType), dataType, "Unknown register data type."),
        };
    }

    /// <summary>Encodes a raw (unscaled) value into registers in wire order.</summary>
    /// <exception cref="OverflowException">The value does not fit into an integer <paramref name="dataType"/>.</exception>
    public static ushort[] Encode(double value, RegisterDataType dataType, ByteOrder byteOrder = ByteOrder.ABCD)
    {
        if (dataType == RegisterDataType.Bool)
            throw new ArgumentException("Bool values are written to coils, not encoded into registers.", nameof(dataType));

        int count = dataType.GetRegisterCount();
        Span<byte> bytes = stackalloc byte[count * 2];

        switch (dataType)
        {
            case RegisterDataType.Int16:
                BinaryPrimitives.WriteInt16BigEndian(bytes, checked((short)RoundInteger(value)));
                break;
            case RegisterDataType.UInt16:
                BinaryPrimitives.WriteUInt16BigEndian(bytes, checked((ushort)RoundInteger(value)));
                break;
            case RegisterDataType.Int32:
                BinaryPrimitives.WriteInt32BigEndian(bytes, checked((int)RoundInteger(value)));
                break;
            case RegisterDataType.UInt32:
                BinaryPrimitives.WriteUInt32BigEndian(bytes, checked((uint)RoundInteger(value)));
                break;
            case RegisterDataType.Float32:
                BinaryPrimitives.WriteSingleBigEndian(bytes, (float)value);
                break;
            case RegisterDataType.Int64:
                BinaryPrimitives.WriteInt64BigEndian(bytes, checked((long)RoundInteger(value)));
                break;
            case RegisterDataType.UInt64:
                BinaryPrimitives.WriteUInt64BigEndian(bytes, checked((ulong)RoundInteger(value)));
                break;
            case RegisterDataType.Float64:
                BinaryPrimitives.WriteDoubleBigEndian(bytes, value);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(dataType), dataType, "Unknown register data type.");
        }

        var registers = new ushort[count];
        FromBigEndianBytes(bytes, byteOrder, registers);
        return registers;
    }

    private static double RoundInteger(double value)
    {
        if (!double.IsFinite(value))
            throw new OverflowException($"{value} cannot be stored in an integer register.");
        return Math.Round(value, MidpointRounding.AwayFromZero);
    }

    // Word i of the big-endian byte image comes from wire word (count-1-i) when words are swapped,
    // and its two bytes are exchanged when bytes are swapped.
    private static void ToBigEndianBytes(ReadOnlySpan<ushort> words, ByteOrder order, Span<byte> destination)
    {
        (bool swapWords, bool swapBytes) = GetSwaps(order);
        int count = words.Length;
        for (int i = 0; i < count; i++)
        {
            ushort word = words[swapWords ? count - 1 - i : i];
            byte hi = (byte)(word >> 8);
            byte lo = (byte)word;
            destination[2 * i] = swapBytes ? lo : hi;
            destination[2 * i + 1] = swapBytes ? hi : lo;
        }
    }

    private static void FromBigEndianBytes(ReadOnlySpan<byte> bytes, ByteOrder order, Span<ushort> destination)
    {
        (bool swapWords, bool swapBytes) = GetSwaps(order);
        int count = destination.Length;
        for (int i = 0; i < count; i++)
        {
            byte first = bytes[2 * i];
            byte second = bytes[2 * i + 1];
            ushort word = swapBytes ? (ushort)((second << 8) | first) : (ushort)((first << 8) | second);
            destination[swapWords ? count - 1 - i : i] = word;
        }
    }

    private static (bool SwapWords, bool SwapBytes) GetSwaps(ByteOrder order) => order switch
    {
        ByteOrder.ABCD => (false, false),
        ByteOrder.CDAB => (true, false),
        ByteOrder.BADC => (false, true),
        ByteOrder.DCBA => (true, true),
        _ => throw new ArgumentOutOfRangeException(nameof(order), order, "Unknown byte order."),
    };
}
