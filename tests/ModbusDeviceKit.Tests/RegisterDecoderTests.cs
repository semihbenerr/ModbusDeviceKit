using ModbusDeviceKit.Profiles;

namespace ModbusDeviceKit.Tests;

public class RegisterDecoderTests
{
    // 123.456f = 0x42F6E979 → bytes A=42 B=F6 C=E9 D=79
    [Theory]
    [InlineData(ByteOrder.ABCD, (ushort)0x42F6, (ushort)0xE979)]
    [InlineData(ByteOrder.CDAB, (ushort)0xE979, (ushort)0x42F6)]
    [InlineData(ByteOrder.BADC, (ushort)0xF642, (ushort)0x79E9)]
    [InlineData(ByteOrder.DCBA, (ushort)0x79E9, (ushort)0xF642)]
    public void Decodes_Float32_in_every_byte_order(ByteOrder order, ushort w0, ushort w1)
    {
        double value = RegisterDecoder.Decode(new[] { w0, w1 }, RegisterDataType.Float32, order);

        Assert.Equal(123.456f, (float)value);
    }

    [Fact]
    public void Decodes_signed_and_unsigned_16_bit()
    {
        Assert.Equal(-2, RegisterDecoder.Decode(new ushort[] { 0xFFFE }, RegisterDataType.Int16));
        Assert.Equal(65534, RegisterDecoder.Decode(new ushort[] { 0xFFFE }, RegisterDataType.UInt16));
        Assert.Equal(-2, RegisterDecoder.Decode(new ushort[] { 0xFEFF }, RegisterDataType.Int16, ByteOrder.BADC));
    }

    [Fact]
    public void Decodes_Int32_word_swapped()
    {
        // 0x00012345 = 74565, word swapped on the wire: 0x2345 0x0001
        Assert.Equal(74565, RegisterDecoder.Decode(new ushort[] { 0x2345, 0x0001 }, RegisterDataType.Int32, ByteOrder.CDAB));
    }

    [Theory]
    [InlineData(RegisterDataType.Int16, -1234.0)]
    [InlineData(RegisterDataType.UInt16, 60000.0)]
    [InlineData(RegisterDataType.Int32, -123456789.0)]
    [InlineData(RegisterDataType.UInt32, 4000000000.0)]
    [InlineData(RegisterDataType.Float32, 3.5)]
    [InlineData(RegisterDataType.Int64, -9007199254740991.0)]
    [InlineData(RegisterDataType.UInt64, 9007199254740991.0)]
    [InlineData(RegisterDataType.Float64, 1.0 / 3.0)]
    public void Encode_and_decode_round_trip(RegisterDataType type, double value)
    {
        foreach (var order in Enum.GetValues<ByteOrder>())
        {
            ushort[] words = RegisterDecoder.Encode(value, type, order);

            Assert.Equal(type.GetRegisterCount(), words.Length);
            Assert.Equal(value, RegisterDecoder.Decode(words, type, order));
        }
    }

    [Fact]
    public void Encode_rejects_values_out_of_range()
    {
        Assert.Throws<OverflowException>(() => RegisterDecoder.Encode(40000, RegisterDataType.Int16));
        Assert.Throws<OverflowException>(() => RegisterDecoder.Encode(-1, RegisterDataType.UInt16));
        Assert.Throws<OverflowException>(() => RegisterDecoder.Encode(double.NaN, RegisterDataType.Int32));
    }

    [Fact]
    public void Decode_requires_enough_registers()
    {
        Assert.Throws<ArgumentException>(() => RegisterDecoder.Decode(new ushort[] { 1 }, RegisterDataType.Float32));
    }
}
