namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlInsertSelectIntegrationTests
{
    private static readonly string[] s_explicitNullRows = ["10|<NULL>|20|1", "20|<NULL>|40|1"];
    private static readonly string[] s_defaultRows = ["10|server-default|20|1", "20|server-default|40|1"];
    private static readonly string[] s_existingPrimaryRows = ["99|existing"];

    /// <summary>Reports independent native plain-insert violations, including a late-row constraint failure.</summary>
    public static IEnumerable<TheoryDataRow<string, string>> ConstraintCases => SelectedTargets.SelectMany(target
        => new[]
        {
            new TheoryDataRow<string, string>(target.Data, "primary"),
            new TheoryDataRow<string, string>(target.Data, "unique-bag"),
            new TheoryDataRow<string, string>(target.Data, "foreign-key"),
            new TheoryDataRow<string, string>(target.Data, "check"),
            new TheoryDataRow<string, string>(target.Data, "trigger"),
        });

    /// <summary>Separates omission, which invokes the default, from an explicit SQL NULL.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(EmptyCases))]
    public async Task Omitted_default_and_explicit_null_have_distinct_persisted_semantics(
        string target,
        bool explicitNull
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await InsertStore.CreateAsync(target, safety.Token);
        var commands = new InsertCommands();
        await using var context = store.CreateContext(commands);

        // Act
        var count = await context
            .Set<InsertSource>()
            .Where(row => row.Id <= 2)
            .ExecuteInsertFromQueryAsync(context.Set<InsertTarget>(),
                setters =>
                {
                    setters
                        .SetProperty(row => row.Code, row => row.Label)
                        .SetProperty(row => row.Value, row => row.Value);

                    if (explicitNull)
                    {
                        setters.SetProperty(row => row.Optional, row => null);
                    }
                },
                safety.Token);

        var actual = await ReadNativeAsync(store.Observer,
            $"SELECT `Value`,`Optional`,`Computed`,`Id`>0 FROM `{TargetTable}` ORDER BY `Value`;",
            safety.Token);

        // Assert
        Assert.Equal(2, count);
        Assert.Equal(explicitNull
                ? s_explicitNullRows
                : s_defaultRows,
            actual);

        var sql = Assert.Single(commands.Statements);
        Assert.DoesNotContain("`Computed`", sql, StringComparison.Ordinal);
        Assert.Equal(0, commands.Readers);
    }

    /// <summary>InnoDB constraints preserve the original native error and do not become IGNORE, UPSERT or partial success.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(ConstraintCases))]
    public async Task Native_constraint_failure_is_plain_insert_and_statement_atomic_on_innodb(
        string target,
        string constraint
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await InsertStore.CreateAsync(target, safety.Token);
        var setup = constraint switch
        {
            "primary" => $"INSERT INTO `{TargetTable}` (`Id`,`Value`,`order`) VALUES (1,99,'existing');",
            "unique-bag" => $"ALTER TABLE `{TargetTable}` ADD UNIQUE INDEX `IX_code` (`order`);",
            "foreign-key" => "CREATE TABLE AllowedValues (`Value` INT PRIMARY KEY) ENGINE=InnoDB; "
                + "INSERT INTO AllowedValues VALUES (10),(20); "
                + $"ALTER TABLE `{TargetTable}` ADD CONSTRAINT `FK_allowed` FOREIGN KEY (`Value`) REFERENCES AllowedValues (`Value`);",
            "check" => $"ALTER TABLE `{TargetTable}` ADD CONSTRAINT `CK_range` CHECK (`Value` < 30);",
            "trigger" => $"CREATE TRIGGER InsertFailure BEFORE INSERT ON `{TargetTable}` FOR EACH ROW "
                + "BEGIN IF NEW.`Value` = 30 THEN SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'late native insert rejection'; END IF; END;",
            _ => throw new ArgumentOutOfRangeException(nameof(constraint)),
        };

        await ExecuteNativeAsync(store.Observer, setup, safety.Token);
        var commands = new InsertCommands();
        await using var context = store.CreateContext(commands);

        // Act
        var error = await Assert.ThrowsAsync<MySqlException>(() => context
            .Set<InsertSource>()
            .OrderBy(row => row.Id)
            .ExecuteInsertFromQueryAsync(context.Set<InsertTarget>(),
                setters =>
                {
                    setters
                        .SetProperty(row => row.Value, row => row.Value)
                        .SetProperty(row => row.Code,
                            row => constraint == "unique-bag"
                                ? "duplicate"
                                : row.Label);

                    if (constraint == "primary")
                    {
                        setters.SetProperty(row => row.Id, row => row.Id);
                    }
                },
                safety.Token));

        var persisted = await ReadNativeAsync(store.Observer,
            $"SELECT `Value`,`order` FROM `{TargetTable}` ORDER BY `Value`;",
            safety.Token);

        var healthy = await context
            .Set<InsertSource>()
            .CountAsync(safety.Token);

        // Assert
        Assert.Equal(constraint switch
            {
                "primary" or "unique-bag" => 1062,
                "foreign-key" => 1452,
                "check" => store.Version.IsMariaDb
                    ? 4025
                    : 3819,
                "trigger" => 1644,
                _ => throw new ArgumentOutOfRangeException(nameof(constraint)),
            },
            error.Number);

        Assert.Equal(constraint == "primary"
                ? s_existingPrimaryRows
                : [],
            persisted);

        Assert.Equal(3, healthy);
        var sql = Assert.Single(commands.Statements);
        Assert.DoesNotContain("IGNORE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DUPLICATE KEY", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("REPLACE", sql, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Native trigger side effects do not inflate the affected-row count or introduce provider readback.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Trigger_side_effects_preserve_native_target_count_and_values(
        string target
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await InsertStore.CreateAsync(target, safety.Token);
        await ExecuteNativeAsync(store.Observer,
            "CREATE TABLE InsertAudit (`Value` INT NOT NULL) ENGINE=InnoDB; "
            + $"CREATE TRIGGER InsertAuditTrigger AFTER INSERT ON `{TargetTable}` FOR EACH ROW "
            + "BEGIN INSERT INTO InsertAudit VALUES (NEW.`Value`),(NEW.`Value` + 1); END;",
            safety.Token);

        var commands = new InsertCommands();
        await using var context = store.CreateContext(commands);

        // Act
        var count = await context
            .Set<InsertSource>()
            .ExecuteInsertFromQueryAsync(context.Set<InsertTarget>(),
                setters => setters
                    .SetProperty(row => row.Value, row => row.Value)
                    .SetProperty(row => row.Code, row => row.Label),
                safety.Token);

        var audit = await ReadNativeAsync(store.Observer,
            "SELECT `Value` FROM InsertAudit ORDER BY `Value`;",
            safety.Token);

        var inserted = await ReadNativeAsync(store.Observer,
            $"SELECT `Value` FROM `{TargetTable}` ORDER BY `Value`;",
            safety.Token);

        // Assert
        Assert.Equal(3, count);
        Assert.Equal(["10", "20", "30"], inserted);
        Assert.Equal(["10", "11", "20", "21", "30", "31"], audit);
        Assert.Single(commands.Statements);
        Assert.Equal(0, commands.Readers);
    }

    /// <summary>Strict text overflow is a native error with a healthy subsequent command on the same context.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Strict_text_overflow_keeps_native_error_and_clean_connection(
        string target
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await InsertStore.CreateAsync(target, safety.Token);
        var commands = new InsertCommands();
        await using var context = store.CreateContext(commands);
        await context.Database.OpenConnectionAsync(safety.Token);
        await context.Database.ExecuteSqlRawAsync("SET SESSION sql_mode = 'STRICT_ALL_TABLES';", safety.Token);
        commands.Statements.Clear();
        commands.Parameters.Clear();
        commands.Tokens.Clear();
        commands.Connections.Clear();
        var tooLong = new string('x', 101);

        // Act
        var error = await Assert.ThrowsAsync<MySqlException>(() => context
            .Set<InsertSource>()
            .ExecuteInsertFromQueryAsync(context.Set<InsertTarget>(),
                setters => setters
                    .SetProperty(row => row.Value, row => row.Value)
                    .SetProperty(row => row.Code, row => tooLong),
                safety.Token));

        var rows = await ReadNativeAsync(store.Observer, $"SELECT COUNT(*) FROM `{TargetTable}`;", safety.Token);
        var healthy = await context
            .Set<InsertSource>()
            .CountAsync(safety.Token);

        // Assert
        Assert.Equal(1406, error.Number);
        Assert.Equal(["0"], rows);
        Assert.Equal(3, healthy);
        Assert.Single(commands.Statements);
    }

    /// <summary>Permissive native truncation is observable and is never reported as lossless fidelity.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Permissive_mode_retains_native_warning_semantics_without_changing_the_session(
        string target
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await InsertStore.CreateAsync(target, safety.Token);
        await using var context = store.CreateContext();
        await context.Database.OpenConnectionAsync(safety.Token);
        await context.Database.ExecuteSqlRawAsync("SET SESSION sql_mode = '';", safety.Token);
        var tooLong = new string('x', 101);

        // Act
        var count = await context
            .Set<InsertSource>()
            .Where(row => row.Id == 1)
            .ExecuteInsertFromQueryAsync(context.Set<InsertTarget>(),
                setters => setters
                    .SetProperty(row => row.Value, row => row.Value)
                    .SetProperty(row => row.Code, row => tooLong),
                safety.Token);

        var connection = (MySqlConnection)context.Database.GetDbConnection();
        var warnings = await ReadNativeAsync(connection, "SHOW WARNINGS;", safety.Token);
        var actual = await ReadNativeAsync(store.Observer,
            $"SELECT CHAR_LENGTH(`order`) FROM `{TargetTable}`;",
            safety.Token);

        var mode = await ReadNativeAsync(connection, "SELECT @@session.sql_mode;", safety.Token);

        // Assert
        Assert.Equal(1, count);
        Assert.Equal(["100"], actual);
        Assert.Contains(warnings, warning => warning.Contains("|1265|", StringComparison.Ordinal));
        Assert.Equal([string.Empty], mode);
    }
}
