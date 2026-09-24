namespace ModbusDeviceKit.Profiles;

/// <summary>Helpers for <see cref="RegisterDataType"/>.</summary>
public static class RegisterDataTypeExtensions
{
    /// <summary>Number of 16-bit registers (or bits for <see cref="RegisterDataType.Bool"/>) the type occupies.</summary>
    public static int GetRegisterCount(this RegisterDataType dataType) => dataType switch
    {
        RegisterDataType.Bool => 1,
        RegisterDataType.Int16 or RegisterDataType.UInt16 => 1,
        RegisterDataType.Int32 or RegisterDataType.UInt32 or RegisterDataType.Float32 => 2,
        RegisterDataType.Int64 or RegisterDataType.UInt64 or RegisterDataType.Float64 => 4,
        _ => throw new ArgumentOutOfRangeException(nameof(dataType), dataType, "Unknown register data type."),
    };

    /// <summary><c>true</c> for register types that are read bit-wise (coils and discrete inputs).</summary>
    public static bool IsBitType(this RegisterType registerType) =>
        registerType is RegisterType.Coil or RegisterType.DiscreteInput;
}
