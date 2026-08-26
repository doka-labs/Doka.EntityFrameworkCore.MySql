namespace Doka.Caching.MySql;

internal static class MySqlCacheIdentifier
{
    private const int MaximumIdentifierLength = 64;

    public static string GetQualifiedName(
        string schemaName,
        string tableName
    ) => $"{Quote(schemaName, nameof(schemaName))}.{Quote(tableName, nameof(tableName))}";

    public static string Quote(
        string identifier,
        string parameterName
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier, parameterName);

        if (identifier.Length > MaximumIdentifierLength)
        {
            throw new ArgumentException(
                $"MySQL identifiers cannot exceed {MaximumIdentifierLength} characters.",
                parameterName);
        }

        if (identifier[^1] == ' ')
        {
            throw new ArgumentException("MySQL identifiers cannot end with a space.", parameterName);
        }

        foreach (var character in identifier)
        {
            if (character == '\0'
                || char.IsSurrogate(character))
            {
                throw new ArgumentException(
                    "MySQL identifiers require BMP characters other than the null character.",
                    parameterName);
            }
        }

        return $"`{identifier.Replace("`", "``", StringComparison.Ordinal)}`";
    }
}
