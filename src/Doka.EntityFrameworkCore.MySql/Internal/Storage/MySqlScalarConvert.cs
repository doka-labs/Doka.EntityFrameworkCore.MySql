namespace Doka.EntityFrameworkCore.MySql;

internal static class MySqlScalarConvert
{
    public static bool ToBoolean(
        object? value
    ) => value switch
    {
        null => false,
        bool boolValue => boolValue,
        sbyte sbyteValue => sbyteValue != 0,
        byte byteValue => byteValue != 0,
        short shortValue => shortValue != 0,
        ushort ushortValue => ushortValue != 0,
        int intValue => intValue != 0,
        uint uintValue => uintValue != 0,
        long longValue => longValue != 0,
        ulong ulongValue => ulongValue != 0,
        float floatValue => floatValue != 0,
        double doubleValue => doubleValue != 0,
        decimal decimalValue => decimalValue != 0,
        string s => s is "1" or "true" or "TRUE" or "True",
        _ => throw new InvalidOperationException(
            $"Cannot convert value of type '{value.GetType().FullName}' to Boolean."),
    };
}
