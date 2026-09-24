using ModbusDeviceKit.Profiles;

namespace ModbusDeviceKit.Internal;

/// <summary>One Modbus read request covering one or more registers of the same table.</summary>
internal sealed class ReadBlock
{
    public ReadBlock(RegisterType registerType, ushort startAddress, ushort count, IReadOnlyList<RegisterDefinition> registers)
    {
        RegisterType = registerType;
        StartAddress = startAddress;
        Count = count;
        Registers = registers;
    }

    public RegisterType RegisterType { get; }

    public ushort StartAddress { get; }

    public ushort Count { get; }

    public IReadOnlyList<RegisterDefinition> Registers { get; }

    public bool IsBitBlock => RegisterType.IsBitType();

    public override string ToString()
    {
        string table = RegisterType switch
        {
            RegisterType.Holding => "holding registers",
            RegisterType.Input => "input registers",
            RegisterType.Coil => "coils",
            _ => "discrete inputs",
        };
        return Count == 1 ? $"read {table} {StartAddress}" : $"read {table} {StartAddress}-{StartAddress + Count - 1}";
    }
}

/// <summary>Groups registers into as few Modbus requests as the read options allow.</summary>
internal static class ReadBlockPlanner
{
    /// <summary>Modbus limit for coils / discrete inputs per request.</summary>
    public const int MaxBitsPerRead = 2000;

    public static IReadOnlyList<ReadBlock> Plan(IEnumerable<RegisterDefinition> registers, ReadOptions options)
    {
        var blocks = new List<ReadBlock>();

        foreach (var group in registers.GroupBy(r => r.RegisterType).OrderBy(g => g.Key))
        {
            int maxSize = group.Key.IsBitType() ? MaxBitsPerRead : options.MaxRegistersPerRead;
            var sorted = group.OrderBy(r => r.Address).ThenByDescending(r => r.RegisterCount).ToList();

            int start = -1;
            int end = -1; // exclusive
            var members = new List<RegisterDefinition>();

            foreach (var reg in sorted)
            {
                int regStart = reg.Address;
                int regEnd = reg.Address + reg.RegisterCount;

                bool fits = members.Count > 0
                    && regStart <= end + options.MaxAddressGap
                    && Math.Max(end, regEnd) - start <= maxSize;

                if (!fits)
                {
                    Flush();
                    start = regStart;
                    end = regEnd;
                }
                else
                {
                    end = Math.Max(end, regEnd);
                }

                members.Add(reg);
            }

            Flush();

            void Flush()
            {
                if (members.Count == 0)
                    return;
                blocks.Add(new ReadBlock(group.Key, (ushort)start, (ushort)(end - start), members.ToArray()));
                members.Clear();
            }
        }

        return blocks;
    }
}
