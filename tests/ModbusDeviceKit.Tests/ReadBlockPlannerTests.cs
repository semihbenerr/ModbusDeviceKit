using ModbusDeviceKit.Internal;
using ModbusDeviceKit.Profiles;

namespace ModbusDeviceKit.Tests;

public class ReadBlockPlannerTests
{
    private static RegisterDefinition Reg(string name, ushort address, RegisterDataType type = RegisterDataType.UInt16,
        RegisterType table = RegisterType.Holding) =>
        new() { Name = name, Address = address, DataType = type, RegisterType = table };

    [Fact]
    public void Merges_adjacent_registers_into_one_request()
    {
        var blocks = ReadBlockPlanner.Plan(
            new[] { Reg("Force", 100, RegisterDataType.Float32), Reg("Temp", 102, RegisterDataType.Int16), Reg("Status", 103) },
            new ReadOptions());

        var block = Assert.Single(blocks);
        Assert.Equal(100, block.StartAddress);
        Assert.Equal(4, block.Count);
        Assert.Equal(3, block.Registers.Count);
    }

    [Fact]
    public void Splits_on_gaps_unless_allowed()
    {
        var registers = new[] { Reg("A", 10), Reg("B", 13) };

        Assert.Equal(2, ReadBlockPlanner.Plan(registers, new ReadOptions()).Count);

        var merged = Assert.Single(ReadBlockPlanner.Plan(registers, new ReadOptions { MaxAddressGap = 2 }));
        Assert.Equal(10, merged.StartAddress);
        Assert.Equal(4, merged.Count);
    }

    [Fact]
    public void Respects_maximum_request_size_and_register_tables()
    {
        var registers = Enumerable.Range(0, 10).Select(i => Reg($"H{i}", (ushort)i))
            .Append(Reg("I0", 0, table: RegisterType.Input))
            .Append(Reg("D0", 0, RegisterDataType.Bool, RegisterType.DiscreteInput));

        var blocks = ReadBlockPlanner.Plan(registers, new ReadOptions { MaxRegistersPerRead = 4 });

        Assert.Equal(new[] { 4, 4, 2 }, blocks.Where(b => b.RegisterType == RegisterType.Holding).Select(b => (int)b.Count));
        Assert.Single(blocks, b => b.RegisterType == RegisterType.Input);
        Assert.Single(blocks, b => b.RegisterType == RegisterType.DiscreteInput);
    }

    [Fact]
    public void Overlapping_registers_share_a_request()
    {
        var blocks = ReadBlockPlanner.Plan(
            new[] { Reg("AsFloat", 0, RegisterDataType.Float32), Reg("HighWord", 0), Reg("LowWord", 1) },
            new ReadOptions());

        var block = Assert.Single(blocks);
        Assert.Equal(2, block.Count);
    }
}
