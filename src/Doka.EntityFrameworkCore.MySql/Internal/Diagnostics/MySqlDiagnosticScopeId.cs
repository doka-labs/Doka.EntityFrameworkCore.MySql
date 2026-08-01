namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Creates stable diagnostic pseudonyms for database and model object names.
/// </summary>
/// <remarks>
/// The 16-character identifier is derived from SHA-256 and is intended only for
/// correlating the small set of objects represented in provider diagnostics. It
/// is not a security token and must not be used for authorization or equality in
/// persisted application data.
/// </remarks>
internal static class MySqlDiagnosticScopeId
{
    /// <summary>
    /// Creates a scope identifier for one logical component.
    /// </summary>
    public static string Create(
        string component
    )
    {
        ArgumentNullException.ThrowIfNull(component);

        return Hash(component);
    }

    /// <summary>
    /// Creates a scope identifier for two ordered logical components.
    /// </summary>
    public static string Create(
        string first,
        string second
    )
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);

        var value = new StringBuilder(first.Length + second.Length + 24);
        AppendComponent(value, first);
        AppendComponent(value, second);

        return Hash(value.ToString());
    }

    /// <summary>
    /// Creates a scope identifier for three ordered logical components.
    /// </summary>
    public static string Create(
        string first,
        string second,
        string third
    )
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        ArgumentNullException.ThrowIfNull(third);

        var value = new StringBuilder(first.Length + second.Length + third.Length + 36);
        AppendComponent(value, first);
        AppendComponent(value, second);
        AppendComponent(value, third);

        return Hash(value.ToString());
    }

    private static void AppendComponent(
        StringBuilder destination,
        string component
    ) => destination
        .Append(component.Length)
        .Append(':')
        .Append(component);

    private static string Hash(
        string value
    )
    {
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));

        return Convert.ToHexStringLower(hashBytes.AsSpan(0, 8));
    }
}
