using FsCheck.Xunit;

namespace Doka.EntityFrameworkCore.MySql.Tests.Properties;

/// <summary>
/// Property-style coverage for <see cref="MySqlSqlGenerationHelper.DelimitIdentifier"/>.
/// The MySQL identifier-quoting contract is "wrap in backticks, double any backticks
/// inside the name". The round-trip property is: stripping the outer backticks and
/// undoing doubled backticks recovers the input.
/// </summary>
public sealed class MySqlSqlGenerationHelperPropertyTests
{
    // EF1001: RelationalSqlGenerationHelperDependencies is internal-API. Test code
    // legitimately instantiates the provider helper directly to exercise its identifier
    // contract without standing up a full DI container; the dependency type is a marker
    // class with no constructor parameters that affect behavior.
#pragma warning disable EF1001
    private static readonly MySqlSqlGenerationHelper s_helper = new(new RelationalSqlGenerationHelperDependencies());
#pragma warning restore EF1001

    /// <summary>Verifies quoted identifier roundtrips across generated nonblank names.</summary>
    [Property(MaxTest = 1000)]
    public bool DelimitIdentifier_round_trips_arbitrary_non_blank_identifier(
        string? identifier
    )
    {
        // Arrange
        if (string.IsNullOrWhiteSpace(identifier))
        {
            return true;
        }

        // Act
        var delimited = s_helper.DelimitIdentifier(identifier);

        // Assert
        return delimited.StartsWith('`')
            && delimited.EndsWith('`')
            && UnescapeBackticked(delimited[1..^1]) == identifier;
    }

    /// <summary>Verifies that quoting doubles every backtick in generated names.</summary>
    [Property(MaxTest = 1000)]
    public bool DelimitIdentifier_doubles_every_internal_backtick(
        string? identifier
    )
    {
        // Arrange
        if (string.IsNullOrWhiteSpace(identifier))
        {
            return true;
        }

        var inputBackticks = identifier.Count(c => c == '`');

        // Act
        var delimited = s_helper.DelimitIdentifier(identifier);

        // Assert
        var inner = delimited[1..^1];
        var innerBackticks = inner.Count(c => c == '`');

        return innerBackticks == inputBackticks * 2;
    }

    /// <summary>Verifies separate schema and object quoting across generated identifier pairs.</summary>
    [Property(MaxTest = 1000)]
    public bool DelimitIdentifier_with_schema_round_trips(
        string? schema,
        string? name
    )
    {
        // Arrange
        if (string.IsNullOrWhiteSpace(schema)
            || string.IsNullOrWhiteSpace(name))
        {
            return true;
        }

        // Act
        var delimited = s_helper.DelimitIdentifier(name, schema);

        // Assert
        var separatorIndex = FindSchemaSeparator(delimited);

        if (separatorIndex < 0)
        {
            return false;
        }

        var schemaPart = delimited[..separatorIndex];
        var namePart = delimited[(separatorIndex + 1)..];

        return schemaPart.StartsWith('`')
            && schemaPart.EndsWith('`')
            && namePart.StartsWith('`')
            && namePart.EndsWith('`')
            && UnescapeBackticked(schemaPart[1..^1]) == schema
            && UnescapeBackticked(namePart[1..^1]) == name;
    }

    /// <summary>Reverses backtick doubling without invoking the provider encoder.</summary>
    private static string UnescapeBackticked(
        string value
    ) => value.Replace("``", "`", StringComparison.Ordinal);

    /// <summary>Finds the schema separator outside quoted and doubled-backtick spans.</summary>
    private static int FindSchemaSeparator(
        string twoPart
    )
    {
        // WHY: A dot within a quoted name is data. Track backtick parity so the first dot outside a
        // backtick-delimited span is the schema / name separator.
        var inBackticks = false;

        for (var index = 0; index < twoPart.Length; index++)
        {
            var character = twoPart[index];

            switch (character)
            {
                case '`' when inBackticks
                    && index + 1 < twoPart.Length
                    && twoPart[index + 1] == '`':
                    index++;
                    continue;
                case '`':
                    inBackticks = !inBackticks;
                    continue;
                case '.'
                    when !inBackticks:
                    return index;
            }
        }

        return -1;
    }
}
