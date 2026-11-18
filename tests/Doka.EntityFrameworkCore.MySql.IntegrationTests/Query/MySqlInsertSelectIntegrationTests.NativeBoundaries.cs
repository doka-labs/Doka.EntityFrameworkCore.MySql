namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlInsertSelectIntegrationTests
{
    /// <summary>Separates native temporal range handling under strict and permissive SQL modes on every supported profile.</summary>
    public static IEnumerable<TheoryDataRow<string, bool>> TemporalRangeCases => SelectedTargets.SelectMany(target
        => new[]
        {
            new TheoryDataRow<string, bool>(target.Data, false), new TheoryDataRow<string, bool>(target.Data, true),
        });

    /// <summary>Reports native required-NULL and decimal range failures separately under an explicit strict session.</summary>
    public static IEnumerable<TheoryDataRow<string, string>> RangeCases => SelectedTargets.SelectMany(target => new[]
    {
        new TheoryDataRow<string, string>(target.Data, "required-null"),
        new TheoryDataRow<string, string>(target.Data, "decimal-range"),
    });

    /// <summary>Server-side date overflow preserves native errors or warning/coercion semantics and the caller's prior work.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(TemporalRangeCases))]
    public async Task Temporal_range_overflow_matches_native_sql_mode_and_preserves_caller_work(
        string target,
        bool strict
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await InsertStore.CreateAsync(target, safety.Token);
        await using var setup = store.CreateContext();
        setup
            .Set<FacetSource>()
            .AddRange(
                new FacetSource
                {
                    Id = 1,
                    Status = InsertStatus.Approved,
                    Code = new InsertCode(7),
                    Amount = 1,
                    Moment = new DateTime(2026, 11, 17),
                    Text = "ordinary",
                },
                new FacetSource
                {
                    Id = 2,
                    Status = InsertStatus.Rejected,
                    Code = new InsertCode(8),
                    Amount = 2,
                    Moment = new DateTime(9999, 12, 30),
                    Text = "native range boundary",
                });

        await setup.SaveChangesAsync(safety.Token);
        await ExecuteNativeAsync(store.Observer,
            "CREATE TABLE InsertTemporalControl LIKE InsertTemporalRange;",
            safety.Token);

        var commands = new InsertCommands();
        await using var context = store.CreateContext(commands);
        await context.Database.OpenConnectionAsync(safety.Token);
        await context.Database.ExecuteSqlRawAsync(strict
                ? "SET SESSION sql_mode='STRICT_ALL_TABLES';"
                : "SET SESSION sql_mode='';",
            safety.Token);

        commands.Statements.Clear();
        commands.Parameters.Clear();
        commands.Connections.Clear();
        commands.Tokens.Clear();
        await using var transaction = await context.Database.BeginTransactionAsync(safety.Token);
        var worker = (MySqlConnection)context.Database.GetDbConnection();
        var nativeTransaction = (MySqlTransaction)transaction.GetDbTransaction();
        await ExecuteNativeAsync(worker,
            "INSERT INTO InsertTemporalControl VALUES (99,'2024-01-01');"
            + "INSERT INTO InsertTemporalRange VALUES (99,'2024-01-01');",
            safety.Token,
            nativeTransaction);

        var nativeAffected = 0;
        var affected = 0;

        // Act
        var nativeError = await Record.ExceptionAsync(async () => nativeAffected = await ExecuteNativeAsync(worker,
            "INSERT INTO InsertTemporalControl (`Id`,`Moment`) SELECT `Id`,DATE_ADD(`Moment`,INTERVAL 1 YEAR) "
            + "FROM InsertFacetSource ORDER BY `Id`;",
            safety.Token,
            nativeTransaction));

        var nativeWarnings = await ReadNativeAsync(worker, "SHOW WARNINGS;", safety.Token, nativeTransaction);
        var error = await Record.ExceptionAsync(async () => affected = await context
            .Set<FacetSource>()
            .OrderBy(row => row.Id)
            .ExecuteInsertFromQueryAsync(context.Set<TemporalRangeTarget>(),
                setters => setters
                    .SetProperty(row => row.Id, row => row.Id)
                    .SetProperty(row => row.Moment, row => row.Moment.AddYears(1)),
                safety.Token));

        var warnings = await ReadNativeAsync(worker, "SHOW WARNINGS;", safety.Token, nativeTransaction);

        // WHY: Read the real stored representation as text; permissive zero dates must not be hidden by reader conversion options.
        var native = await ReadNativeAsync(worker,
            "SELECT `Id`,CAST(`Moment` AS CHAR) FROM InsertTemporalControl ORDER BY `Id`;",
            safety.Token,
            nativeTransaction);

        var actual = await ReadNativeAsync(worker,
            "SELECT `Id`,CAST(`Moment` AS CHAR) FROM InsertTemporalRange ORDER BY `Id`;",
            safety.Token,
            nativeTransaction);

        await transaction.RollbackAsync(safety.Token);
        var remaining = await ReadNativeAsync(store.Observer,
            "SELECT (SELECT COUNT(*) FROM InsertTemporalControl),(SELECT COUNT(*) FROM InsertTemporalRange);",
            safety.Token);

        // Assert
        if (strict)
        {
            Assert.IsType<MySqlException>(nativeError);
        }

        if (nativeError is null)
        {
            Assert.Null(error);
            Assert.Equal(2, nativeAffected);
            Assert.Equal(nativeAffected, affected);
            Assert.Equal(3, actual.Length);
            Assert.Contains(actual, row => row.StartsWith("1|2027-11-17", StringComparison.Ordinal));
        }
        else
        {
            Assert.Equal(Assert.IsType<MySqlException>(nativeError)
                    .Number,
                Assert.IsType<MySqlException>(error)
                    .Number);

            Assert.StartsWith("99|2024-01-01", Assert.Single(actual), StringComparison.Ordinal);
        }

        Assert.NotEmpty(nativeWarnings);
        Assert.Equal(nativeWarnings.Select(warning => string.Join('|',
                warning
                    .Split('|')
                    .Take(2))),
            warnings.Select(warning => string.Join('|',
                warning
                    .Split('|')
                    .Take(2))));

        Assert.Equal(native, actual);
        Assert.Contains(actual, row => row.StartsWith("99|2024-01-01", StringComparison.Ordinal));
        Assert.Equal(["0|0"], remaining);
        Assert.Contains("DATE_ADD", Assert.Single(commands.Statements), StringComparison.Ordinal);
        Assert.Equal(0, commands.Readers);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Temporary-table self-insertion follows the native same-session boundary, without inventing schema metadata.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Temporary_same_table_source_matches_the_native_engine_boundary(
        string target
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await InsertStore.CreateAsync(target, safety.Token);
        await using var connection = await store.OpenNativeAsync(safety.Token);
        await ExecuteNativeAsync(connection,

            // WHY: Same-name CREATE TEMPORARY ... LIKE collides with the source alias before any self-insert is tested.
            // A distinct permanent structure template keeps both temporary tables physically identical.
            $"CREATE TABLE InsertTempTemplate LIKE `{TargetTable}`; "
            + "CREATE TEMPORARY TABLE InsertTempControl LIKE InsertTempTemplate; "
            + $"CREATE TEMPORARY TABLE `{TargetTable}` LIKE InsertTempTemplate; "
            + "INSERT INTO InsertTempControl (`Value`,`order`,`TenantId`) VALUES (11,'first',1),(22,'second',1); "
            + $"INSERT INTO `{TargetTable}` (`Value`,`order`,`TenantId`) VALUES (11,'first',1),(22,'second',1);",
            safety.Token);

        var commands = new InsertCommands();
        await using var context = store.CreateContext(commands, connection);
        var nativeCount = 0;
        var providerCount = 0;

        // Act
        var nativeError = await Record.ExceptionAsync(async () => nativeCount = await ExecuteNativeAsync(connection,
            "INSERT INTO InsertTempControl (`Value`,`order`,`TenantId`) SELECT `Value`+100,`order`,`TenantId` "
            + "FROM InsertTempControl WHERE `Value`<30;",
            safety.Token));

        var providerError = await Record.ExceptionAsync(async () => providerCount = await context
            .Set<InsertTarget>()
            .Where(row => row.Value < 30)
            .ExecuteInsertFromQueryAsync(context.Set<InsertTarget>(),
                setters => setters
                    .SetProperty(row => row.Value, row => row.Value + 100)
                    .SetProperty(row => row.Code, row => row.Code),
                safety.Token));

        var native = await ReadNativeAsync(connection,
            "SELECT `Value`,`order` FROM InsertTempControl ORDER BY `Value`;",
            safety.Token);

        var actual = await ReadNativeAsync(connection,
            $"SELECT `Value`,`order` FROM `{TargetTable}` ORDER BY `Value`;",
            safety.Token);

        // Assert
        if (nativeError is null)
        {
            Assert.Null(providerError);
            Assert.Equal(nativeCount, providerCount);
        }
        else
        {
            Assert.Equal(Assert.IsType<MySqlException>(nativeError)
                    .Number,
                Assert.IsType<MySqlException>(providerError)
                    .Number);
        }

        Assert.Equal(native, actual);
        Assert.Single(commands.Statements);
        Assert.Equal(0, commands.Readers);
        Assert.Same(connection, Assert.Single(commands.Connections));
    }

    /// <summary>Native nullability and decimal overflow remain visible with no persisted target rows.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(RangeCases))]
    public async Task Strict_nullability_and_decimal_range_failures_preserve_native_error(
        string target,
        string invalid
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await InsertStore.CreateAsync(target, safety.Token);
        await using var setup = store.CreateContext();
        setup.Add(new FacetSource
        {
            Id = 1,
            Status = InsertStatus.Approved,
            Code = new InsertCode(7),
            Amount = 1234.56m,
            Moment = new DateTime(2026, 11, 17),
            Text = "range",
        });

        await setup.SaveChangesAsync(safety.Token);
        var commands = new InsertCommands();
        await using var context = store.CreateContext(commands);
        await context.Database.OpenConnectionAsync(safety.Token);
        await context.Database.ExecuteSqlRawAsync("SET SESSION sql_mode = 'STRICT_ALL_TABLES';", safety.Token);
        commands.Statements.Clear();
        commands.Parameters.Clear();
        commands.Connections.Clear();
        commands.Tokens.Clear();

        // Act
        var error = await Record.ExceptionAsync(async () =>
        {
            if (invalid == "required-null")
            {
                _ = await context
                    .Set<InsertSource>()
                    .Where(row => row.Id == 1)
                    .ExecuteInsertFromQueryAsync(context.Set<InsertTarget>(),
                        setters => setters
                            .SetProperty(row => row.Value, row => row.Value)
                            .SetProperty(row => row.Code, row => null!),
                        safety.Token);
            }
            else
            {
                _ = await context
                    .Set<FacetSource>()
                    .ExecuteInsertFromQueryAsync(context.Set<NarrowTarget>(),
                        setters => setters
                            .SetProperty(row => row.Amount, row => row.Amount),
                        safety.Token);
            }
        });

        var actual = await ReadNativeAsync(store.Observer,
            invalid == "required-null"
                ? $"SELECT COUNT(*) FROM `{TargetTable}`;"
                : "SELECT COUNT(*) FROM InsertNarrow;",
            safety.Token);

        // Assert
        Assert.Equal(invalid == "required-null"
                ? 1048
                : 1264,
            Assert.IsType<MySqlException>(error)
                .Number);

        Assert.Equal(["0"], actual);
        Assert.Single(commands.Statements);
        Assert.Equal(0, commands.Readers);
    }
}
