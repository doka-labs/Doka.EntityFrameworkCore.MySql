namespace Doka.EntityFrameworkCore.MySql.Tests;

/// <summary>Contains text-token, final-pair and immutable setter-order controls.</summary>
public sealed partial class MySqlDefaultTextOptionsTests
{
    /// <summary>Invalid tokens fail locally and never become interpolated SQL or exception payloads.</summary>
    /// <param name="value">The invalid charset/collation token.</param>
    /// <param name="charSet">Whether to call the charset setter.</param>
    [Theory]
    [InlineData("", true)]
    [InlineData(" ", false)]
    [InlineData("utf8mb4; SELECT secret", true)]
    [InlineData("utf8mb4_bin COLLATE latin1_swedish_ci", false)]
    [InlineData("`utf8mb4`", true)]
    [InlineData("utf8mb4\nsecret", false)]
    public void Invalid_text_option_tokens_are_rejected(
        string value,
        bool charSet
    )
    {
        // Arrange
        var builder = CreateOptions(null, null);
        var provider = new MySqlDbContextOptionsBuilder(builder);

        // Act
        var exception = Record.Exception(() => charSet
            ? provider.DefaultCharSet(value)
            : provider.DefaultCollation(value));

        // Assert
        Assert.NotNull(exception);
        Assert.DoesNotContain("secret", exception.Message, StringComparison.Ordinal);
        Assert.Null(builder.Options.FindExtension<MySqlOptionsExtension>()!.DefaultCharSet);
        Assert.Null(builder.Options.FindExtension<MySqlOptionsExtension>()!.DefaultCollation);
    }

    /// <summary>Setter order does not validate an intermediate pair that the final values replace.</summary>
    /// <param name="charSetFirst">Whether the charset setter runs before the collation setter.</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Replacing_both_options_is_independent_of_setter_order(
        bool charSetFirst
    )
    {
        // Arrange
        var builder = CreateOptions("latin1", "latin1_swedish_ci");
        var provider = new MySqlDbContextOptionsBuilder(builder);

        // Act
        if (charSetFirst)
        {
            provider
                .DefaultCharSet("utf8mb4")
                .DefaultCollation("utf8mb4_unicode_ci");
        }
        else
        {
            provider
                .DefaultCollation("utf8mb4_unicode_ci")
                .DefaultCharSet("utf8mb4");
        }

        var actual = builder.Options.FindExtension<MySqlOptionsExtension>()!;
        var exception = Record.Exception(() => actual.Validate(builder.Options));

        // Assert
        Assert.Null(exception);
        Assert.Equal("utf8mb4", actual.DefaultCharSet);
        Assert.Equal("utf8mb4_unicode_ci", actual.DefaultCollation);
    }

    /// <summary>Known aliases and versioned shared UCA collations use one bounded compatibility rule.</summary>
    /// <param name="mariaDb">Whether to use a MariaDB rather than MySQL profile.</param>
    /// <param name="major">The configured server major.</param>
    /// <param name="minor">The configured server minor.</param>
    /// <param name="charSet">The requested charset.</param>
    /// <param name="collation">The requested collation.</param>
    /// <param name="expected">Whether the pair belongs to a compatible supported charset family.</param>
    [Theory]
    [InlineData(false, 8, 4, "utf8", "utf8mb3_general_ci", true)]
    [InlineData(false, 8, 4, "utf8mb3", "utf8_general_ci", true)]
    [InlineData(false, 8, 4, "UTF8MB4", "utf8mb4_unicode_ci", true)]
    [InlineData(false, 8, 4, "utf8mb4", "utf8_general_ci", false)]
    [InlineData(false, 8, 4, "utf8mb4", "uca1400_ai_ci", false)]
    [InlineData(true, 10, 11, "utf8mb4", "uca1400_ai_ci", true)]
    [InlineData(true, 10, 11, "utf16", "uca1400_ai_ci", true)]
    [InlineData(true, 12, 3, "ucs2", "uca1400_ai_ci", true)]
    [InlineData(true, 12, 3, "utf32", "uca1400_ai_ci", true)]
    [InlineData(true, 12, 3, "latin1", "uca1400_ai_ci", false)]
    [InlineData(true, 12, 3, "latin1", "utf8mb4_unicode_ci", false)]
    [InlineData(true, 12, 3, null, "utf8mb4_unicode_ci", true)]
    [InlineData(true, 12, 3, "latin1", null, true)]
    public void Charset_collation_compatibility_matches_qualified_alias_rules(
        bool mariaDb,
        int major,
        int minor,
        string? charSet,
        string? collation,
        bool expected
    )
    {
        // Arrange
        var version = mariaDb
            ? MySqlServerVersion.MariaDb(new Version(major, minor, 0))
            : MySqlServerVersion.MySql(new Version(major, minor, 0));

        // Act
        var actual = MySqlTextDefaults.IsCompatible(charSet, collation, version.Profile);

        // Assert
        Assert.Equal(expected, actual);
    }

    /// <summary>Every connection-path clone retains the model-affecting text options.</summary>
    /// <param name="path">The target connection path.</param>
    [Theory]
    [InlineData("connection-string")]
    [InlineData("connection")]
    [InlineData("data-source")]
    public void Connection_path_clones_preserve_text_defaults(
        string path
    )
    {
        // Arrange
        const string connectionString = ConnectionString + "GuidFormat=Binary16;";
        using var connection = new MySqlConnection(connectionString);
        using var dataSource = new MySqlDataSourceBuilder(connectionString).Build();
        var initial = CreateOptions("utf8mb4", "utf8mb4_unicode_ci")
            .Options
            .FindExtension<MySqlOptionsExtension>()!;

        // Act
        var actual = path switch
        {
            "connection-string" => initial.WithConnectionString(connectionString),
            "connection" => initial.WithConnection(connection),
            "data-source" => initial.WithDataSource(dataSource),
            _ => throw new ArgumentOutOfRangeException(nameof(path)),
        };

        // Assert
        Assert.Equal("utf8mb4", actual.DefaultCharSet);
        Assert.Equal("utf8mb4_unicode_ci", actual.DefaultCollation);
    }

    /// <summary>Every public generic/non-generic connection-path overload uses the same text-default contract.</summary>
    /// <param name="generic">Whether to use the typed context builder overload.</param>
    /// <param name="path">The selected public connection path.</param>
    [Theory]
    [InlineData(false, "connection-string")]
    [InlineData(false, "connection")]
    [InlineData(false, "data-source")]
    [InlineData(true, "connection-string")]
    [InlineData(true, "connection")]
    [InlineData(true, "data-source")]
    public void Public_builder_paths_share_the_text_default_contract(
        bool generic,
        string path
    )
    {
        // Arrange
        const string connectionString = ConnectionString + "GuidFormat=Binary16;";
        var version = MySqlServerVersion.MariaDb(new Version(12, 3, 0));
        using var connection = new MySqlConnection(connectionString);
        using var dataSource = new MySqlDataSourceBuilder(connectionString).Build();
        var typedBuilder = new DbContextOptionsBuilder<CachedTextContext>();
        var untypedBuilder = new DbContextOptionsBuilder();
        Action<MySqlDbContextOptionsBuilder> configure = options =>
            options
                .DefaultCharSet("utf8mb4")
                .DefaultCollation("utf8mb4_unicode_ci");

        // Act
        DbContextOptionsBuilder actual;
        if (generic)
        {
            actual = path switch
            {
                "connection-string" => typedBuilder.UseMySql(connectionString, version, configure),
                "connection" => typedBuilder.UseMySql(connection, version, configure),
                "data-source" => typedBuilder.UseMySql(dataSource, version, configure),
                _ => throw new ArgumentOutOfRangeException(nameof(path)),
            };
        }
        else
        {
            actual = path switch
            {
                "connection-string" => untypedBuilder.UseMySql(connectionString, version, configure),
                "connection" => untypedBuilder.UseMySql(connection, version, configure),
                "data-source" => untypedBuilder.UseMySql(dataSource, version, configure),
                _ => throw new ArgumentOutOfRangeException(nameof(path)),
            };
        }

        var extension = actual.Options.FindExtension<MySqlOptionsExtension>()!;

        // Assert
        Assert.Same(generic
                ? typedBuilder
                : untypedBuilder,
            actual);

        Assert.Equal("utf8mb4", extension.DefaultCharSet);
        Assert.Equal("utf8mb4_unicode_ci", extension.DefaultCollation);
        Assert.Equal(ConnectionState.Closed, connection.State);
    }
}
