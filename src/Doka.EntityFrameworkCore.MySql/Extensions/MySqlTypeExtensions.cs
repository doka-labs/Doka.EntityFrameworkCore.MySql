namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Provides shared CLR type operations used across provider subsystems.
/// </summary>
internal static class MySqlTypeExtensions
{
    /// <summary>
    /// Returns the underlying value type for <see cref="Nullable{T}"/> or the
    /// supplied type when it is not nullable.
    /// </summary>
    internal static Type UnwrapNullableType(
        this Type type
    )
    {
        ArgumentNullException.ThrowIfNull(type);

        return Nullable.GetUnderlyingType(type) ?? type;
    }
}
