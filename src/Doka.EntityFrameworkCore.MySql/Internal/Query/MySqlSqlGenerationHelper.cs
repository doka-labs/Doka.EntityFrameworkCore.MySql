namespace Doka.EntityFrameworkCore.MySql;

internal sealed class MySqlSqlGenerationHelper : RelationalSqlGenerationHelper
{
    public MySqlSqlGenerationHelper(
        RelationalSqlGenerationHelperDependencies dependencies
    ) : base(dependencies) { }

    public override string DelimitIdentifier(
        string identifier
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);

        return $"`{EscapeIdentifier(identifier)}`";
    }

    public override string EscapeIdentifier(
        string identifier
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);

        return identifier.Replace("`", "``", StringComparison.Ordinal);
    }

    public override void EscapeIdentifier(
        StringBuilder builder,
        string identifier
    )
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Append(EscapeIdentifier(identifier));
    }

    public override void DelimitIdentifier(
        StringBuilder builder,
        string identifier
    )
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);

        builder.Append(DelimitIdentifier(identifier));
    }

    public override string DelimitIdentifier(
        string name,
        string? schema
    ) => !string.IsNullOrWhiteSpace(schema)
        ? $"{DelimitIdentifier(schema)}.{DelimitIdentifier(name)}"
        : DelimitIdentifier(name);

    public override void DelimitIdentifier(
        StringBuilder builder,
        string name,
        string? schema
    )
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Append(DelimitIdentifier(name, schema));
    }
}
