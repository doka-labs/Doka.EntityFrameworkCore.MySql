namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>Verifies collection extraction under NO_BACKSLASH_ESCAPES.</summary>
public sealed partial class MySqlPrimitiveCollectionParameterIntegrationTests
{
    /// <summary>Verifies string parameters under MariaDB's no-backslash SQL mode.</summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public async Task MariaDb118_no_backslash_escapes_matches_string_parameters()
    {
        // Arrange
        await _fixture.EnsureSqlModeSeededAsync(CancellationToken.None);
        string[] values = ["\"quoted\"", "\"\\q\"", string.Empty, "gr\u00FCn"];

        // Act
        var ids = await ExecuteWithNoBackslashEscapesAsync(
            (context, cancellationToken) => context
                .Items
                .Where(item => EF
                    .Parameter(values)
                    .Contains(item.Value))
                .OrderBy(item => item.Id)
                .Select(item => item.Id)
                .ToArrayAsync(cancellationToken),
            CancellationToken.None);

        // Assert
        Assert.Equal([1, 2, 3, 4], ids);
    }

    /// <summary>Verifies nullable string parameters under MariaDB's no-backslash SQL mode.</summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public async Task MariaDb118_no_backslash_escapes_matches_nullable_string_parameters()
    {
        // Arrange
        await _fixture.EnsureSqlModeSeededAsync(CancellationToken.None);
        string?[] values = ["\"quoted\"", null];

        // Act
        var ids = await ExecuteWithNoBackslashEscapesAsync(
            (context, cancellationToken) => context
                .Items
                .Where(item => EF
                    .Parameter(values)
                    .Contains(item.OptionalValue))
                .OrderBy(item => item.Id)
                .Select(item => item.Id)
                .ToArrayAsync(cancellationToken),
            CancellationToken.None);

        // Assert
        Assert.Equal([1, 2], ids);
    }

    /// <summary>Verifies converted string parameters under MariaDB's no-backslash SQL mode.</summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public async Task MariaDb118_no_backslash_escapes_matches_converted_string_parameters()
    {
        // Arrange
        await _fixture.EnsureSqlModeSeededAsync(CancellationToken.None);
        string[] values = ["alpha"];

        // Act
        var ids = await ExecuteWithNoBackslashEscapesAsync(
            (context, cancellationToken) => context
                .Items
                .Where(item => EF
                    .Parameter(values)
                    .Contains(item.ConvertedValue))
                .Select(item => item.Id)
                .ToArrayAsync(cancellationToken),
            CancellationToken.None);

        // Assert
        Assert.Equal([1], ids);
    }

    /// <summary>Verifies converted text parameters under MariaDB's no-backslash SQL mode.</summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public async Task MariaDb118_no_backslash_escapes_matches_converted_text_parameters()
    {
        // Arrange
        await _fixture.EnsureSqlModeSeededAsync(CancellationToken.None);
        TextValue[] values = [new("\"\\q\""), new("gr\u00FCn")];

        // Act
        var ids = await ExecuteWithNoBackslashEscapesAsync(
            (context, cancellationToken) => context
                .Items
                .Where(item => EF
                    .Parameter(values)
                    .Contains(item.ConvertedText))
                .OrderBy(item => item.Id)
                .Select(item => item.Id)
                .ToArrayAsync(cancellationToken),
            CancellationToken.None);

        // Assert
        Assert.Equal([2, 4], ids);
    }

    /// <summary>Verifies model string collections under MariaDB's no-backslash SQL mode.</summary>
    [RequiresDatabaseTargetFact(IntegrationDatabaseTarget.MariaDb118)]
    public async Task MariaDb118_no_backslash_escapes_preserves_model_string_collections()
    {
        // Arrange
        await _fixture.EnsureSqlModeSeededAsync(CancellationToken.None);
        string?[] expected = ["\"quoted\"", "\"\\q\"", string.Empty, "gr\u00FCn", null];

        // Act
        var values = await ExecuteWithNoBackslashEscapesAsync(
            (context, cancellationToken) => context
                .Items
                .Where(item => item.Id == 1)
                .SelectMany(item => item.Values)
                .ToArrayAsync(cancellationToken),
            CancellationToken.None);

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
        await _fixture.EnsureSqlModeSeededAsync(CancellationToken.None);
        string[] values = ["\"\\q\"", "missing"];

        // Act
        var (sql, ids) = await ExecuteWithNoBackslashEscapesAsync(
            async (context, cancellationToken) =>
            {
                var query = context.Items.Where(item => values.Contains(item.Value));

                return (
                    query.ToQueryString(),
                    await query
                        .Select(item => item.Id)
                        .ToArrayAsync(cancellationToken));
            },
            CancellationToken.None,
            configureConnector: true);

        // Assert
        Assert.Contains(" IN (", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("JSON_TABLE", sql, StringComparison.Ordinal);
        Assert.Equal([2], ids);
    }

    /// <summary>Executes the callback with the requested SQL mode and restores pooled session state.</summary>
    /// <param name="execute">The database action that observes the supplied cancellation token.</param>
    /// <param name="cancellationToken">The cancellation token to observe during database work.</param>
    /// <param name="configureConnector">Whether connector escaping must match the temporary SQL mode.</param>
    /// <returns>The callback result after session state has been restored.</returns>
    private static async Task<TResult> ExecuteWithNoBackslashEscapesAsync<TResult>(
        Func<SqlModeStringCollectionContext, CancellationToken, Task<TResult>> execute,
        CancellationToken cancellationToken,
        bool configureConnector = false
    )
    {
        await using var context = CreateSqlModeContext(configureConnector);
        await context.Database.OpenConnectionAsync(cancellationToken);

        var connection = context.Database.GetDbConnection();
        var previousSqlMode = await ReadSqlModeAsync(connection, cancellationToken);

        try
        {
            await SetSqlModeAsync(connection, "NO_BACKSLASH_ESCAPES", cancellationToken);

            return await execute(context, cancellationToken);
        }
        finally
        {
            // WHY: sql_mode is session state; always restore it before this
            // connection can return to MySqlConnector's pool.
            await SetSqlModeAsync(connection, previousSqlMode, CancellationToken.None);
        }
    }

    /// <summary>Reads the sql mode from the current database.</summary>
    /// <param name="cancellationToken">The cancellation token to observe during database work.</param>
    private static async Task<string> ReadSqlModeAsync(
        DbConnection connection,
        CancellationToken cancellationToken
    )
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT @@SESSION.sql_mode;";

        return Assert.IsType<string>(
            await command.ExecuteScalarAsync(cancellationToken));
    }

    /// <summary>Sets the sql mode for the current connection.</summary>
    /// <param name="cancellationToken">The cancellation token to observe during database work.</param>
    private static async Task SetSqlModeAsync(
        DbConnection connection,
        string sqlMode,
        CancellationToken cancellationToken
    )
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SET SESSION sql_mode = @sqlMode;";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "@sqlMode";
        parameter.Value = sqlMode;
        command.Parameters.Add(parameter);

        _ = await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>Reads the string decode coercibility from the current database.</summary>
    /// <param name="cancellationToken">The cancellation token to observe during database work.</param>
    private static async Task<int> ReadStringDecodeCoercibilityAsync(
        PrimitiveCollectionContext context,
        CancellationToken cancellationToken
    )
    {
        await context.Database.OpenConnectionAsync(cancellationToken);

        try
        {
            await using var command = context
                .Database
                .GetDbConnection()
                .CreateCommand();

            command.CommandText = """
                                  SELECT COERCIBILITY(
                                      JSON_UNQUOTE(JSON_QUOTE(CONVERT(FROM_BASE64('YQ==') USING utf8mb4))));
                                  """;

            return Convert.ToInt32(
                await command.ExecuteScalarAsync(cancellationToken),
                CultureInfo.InvariantCulture);
        }
        finally
        {
            // WHY: Returning the connection to the pool must complete even after the query is cancelled.
            await context.Database.CloseConnectionAsync();
        }
    }
}
