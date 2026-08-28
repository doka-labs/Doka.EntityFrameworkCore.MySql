using System.Text;

namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

/// <summary>
/// Coverage tests for migration DDL operations: Rename Table/Column, AlterSequence,
/// Spatial Index, INVISIBLE, identifier normalization, and argument validation.
/// </summary>
public sealed class MySqlMigrationDdlCoverageTests
{
    private static readonly int[] s_mixedPrefixLengths = [32, 0];
    private static readonly int[] s_singlePrefixLength = [16];
    private static readonly int[] s_invalidPrefixLengths = [-1, 0];
    private static readonly int[] s_fullTextPrefixLengths = [16, 0];

    // -- RENAME TABLE --

    [Fact]
    public void RenameTable_generates_rename_table_sql()
    {
        using var context = CreateMySqlContext();
        var generator = context.GetService<IMigrationsSqlGenerator>();
        var operation = new RenameTableOperation
        {
            Name = "OldTable",
            NewName = "NewTable",
        };

        var commands = generator.Generate([operation], context.Model);
        var sql = JoinSql(commands);

        Assert.Contains("RENAME TABLE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("`OldTable`", sql, StringComparison.Ordinal);
        Assert.Contains("`NewTable`", sql, StringComparison.Ordinal);
    }

    // -- RENAME COLUMN --

    [Fact]
    public void RenameColumn_generates_alter_table_rename_column_sql()
    {
        using var context = CreateMySqlContext();
        var generator = context.GetService<IMigrationsSqlGenerator>();
        var operation = new RenameColumnOperation
        {
            Table = "Products",
            Name = "OldCol",
            NewName = "NewCol",
        };

        var commands = generator.Generate([operation], context.Model);
        var sql = JoinSql(commands);

        Assert.Contains("ALTER TABLE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("RENAME COLUMN", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("`OldCol`", sql, StringComparison.Ordinal);
        Assert.Contains("`NewCol`", sql, StringComparison.Ordinal);
    }

    // -- ROW VERSION --

    [Fact]
    public void Temporal_row_version_generates_current_timestamp_clauses()
    {
        using var context = CreateMySqlContext();
        var generator = context.GetService<IMigrationsSqlGenerator>();
        var operation = new AddColumnOperation
        {
            Table = "AuditEntries",
            Name = "Version",
            ClrType = typeof(byte[]),
            ColumnType = "timestamp(6)",
            IsRowVersion = true,
        };

        var sql = JoinSql(generator.Generate([operation], context.Model));

        Assert.Contains("DEFAULT CURRENT_TIMESTAMP(6)", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ON UPDATE CURRENT_TIMESTAMP(6)", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Non_temporal_row_version_does_not_generate_current_timestamp_clauses()
    {
        using var context = CreateMySqlContext();
        var generator = context.GetService<IMigrationsSqlGenerator>();
        var operation = new AddColumnOperation
        {
            Table = "AuditEntries",
            Name = "Version",
            ClrType = typeof(string),
            ColumnType = "varchar(64)",
            IsRowVersion = true,
            DefaultValue = "initial",
        };

        var sql = JoinSql(generator.Generate([operation], context.Model));

        Assert.DoesNotContain("CURRENT_TIMESTAMP", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("`Version` varchar(64)", sql, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies that an application-selected value repairs existing null rows
    /// before the provider makes the column required.
    /// </summary>
    [Theory]
    [InlineData(typeof(string), "varchar(64)", "ready", "'ready'")]
    [InlineData(typeof(int), "int", 1, "1")]
    [InlineData(typeof(string), "json", "{}", "'{}'")]
    [InlineData(typeof(string), "enum('alpha','beta')", "alpha", "'alpha'")]
    public void Nullable_column_becoming_required_uses_explicit_backfill(
        Type clrType,
        string columnType,
        object defaultValue,
        string expectedLiteral
    )
    {
        using var context = CreateMySqlContext();
        var generator = context.GetService<IMigrationsSqlGenerator>();
        var operation = new AlterColumnOperation
        {
            Table = "Entries",
            Name = "Value",
            ClrType = clrType,
            ColumnType = columnType,
            IsNullable = false,
            DefaultValue = defaultValue,
            OldColumn =
            {
                ClrType = clrType,
                ColumnType = columnType,
                IsNullable = true,
            },
        };

        var sql = JoinSql(generator.Generate([operation], context.Model));

        Assert.Contains(
            $"UPDATE `Entries` SET `Value` = {expectedLiteral} WHERE `Value` IS NULL;",
            sql,
            StringComparison.Ordinal);
        Assert.Contains("MODIFY COLUMN `Value`", sql, StringComparison.Ordinal);
    }

    /// <summary>
    /// Prevents the provider from inventing data that may violate a store domain,
    /// a check constraint, or application semantics.
    /// </summary>
    [Theory]
    [InlineData(typeof(string), "varchar(64)")]
    [InlineData(typeof(int), "int")]
    [InlineData(typeof(byte[]), "varbinary(16)")]
    [InlineData(typeof(DateOnly), "date")]
    [InlineData(typeof(DateTime), "datetime(6)")]
    [InlineData(typeof(string), "json")]
    [InlineData(typeof(byte[]), "point")]
    [InlineData(typeof(string), "enum('alpha','beta')")]
    public void Nullable_column_becoming_required_rejects_an_implicit_backfill(
        Type clrType,
        string columnType
    )
    {
        using var context = CreateMySqlContext();
        var generator = context.GetService<IMigrationsSqlGenerator>();
        var operation = new AlterColumnOperation
        {
            Table = "Entries",
            Name = "SecretColumn",
            ClrType = clrType,
            ColumnType = columnType,
            IsNullable = false,
            OldColumn =
            {
                ClrType = clrType,
                ColumnType = columnType,
                IsNullable = true,
            },
        };

        var exception = Assert.Throws<InvalidOperationException>(() => generator.Generate([operation], context.Model));

        Assert.Contains(nameof(AlterColumnOperation), exception.Message, StringComparison.Ordinal);
        Assert.Contains("explicit DefaultValue or DefaultValueSql", exception.Message, StringComparison.Ordinal);
        Assert.Contains("application contract", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Entries", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("SecretColumn", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Nullable_timestamp_becoming_required_requires_explicit_backfill()
    {
        using var context = CreateMySqlContext();
        var generator = context.GetService<IMigrationsSqlGenerator>();
        var operation = CreateNullableTimestampRepair();

        var exception = Assert.Throws<InvalidOperationException>(() => generator.Generate([operation], context.Model));

        Assert.Contains(nameof(AlterColumnOperation), exception.Message, StringComparison.Ordinal);
        Assert.Contains("timestamp(6)", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Entries", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("OccurredAt", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Nullable_timestamp_uses_the_effective_model_store_type_when_the_operation_omits_it()
    {
        using var context = CreateTimestampMappingContext();
        var generator = context.GetService<IMigrationsSqlGenerator>();
        var operation = CreateNullableTimestampRepair();
        operation.ColumnType = null;
        operation.OldColumn.ColumnType = null;

        var exception = Assert.Throws<InvalidOperationException>(() => generator.Generate(
            [operation],
            context.GetService<IDesignTimeModel>()
                .Model));

        Assert.Contains(nameof(AlterColumnOperation), exception.Message, StringComparison.Ordinal);
        Assert.Contains("timestamp(6)", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Required_timestamp_that_was_already_required_needs_no_repair_backfill()
    {
        using var context = CreateMySqlContext();
        var generator = context.GetService<IMigrationsSqlGenerator>();
        var operation = CreateNullableTimestampRepair();
        operation.OldColumn.IsNullable = false;

        var sql = JoinSql(generator.Generate([operation], context.Model));

        Assert.DoesNotContain("UPDATE `Entries`", sql, StringComparison.Ordinal);
        Assert.Contains("MODIFY COLUMN `OccurredAt`", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Nullable_timestamp_becoming_required_rejects_an_explicit_clr_value()
    {
        using var context = CreateMySqlContext();
        var generator = context.GetService<IMigrationsSqlGenerator>();
        var operation = CreateNullableTimestampRepair();
        operation.DefaultValue = new DateTime(
            2026,
            8,
            21,
            12,
            34,
            56,
            789);

        var exception = Assert.Throws<InvalidOperationException>(() => generator.Generate([operation], context.Model));

        Assert.Contains("requires DefaultValueSql", exception.Message, StringComparison.Ordinal);
        Assert.Contains("session time zone", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Entries", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("OccurredAt", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Nullable_timestamp_becoming_required_accepts_explicit_sql_expression()
    {
        using var context = CreateMySqlContext();
        var generator = context.GetService<IMigrationsSqlGenerator>();
        var operation = CreateNullableTimestampRepair();
        operation.DefaultValueSql = "CURRENT_TIMESTAMP(6)";

        var sql = JoinSql(generator.Generate([operation], context.Model));

        Assert.Contains(
            "UPDATE `Entries` SET `OccurredAt` = CURRENT_TIMESTAMP(6) " + "WHERE `OccurredAt` IS NULL;",
            sql,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Nullable_timestamp_becoming_required_rejects_whitespace_sql_expression()
    {
        using var context = CreateMySqlContext();
        var generator = context.GetService<IMigrationsSqlGenerator>();
        var operation = CreateNullableTimestampRepair();
        operation.DefaultValueSql = "   ";

        var exception = Assert.Throws<InvalidOperationException>(() => generator.Generate([operation], context.Model));

        Assert.Contains("explicit DefaultValue or DefaultValueSql", exception.Message, StringComparison.Ordinal);
    }

    private static AlterColumnOperation CreateNullableTimestampRepair() => new()
    {
        Table = "Entries",
        Name = "OccurredAt",
        ClrType = typeof(DateTime),
        ColumnType = "timestamp(6)",
        IsNullable = false,
        OldColumn =
        {
            ClrType = typeof(DateTime),
            ColumnType = "timestamp(6)",
            IsNullable = true,
        },
    };

    [Fact]
    public void Qualified_table_operations_preserve_database_name()
    {
        using var context = CreateMySqlContext();
        var generator = context.GetService<IMigrationsSqlGenerator>();
        MigrationOperation[] operations =
        [
            new DropIndexOperation
            {
                Name = "IX_Entries_Code",
                Schema = "tenant_database",
                Table = "Entries",
            },
            new RenameIndexOperation
            {
                Name = "IX_Entries_Old",
                NewName = "IX_Entries_New",
                Schema = "tenant_database",
                Table = "Entries",
            },
            new DropForeignKeyOperation
            {
                Name = "FK_Entries_Parents",
                Schema = "tenant_database",
                Table = "Entries",
            },
            new RenameColumnOperation
            {
                Name = "OldCode",
                NewName = "Code",
                Schema = "tenant_database",
                Table = "Entries",
            },
        ];

        var sql = JoinSql(generator.Generate(operations, context.Model));

        Assert.Equal(
            4,
            sql.Split("ALTER TABLE `tenant_database`.`Entries`", StringSplitOptions.None).Length - 1);
    }

    // -- ALTER SEQUENCE --

    [Fact]
    public void AlterSequence_mysql_updates_persisted_metadata()
    {
        using var context = CreateMySqlContext();
        var generator = context.GetService<IMigrationsSqlGenerator>();
        var operation = new AlterSequenceOperation
        {
            Name = "TestSeq",
            IncrementBy = 5,
        };

        var commands = generator.Generate([operation], context.Model);
        var sql = JoinSql(commands);

        Assert.Contains("UPDATE `__efsequence_TestSeq`", sql, StringComparison.Ordinal);
        Assert.Contains("`increment_by` = 5", sql, StringComparison.Ordinal);
        Assert.Contains("`is_cyclic` = FALSE", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void AlterSequence_mariadb_generates_alter_sequence_sql()
    {
        using var context = CreateMariaDbContext();
        var generator = context.GetService<IMigrationsSqlGenerator>();
        var operation = new AlterSequenceOperation
        {
            Name = "TestSeq",
            IncrementBy = 5,
        };

        var commands = generator.Generate([operation], context.Model);
        var sql = JoinSql(commands);

        Assert.Contains("ALTER SEQUENCE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("`TestSeq`", sql, StringComparison.Ordinal);
        Assert.Contains("INCREMENT BY 5", sql, StringComparison.OrdinalIgnoreCase);
    }

    // -- CREATE SEQUENCE quoting (emulation + native) --

    [Fact]
    public void CreateSequence_emulation_quotes_table_name_with_backtick_prefix()
    {
        using var context = CreateMySqlContext();
        var generator = context.GetService<IMigrationsSqlGenerator>();
        var operation = new CreateSequenceOperation
        {
            Name = "OrderSeq",
            StartValue = 1,
            IncrementBy = 10,
            ClrType = typeof(long),
        };

        var sql = JoinSql(generator.Generate([operation], context.Model));

        Assert.Contains("CREATE TABLE `__efsequence_OrderSeq`", sql, StringComparison.Ordinal);
        Assert.Contains("`id` TINYINT UNSIGNED NOT NULL", sql, StringComparison.Ordinal);
        Assert.Contains("`value` BIGINT NOT NULL", sql, StringComparison.Ordinal);
        Assert.Contains("`start_value` BIGINT NOT NULL", sql, StringComparison.Ordinal);
        Assert.Contains("`increment_by` INT NOT NULL", sql, StringComparison.Ordinal);
        Assert.Contains("`min_value` BIGINT NOT NULL", sql, StringComparison.Ordinal);
        Assert.Contains("`max_value` BIGINT NOT NULL", sql, StringComparison.Ordinal);
        Assert.Contains("`is_cyclic` BOOLEAN NOT NULL", sql, StringComparison.Ordinal);
        Assert.Contains("`is_called` BOOLEAN NOT NULL", sql, StringComparison.Ordinal);
        Assert.Contains("PRIMARY KEY (`id`)", sql, StringComparison.Ordinal);
        Assert.Contains("CHECK (`id` = 1)", sql, StringComparison.Ordinal);
        Assert.Contains("INSERT INTO `__efsequence_OrderSeq`", sql, StringComparison.Ordinal);
        Assert.Contains(
            "VALUES (1, 1, 1, 10, 1, 9223372036854775806, FALSE, FALSE)",
            sql,
            StringComparison.Ordinal);
    }

    [Fact]
    public void CreateSequence_emulation_doubles_embedded_backticks_in_table_name()
    {
        using var context = CreateMySqlContext();
        var generator = context.GetService<IMigrationsSqlGenerator>();
        var operation = new CreateSequenceOperation
        {
            Name = "evil`seq",
            StartValue = 1,
            IncrementBy = 1,
            ClrType = typeof(long),
        };

        var sql = JoinSql(generator.Generate([operation], context.Model));

        Assert.Contains("`__efsequence_evil``seq`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("__efsequence_evil`seq`", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateSequence_native_quotes_sequence_name_with_backticks()
    {
        using var context = CreateMariaDbContext();
        var generator = context.GetService<IMigrationsSqlGenerator>();
        var operation = new CreateSequenceOperation
        {
            Name = "OrderSeq",
            StartValue = 100,
            IncrementBy = 5,
            ClrType = typeof(long),
        };

        var sql = JoinSql(generator.Generate([operation], context.Model));

        Assert.Contains("CREATE SEQUENCE `OrderSeq`", sql, StringComparison.Ordinal);
        Assert.Contains("START WITH 100", sql, StringComparison.Ordinal);
        Assert.Contains("INCREMENT BY 5", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateSequence_native_doubles_embedded_backticks_in_sequence_name()
    {
        using var context = CreateMariaDbContext();
        var generator = context.GetService<IMigrationsSqlGenerator>();
        var operation = new CreateSequenceOperation
        {
            Name = "evil`seq",
            StartValue = 1,
            IncrementBy = 1,
            ClrType = typeof(long),
        };

        var sql = JoinSql(generator.Generate([operation], context.Model));

        Assert.Contains("CREATE SEQUENCE `evil``seq`", sql, StringComparison.Ordinal);
    }

    // -- DROP SEQUENCE quoting --

    [Fact]
    public void DropSequence_emulation_quotes_emulation_table_name()
    {
        using var context = CreateMySqlContext();
        var generator = context.GetService<IMigrationsSqlGenerator>();
        var operation = new DropSequenceOperation { Name = "evil`seq" };

        var sql = JoinSql(generator.Generate([operation], context.Model));

        Assert.Contains("DROP TABLE IF EXISTS `__efsequence_evil``seq`", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void DropSequence_native_quotes_sequence_name()
    {
        using var context = CreateMariaDbContext();
        var generator = context.GetService<IMigrationsSqlGenerator>();
        var operation = new DropSequenceOperation { Name = "evil`seq" };

        var sql = JoinSql(generator.Generate([operation], context.Model));

        Assert.Contains("DROP SEQUENCE IF EXISTS `evil``seq`", sql, StringComparison.Ordinal);
    }

    // -- RENAME SEQUENCE quoting --

    [Fact]
    public void RenameSequence_emulation_quotes_old_and_new_emulation_table_names()
    {
        using var context = CreateMySqlContext();
        var generator = context.GetService<IMigrationsSqlGenerator>();
        var operation = new RenameSequenceOperation
        {
            Name = "OldSeq",
            NewName = "evil`new",
        };

        var sql = JoinSql(generator.Generate([operation], context.Model));

        Assert.Contains(
            "RENAME TABLE `__efsequence_OldSeq` TO `__efsequence_evil``new`",
            sql,
            StringComparison.Ordinal);
    }

    [Fact]
    public void RenameSequence_native_renames_the_sequence_table()
    {
        using var context = CreateMariaDbContext();
        var generator = context.GetService<IMigrationsSqlGenerator>();
        var operation = new RenameSequenceOperation
        {
            Name = "OldSeq",
            NewName = "NewSeq",
        };

        var sql = JoinSql(generator.Generate([operation], context.Model));

        Assert.Contains("RENAME TABLE `OldSeq` TO `NewSeq`", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void RestartSequence_native_updates_start_metadata_and_next_value()
    {
        using var context = CreateMariaDbContext();
        var generator = context.GetService<IMigrationsSqlGenerator>();
        var operation = new RestartSequenceOperation
        {
            Name = "TestSeq",
            StartValue = 3,
        };

        var sql = JoinSql(generator.Generate([operation], context.Model));

        Assert.Contains("START WITH 3 RESTART WITH 3", sql, StringComparison.Ordinal);
    }

    // -- INDEX METADATA --

    /// <summary>
    /// Verifies that prefix lengths and descending key parts retain their column order.
    /// </summary>
    [Fact]
    public void CreateIndex_with_prefix_and_direction_generates_exact_key_parts()
    {
        using var context = CreateMySqlContext();
        var generator = context.GetService<IMigrationsSqlGenerator>();
        var operation = new CreateIndexOperation
        {
            Name = "IX_Entries_Name_Code",
            Table = "Entries",
            Columns = ["Name", "Code"],
            IsDescending = [false, true],
        };

        operation.SetAnnotation(MySqlAnnotationNames.IndexPrefixLength, s_mixedPrefixLengths);

        var sql = JoinSql(generator.Generate([operation], context.Model));

        Assert.Contains(
            "CREATE INDEX `IX_Entries_Name_Code` ON `Entries` (`Name`(32), `Code` DESC)",
            sql,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies EF Core's empty direction array convention: every index key part
    /// is descending when <c>IndexBuilder.IsDescending()</c> has no arguments.
    /// </summary>
    [Fact]
    public void CreateIndex_with_empty_directions_marks_every_key_part_descending()
    {
        using var context = CreateMySqlContext();
        var generator = context.GetService<IMigrationsSqlGenerator>();
        var operation = new CreateIndexOperation
        {
            Name = "IX_Entries_First_Second",
            Table = "Entries",
            Columns = ["First", "Second"],
            IsDescending = [],
        };

        var sql = JoinSql(generator.Generate([operation], context.Model));

        Assert.Contains(
            "CREATE INDEX `IX_Entries_First_Second` ON `Entries` (`First` DESC, `Second` DESC)",
            sql,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that the full-text annotation selects the native FULLTEXT index grammar.
    /// </summary>
    [Fact]
    public void CreateIndex_with_full_text_annotation_generates_full_text_index()
    {
        using var context = CreateMySqlContext();
        var generator = context.GetService<IMigrationsSqlGenerator>();
        var operation = new CreateIndexOperation
        {
            Name = "IX_Entries_Body",
            Table = "Entries",
            Columns = ["Body"],
        };

        operation.SetAnnotation(MySqlAnnotationNames.FullTextIndex, true);

        var sql = JoinSql(generator.Generate([operation], context.Model));

        Assert.Contains(
            "CREATE FULLTEXT INDEX `IX_Entries_Body` ON `Entries` (`Body`)",
            sql,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that invalid provider index metadata fails before incomplete SQL can be emitted.
    /// </summary>
    [Theory]
    [InlineData("prefix-count")]
    [InlineData("negative-prefix")]
    [InlineData("unique-full-text")]
    [InlineData("prefixed-full-text")]
    [InlineData("spatial-full-text")]
    [InlineData("prefixed-spatial")]
    public void CreateIndex_with_invalid_provider_metadata_is_rejected(
        string scenario
    )
    {
        using var context = CreateMySqlContext();
        var generator = context.GetService<IMigrationsSqlGenerator>();
        var operation = new CreateIndexOperation
        {
            Name = "IX_Invalid",
            Table = "Entries",
            Columns = ["Name", "Code"],
        };

        ConfigureInvalidIndex(operation, scenario);

        Assert.Throws<InvalidOperationException>(
            () => generator.Generate([operation], context.Model));
    }

    // -- SPATIAL INDEX --

    [Fact]
    public void CreateIndex_with_spatial_annotation_generates_spatial_index()
    {
        using var context = CreateMySqlContext();
        var generator = context.GetService<IMigrationsSqlGenerator>();
        var operation = new CreateIndexOperation
        {
            Name = "IX_Location",
            Table = "Places",
            Columns = ["Location"],
        };

        operation.SetAnnotation(MySqlAnnotationNames.SpatialIndex, true);

        var commands = generator.Generate([operation], context.Model);
        var sql = JoinSql(commands);

        Assert.Contains("SPATIAL INDEX", sql, StringComparison.OrdinalIgnoreCase);
    }

    // -- JSON translation gaps --

    [Fact]
    public void JsonReplace_translates_to_json_replace_sql()
    {
        using var context = CreateJsonContext();
        var sql = context
            .Set<JsonEntity>()
            .Select(e => EF.Functions.JsonReplace(e.Data, "$.name", "updated"))
            .ToQueryString();

        Assert.Contains("JSON_REPLACE", sql, StringComparison.OrdinalIgnoreCase);
    }

    // -- TimeOnly.AddHours / AddMinutes --

    [Fact]
    public void TimeOnly_AddHours_translates_to_interval_hour()
    {
        using var context = CreateTemporalContext();
        var sql = context
            .Set<TemporalEntity>()
            .Where(e => e.StartTime.AddHours(2) > new TimeOnly(14, 0))
            .ToQueryString();

        Assert.Contains("INTERVAL", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("HOUR", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TimeOnly_AddMinutes_translates_to_interval_minute()
    {
        using var context = CreateTemporalContext();
        var sql = context
            .Set<TemporalEntity>()
            .Where(e => e.StartTime.AddMinutes(30) > new TimeOnly(14, 0))
            .ToQueryString();

        Assert.Contains("INTERVAL", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("MINUTE", sql, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies that convention-generated foreign-key names are normalized to the
    /// cross-engine 64-character identifier limit before model validation.
    /// </summary>
    [Fact]
    public void Model_with_long_fk_name_builds_with_normalized_constraint_name()
    {
        var builder = MySqlFunctionalTestOptions.CreateTransientBuilder<ConstraintTestContext>();
        builder.UseMySql(
            "Server=localhost;Database=doka;User ID=root;Password=password;",
            MySqlServerVersion.MySql(new Version(8, 4, 0)));

        using var context = new ConstraintTestContext(builder.Options);
        var childEntity = context.Model.FindEntityType(typeof(ChildEntity))
            ?? throw new InvalidOperationException("ChildEntity metadata was not created.");

        var foreignKey = Assert.Single(childEntity.GetForeignKeys());
        var constraintName = foreignKey.GetConstraintName()
            ?? throw new InvalidOperationException("Foreign-key constraint name was not generated.");

        Assert.True(
            constraintName.Length <= 64,
            $"Convention-generated constraint name '{constraintName}' exceeds 64 characters.");
    }

    /// <summary>
    /// Verifies that every migration path carrying user text uses the same
    /// SQL-mode-independent UTF-8 literal contract.
    /// </summary>
    [Fact]
    public void Migration_text_payloads_use_mode_independent_utf8_literals()
    {
        const string tableComment = "table\\comment 'quoted'";
        const string columnComment = "column\\comment 'quoted'";
        const string insertedValue = "inserted\\value";
        const string updatedValue = "updated\\value";
        const string updateKey = "update\\key";
        const string deleteKey = "delete\\key";
        using var context = CreateMySqlContext();
        var generator = context.GetService<IMigrationsSqlGenerator>();
        var createTable = new CreateTableOperation
        {
            Name = "LiteralContracts",
            Comment = tableComment,
        };

        createTable.Columns.Add(
            new AddColumnOperation
            {
                Table = createTable.Name,
                Name = "Value",
                ClrType = typeof(string),
                ColumnType = "longtext",
                IsNullable = false,
                Comment = columnComment,
            });
        var insertData = new InsertDataOperation
        {
            Table = createTable.Name,
            Columns = ["Value"],
            ColumnTypes = ["longtext"],
            Values = new object[,]
            {
                { insertedValue },
            },
        };

        var updateData = new UpdateDataOperation
        {
            Table = createTable.Name,
            Columns = ["Value"],
            ColumnTypes = ["longtext"],
            Values = new object[,]
            {
                { updatedValue },
            },
            KeyColumns = ["Value"],
            KeyColumnTypes = ["longtext"],
            KeyValues = new object[,]
            {
                { updateKey },
            },
        };

        var deleteData = new DeleteDataOperation
        {
            Table = createTable.Name,
            KeyColumns = ["Value"],
            KeyColumnTypes = ["longtext"],
            KeyValues = new object[,]
            {
                { deleteKey },
            },
        };

        var sql = JoinSql(generator.Generate([createTable, insertData, updateData, deleteData], context.Model));

        Assert.Contains(
            "/*! SET @__doka_previous_sql_mode = @@SESSION.sql_mode */;",
            sql,
            StringComparison.Ordinal);
        Assert.Contains("'NO_BACKSLASH_ESCAPES'", sql, StringComparison.Ordinal);
        Assert.Contains(
            "/*! SET SESSION sql_mode = @__doka_previous_sql_mode */;",
            sql,
            StringComparison.Ordinal);
        Assert.Contains($"COMMENT = {GenerateExpectedDdlComment(tableComment)}", sql, StringComparison.Ordinal);
        Assert.Contains($"COMMENT {GenerateExpectedDdlComment(columnComment)}", sql, StringComparison.Ordinal);
        Assert.Contains(GenerateExpectedHexLiteral(insertedValue), sql, StringComparison.Ordinal);
        Assert.Contains(GenerateExpectedHexLiteral(updatedValue), sql, StringComparison.Ordinal);
        Assert.Contains(GenerateExpectedHexLiteral(updateKey), sql, StringComparison.Ordinal);
        Assert.Contains(GenerateExpectedHexLiteral(deleteKey), sql, StringComparison.Ordinal);
    }

    // -- HiLo for short type --

    [Fact]
    public void UseHiLo_on_short_property_sets_strategy()
    {
        var builder = MySqlFunctionalTestOptions.CreateTransientBuilder<HiLoShortContext>();
        builder.UseMySql(
            "Server=localhost;Database=doka;User ID=root;Password=password;",
            MySqlServerVersion.MySql(new Version(8, 4, 0)));

        using var context = new HiLoShortContext(builder.Options);
        var property = context.Model.FindEntityType(typeof(HiLoShortEntity))!.FindProperty(nameof(HiLoShortEntity.Id))!;

        Assert.Equal(MySqlValueGenerationStrategy.HiLo, property.GetMySqlValueGenerationStrategy());
    }

    // -- Helpers --

    private static string JoinSql(
        IReadOnlyList<MigrationCommand> commands
    ) => string.Join("\n", commands.Select(c => c.CommandText));

    private static string GenerateExpectedHexLiteral(
        string value
    ) => $"_utf8mb4 X'{Convert.ToHexString(Encoding.UTF8.GetBytes(value))}'";

    private static string GenerateExpectedDdlComment(
        string value
    ) => $"'{value.Replace("'", "''", StringComparison.Ordinal)}'";

    private static void ConfigureInvalidIndex(
        CreateIndexOperation operation,
        string scenario
    )
    {
        switch (scenario)
        {
            case "prefix-count":
                operation.SetAnnotation(MySqlAnnotationNames.IndexPrefixLength, s_singlePrefixLength);
                break;
            case "negative-prefix":
                operation.SetAnnotation(MySqlAnnotationNames.IndexPrefixLength, s_invalidPrefixLengths);
                break;
            case "unique-full-text":
                operation.IsUnique = true;
                operation.SetAnnotation(MySqlAnnotationNames.FullTextIndex, true);
                break;
            case "prefixed-full-text":
                operation.SetAnnotation(MySqlAnnotationNames.FullTextIndex, true);
                operation.SetAnnotation(MySqlAnnotationNames.IndexPrefixLength, s_fullTextPrefixLengths);
                break;
            case "spatial-full-text":
                operation.Columns = ["Location"];
                operation.SetAnnotation(MySqlAnnotationNames.SpatialIndex, true);
                operation.SetAnnotation(MySqlAnnotationNames.FullTextIndex, true);
                break;
            case "prefixed-spatial":
                operation.Columns = ["Location"];
                operation.SetAnnotation(MySqlAnnotationNames.SpatialIndex, true);
                operation.SetAnnotation(MySqlAnnotationNames.IndexPrefixLength, s_singlePrefixLength);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(scenario), scenario, "Unknown test scenario.");
        }
    }

    private static DdlCoverageContext CreateMySqlContext()
    {
        var builder = MySqlFunctionalTestOptions.CreateTransientBuilder<DdlCoverageContext>();
        builder.UseMySql(
            "Server=localhost;Database=doka;User ID=root;Password=password;",
            MySqlServerVersion.MySql(new Version(8, 4, 0)));
        return new DdlCoverageContext(builder.Options);
    }

    private static DdlCoverageContext CreateMariaDbContext(
        Version? version = null
    )
    {
        var builder = MySqlFunctionalTestOptions.CreateTransientBuilder<DdlCoverageContext>();
        builder.UseMySql(
            "Server=localhost;Database=doka;User ID=root;Password=password;",
            MySqlServerVersion.MariaDb(version ?? new Version(11, 8, 0)));
        return new DdlCoverageContext(builder.Options);
    }

    private static JsonFuncContext CreateJsonContext()
    {
        var builder = MySqlFunctionalTestOptions.CreateTransientBuilder<JsonFuncContext>();
        builder.UseMySql(
            "Server=localhost;Database=doka;User ID=root;Password=password;",
            MySqlServerVersion.MySql(new Version(8, 4, 0)));
        return new JsonFuncContext(builder.Options);
    }

    private static TemporalContext CreateTemporalContext()
    {
        var builder = MySqlFunctionalTestOptions.CreateTransientBuilder<TemporalContext>();
        builder.UseMySql(
            "Server=localhost;Database=doka;User ID=root;Password=password;",
            MySqlServerVersion.MySql(new Version(8, 4, 0)));
        return new TemporalContext(builder.Options);
    }

    private static TimestampMappingContext CreateTimestampMappingContext()
    {
        var builder = MySqlFunctionalTestOptions.CreateTransientBuilder<TimestampMappingContext>();
        builder.UseMySql(
            "Server=localhost;Database=doka;User ID=root;Password=password;",
            MySqlServerVersion.MySql(new Version(8, 4, 0)));

        return new TimestampMappingContext(builder.Options);
    }

    // -- Entities / Contexts --

    private sealed class DdlCoverageContext : DbContext
    {
        public DdlCoverageContext(
            DbContextOptions<DdlCoverageContext> options
        ) : base(options) { }

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        ) => modelBuilder.Entity<DdlEntity>(e => { e.HasKey(x => x.Id); });
    }

    private sealed class DdlEntity
    {
        public int Id { get; set; }
    }

    private sealed class JsonFuncContext : DbContext
    {
        public JsonFuncContext(
            DbContextOptions<JsonFuncContext> options
        ) : base(options) { }

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<JsonEntity>(e =>
            {
                e.HasKey(x => x.Id);
                e
                    .Property(x => x.Data)
                    .HasColumnType("json");
            });
        }
    }

    private sealed class JsonEntity
    {
        public int Id { get; set; }
        public string Data { get; set; } = "{}";
    }

    private sealed class TemporalContext : DbContext
    {
        public TemporalContext(
            DbContextOptions<TemporalContext> options
        ) : base(options) { }

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        ) => modelBuilder.Entity<TemporalEntity>(e => e.HasKey(x => x.Id));
    }

    private sealed class TemporalEntity
    {
        public int Id { get; set; }
        public TimeOnly StartTime { get; set; }
    }

    private sealed class TimestampMappingContext : DbContext
    {
        public TimestampMappingContext(
            DbContextOptions<TimestampMappingContext> options
        ) : base(options) { }

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        ) => modelBuilder.Entity<TimestampMappingEntity>(entity =>
        {
            entity.ToTable("Entries");
            entity.HasKey(candidate => candidate.Id);
            entity
                .Property(candidate => candidate.OccurredAt)
                .HasColumnType("timestamp(6)");
        });
    }

    private sealed class TimestampMappingEntity
    {
        public int Id { get; set; }
        public DateTime OccurredAt { get; set; }
    }

    private sealed class ConstraintTestContext : DbContext
    {
        public ConstraintTestContext(
            DbContextOptions<ConstraintTestContext> options
        ) : base(options) { }

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<ParentEntity>(e =>
            {
                e.ToTable("Parents");
                e.HasKey(x => x.Id);
            });

            // The long table name makes the convention-generated foreign-key name exceed
            // the shared MySQL/MariaDB identifier limit before normalization.
            modelBuilder.Entity<ChildEntity>(e =>
            {
                e.ToTable("ChildrenWithVeryLongTableNameThatExceedsTheSixtyFourCharacterLimit");
                e.HasKey(x => x.Id);
                e
                    .HasOne<ParentEntity>()
                    .WithMany()
                    .HasForeignKey(x => x.ParentId);
            });
        }
    }

    private sealed class ParentEntity
    {
        public int Id { get; set; }
    }

    private sealed class ChildEntity
    {
        public int Id { get; set; }
        public int ParentId { get; set; }
    }

    private sealed class HiLoShortContext : DbContext
    {
        public HiLoShortContext(
            DbContextOptions<HiLoShortContext> options
        ) : base(options) { }

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<HiLoShortEntity>(e =>
            {
                e.HasKey(x => x.Id);
                e
                    .Property(x => x.Id)
                    .UseHiLo("ShortSeq");
            });
        }
    }

    private sealed class HiLoShortEntity
    {
        public short Id { get; set; }
    }
}
