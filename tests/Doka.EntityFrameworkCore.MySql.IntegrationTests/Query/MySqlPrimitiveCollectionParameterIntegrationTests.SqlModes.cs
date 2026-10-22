namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>Verifies collection extraction under NO_BACKSLASH_ESCAPES.</summary>
public sealed partial class MySqlPrimitiveCollectionParameterIntegrationTests
{
    /// <summary>Verifies string parameters under MariaDB's no-backslash SQL mode.</summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public async Task MariaDb118_no_backslash_escapes_matches_string_parameters()
    {
        // Arrange
        await _fixture.EnsureSqlModeSeededAsync();
        string[] values = ["\"quoted\"", "\"\\q\"", string.Empty, "gr\u00FCn"];

        // Act
        var ids = await ExecuteWithNoBackslashEscapesAsync(context => context.Items
            .Where(item => EF.Parameter(values).Contains(item.Value))
            .OrderBy(item => item.Id)
            .Select(item => item.Id)
            .ToArrayAsync(CancellationToken.None));

        // Assert
        Assert.Equal([1, 2, 3, 4], ids);
    }

    /// <summary>Verifies nullable string parameters under MariaDB's no-backslash SQL mode.</summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public async Task MariaDb118_no_backslash_escapes_matches_nullable_string_parameters()
    {
        // Arrange
        await _fixture.EnsureSqlModeSeededAsync();
        string?[] values = ["\"quoted\"", null];

        // Act
        var ids = await ExecuteWithNoBackslashEscapesAsync(context => context.Items
            .Where(item => EF.Parameter(values).Contains(item.OptionalValue))
            .OrderBy(item => item.Id)
            .Select(item => item.Id)
            .ToArrayAsync(CancellationToken.None));

        // Assert
        Assert.Equal([1, 2], ids);
    }

    /// <summary>Verifies converted string parameters under MariaDB's no-backslash SQL mode.</summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public async Task MariaDb118_no_backslash_escapes_matches_converted_string_parameters()
    {
        // Arrange
        await _fixture.EnsureSqlModeSeededAsync();
        string[] values = ["alpha"];

        // Act
        var ids = await ExecuteWithNoBackslashEscapesAsync(context => context.Items
            .Where(item => EF.Parameter(values).Contains(item.ConvertedValue))
            .Select(item => item.Id)
            .ToArrayAsync(CancellationToken.None));

        // Assert
        Assert.Equal([1], ids);
    }

    /// <summary>Verifies converted text parameters under MariaDB's no-backslash SQL mode.</summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public async Task MariaDb118_no_backslash_escapes_matches_converted_text_parameters()
    {
        // Arrange
        await _fixture.EnsureSqlModeSeededAsync();
        TextValue[] values = [new("\"\\q\""), new("gr\u00FCn")];

        // Act
        var ids = await ExecuteWithNoBackslashEscapesAsync(context => context.Items
            .Where(item => EF.Parameter(values).Contains(item.ConvertedText))
            .OrderBy(item => item.Id)
            .Select(item => item.Id)
            .ToArrayAsync(CancellationToken.None));

        // Assert
        Assert.Equal([2, 4], ids);
    }

    /// <summary>Verifies model string collections under MariaDB's no-backslash SQL mode.</summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public async Task MariaDb118_no_backslash_escapes_preserves_model_string_collections()
    {
        // Arrange
        await _fixture.EnsureSqlModeSeededAsync();
        string?[] expected = ["\"quoted\"", "\"\\q\"", string.Empty, "gr\u00FCn", null];

        // Act
        var values = await ExecuteWithNoBackslashEscapesAsync(context => context.Items
            .Where(item => item.Id == 1)
            .SelectMany(item => item.Values)
            .ToArrayAsync(CancellationToken.None));

        // Assert
        Assert.Equal(expected, values);
    }

    /// <summary>
    /// Verifies ordinary scalar string parameters when the connection's
    /// escaping contract matches the server's NO_BACKSLASH_ESCAPES mode.
    /// </summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public async Task MariaDb118_default_scalar_strings_match_configured_sql_mode()
    {
        // Arrange
        await _fixture.EnsureSqlModeSeededAsync();
        string[] values = ["\"\\q\"", "missing"];

        // Act
        var (sql, ids) = await ExecuteWithNoBackslashEscapesAsync(async context =>
        {
            var query = context.Items.Where(item => values.Contains(item.Value));

            return (
                query.ToQueryString(),
                await query.Select(item => item.Id).ToArrayAsync(CancellationToken.None));
        }, configureConnector: true);

        // Assert
        Assert.Contains(" IN (", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("JSON_TABLE", sql, StringComparison.Ordinal);
        Assert.Equal([2], ids);
    }

    private static async Task<TResult> ExecuteWithNoBackslashEscapesAsync<TResult>(
        Func<SqlModeStringCollectionContext, Task<TResult>> execute,
        bool configureConnector = false
    )
    {
        await using var context = CreateSqlModeContext(configureConnector);
        await context.Database.OpenConnectionAsync(CancellationToken.None);

        var connection = context.Database.GetDbConnection();
        var previousSqlMode = await ReadSqlModeAsync(connection);

        try
        {
            await SetSqlModeAsync(connection, "NO_BACKSLASH_ESCAPES");

            return await execute(context);
        }
        finally
        {
            // WHY: sql_mode is session state; always restore it before this
            // connection can return to MySqlConnector's pool.
            await SetSqlModeAsync(connection, previousSqlMode);
        }
    }

    private static async Task<string> ReadSqlModeAsync(
        DbConnection connection
    )
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT @@SESSION.sql_mode;";

        return Assert.IsType<string>(
            await command.ExecuteScalarAsync(CancellationToken.None));
    }

    private static async Task SetSqlModeAsync(
        DbConnection connection,
        string sqlMode
    )
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SET SESSION sql_mode = @sqlMode;";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "@sqlMode";
        parameter.Value = sqlMode;
        command.Parameters.Add(parameter);

        _ = await command.ExecuteNonQueryAsync(CancellationToken.None);
    }

    private static async Task<int> ReadStringDecodeCoercibilityAsync(
        PrimitiveCollectionContext context
    )
    {
        await context.Database.OpenConnectionAsync(CancellationToken.None);

        try
        {
            await using var command = context.Database.GetDbConnection().CreateCommand();
            command.CommandText = """
                SELECT COERCIBILITY(
                    JSON_UNQUOTE(JSON_QUOTE(CONVERT(FROM_BASE64('YQ==') USING utf8mb4))));
                """;

            return Convert.ToInt32(
                await command.ExecuteScalarAsync(CancellationToken.None),
                CultureInfo.InvariantCulture);
        }
        finally
        {
            await context.Database.CloseConnectionAsync();
        }
    }
}
