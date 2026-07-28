namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Backtick-quoted identifier surface for MySQL and MariaDB. The hot path elides the
/// per-call string.Replace plus interpolated-string allocation that the EF Core base
/// type emits, because identifier escaping fires on every query-translation pass.
/// The no-backtick-in-identifier case (overwhelmingly the common case) routes through
/// direct StringBuilder appends or a single string.Create span fill; the slow path
/// (an identifier that already contains a backtick) doubles the backtick character
/// per-char into the same builder without allocating an intermediate string.
/// </summary>
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

        if (identifier.AsSpan().IndexOf('`') < 0)
        {
            return string.Create(
                identifier.Length + 2,
                identifier,
                static (span, source) =>
                {
                    span[0] = '`';
                    source.AsSpan().CopyTo(span[1..^1]);
                    span[^1] = '`';
                });
        }

        var builder = new StringBuilder(identifier.Length + 8);
        builder.Append('`');
        AppendEscapedIdentifier(builder, identifier);
        builder.Append('`');
        return builder.ToString();
    }

    public override string EscapeIdentifier(
        string identifier
    ) => MySqlIdentifierEscaping.EscapeBackticks(identifier);

    public override void EscapeIdentifier(
        StringBuilder builder,
        string identifier
    )
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);

        AppendEscapedIdentifier(builder, identifier);
    }

    public override void DelimitIdentifier(
        StringBuilder builder,
        string identifier
    )
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);

        builder.Append('`');
        AppendEscapedIdentifier(builder, identifier);
        builder.Append('`');
    }

    public override string DelimitIdentifier(
        string name,
        string? schema
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (string.IsNullOrWhiteSpace(schema))
        {
            return DelimitIdentifier(name);
        }

        var builder = new StringBuilder(name.Length + schema.Length + 5);
        DelimitIdentifier(builder, schema);
        builder.Append('.');
        DelimitIdentifier(builder, name);
        return builder.ToString();
    }

    public override void DelimitIdentifier(
        StringBuilder builder,
        string name,
        string? schema
    )
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (!string.IsNullOrWhiteSpace(schema))
        {
            DelimitIdentifier(builder, schema);
            builder.Append('.');
        }

        DelimitIdentifier(builder, name);
    }

    private static void AppendEscapedIdentifier(
        StringBuilder builder,
        string identifier
    )
    {
        var span = identifier.AsSpan();
        var firstBacktick = span.IndexOf('`');

        if (firstBacktick < 0)
        {
            builder.Append(identifier);
            return;
        }

        if (firstBacktick > 0)
        {
            builder.Append(identifier, 0, firstBacktick);
        }

        for (var index = firstBacktick; index < identifier.Length; index++)
        {
            var character = identifier[index];
            if (character == '`')
            {
                builder
                    .Append('`')
                    .Append('`');
            }
            else
            {
                builder.Append(character);
            }
        }
    }
}
