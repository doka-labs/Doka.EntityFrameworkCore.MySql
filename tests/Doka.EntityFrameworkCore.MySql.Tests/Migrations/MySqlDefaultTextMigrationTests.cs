namespace Doka.EntityFrameworkCore.MySql.Tests;

/// <summary>Verifies canonical migration defaults and explicit-versus-inherited text intent without a live connection.</summary>
public sealed partial class MySqlDefaultTextMigrationTests
{
    /// <summary>Options and explicit model defaults describe the same physical schema.</summary>
    /// <param name="mariaDb">Whether to use the MariaDB dialect.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Options_and_explicit_model_defaults_have_no_migration_difference(
        bool mariaDb
    )
    {
        // Arrange
        using var source = CreateContext(new TextShape { ExplicitModel = true }, null, null, mariaDb);
        using var target = CreateContext(mariaDb: mariaDb);

        // Act
        var difference = Compare(source, target);

        // Assert
        Assert.False(difference.HasDifferences);
        Assert.Empty(difference.Operations);
        Assert.Empty(difference.Sql);
    }

    /// <summary>Initial schema creation carries the option defaults before inheriting tables.</summary>
    /// <param name="mariaDb">Whether to use the MariaDB dialect.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Initial_option_defaults_precede_inheriting_create_table(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb: mariaDb);
        var model = DesignModel(context);

        // Act
        var operations = context
            .GetService<IMigrationsModelDiffer>()
            .GetDifferences(null, model.GetRelationalModel());

        var sql = GenerateSql(context, operations);

        // Assert
        var database = Assert.Single(operations.OfType<AlterDatabaseOperation>());
        Assert.Equal(UnicodeCollation, database.Collation);
        Assert.Equal("utf8mb4",
            database.FindAnnotation(MySqlAnnotationNames.CharSet)
                ?.Value);

        Assert.True(sql.IndexOf("ALTER DATABASE", StringComparison.Ordinal)
            < sql.IndexOf("CREATE TABLE", StringComparison.Ordinal));

        Assert.Contains("CHARACTER SET = utf8mb4 COLLATE = utf8mb4_unicode_ci", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("CONVERT TO", sql, StringComparison.Ordinal);
    }

    /// <summary>Historical operations retain their own values instead of consulting current options.</summary>
    /// <param name="historicalCollation">The value recorded in the historical operation.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("utf8mb4_bin")]
    public void Historical_database_operations_do_not_reconstruct_current_option_collation(
        string? historicalCollation
    )
    {
        // Arrange
        using var context = CreateContext();
        var operation = new AlterDatabaseOperation { Collation = historicalCollation };
        operation.SetAnnotation(MySqlAnnotationNames.CharSet, "utf8mb4");

        // Act
        var sql = GenerateSql(context, [operation]);

        // Assert
        Assert.DoesNotContain(UnicodeCollation, sql, StringComparison.Ordinal);

        if (historicalCollation is null)
        {
            Assert.DoesNotContain("COLLATE", sql, StringComparison.Ordinal);
        }
        else
        {
            Assert.Contains(historicalCollation, sql, StringComparison.Ordinal);
        }
    }

    /// <summary>A database-default change changes neither existing columns nor application values.</summary>
    [Fact]
    public void Changing_only_the_default_emits_no_column_rewrite()
    {
        // Arrange
        using var source = CreateContext();
        using var target = CreateContext(defaultCollation: BinaryCollation);

        // Act
        var difference = Compare(source, target);

        // Assert
        Assert.True(difference.HasDifferences);
        var database = Assert.Single(difference.Operations);
        var alter = Assert.IsType<AlterDatabaseOperation>(database);
        Assert.Equal(BinaryCollation, alter.Collation);
        Assert.Equal(UnicodeCollation, alter.OldDatabase.Collation);
        Assert.DoesNotContain("ALTER TABLE", difference.Sql, StringComparison.Ordinal);
        Assert.DoesNotContain("UPDATE ", difference.Sql, StringComparison.Ordinal);
    }

    /// <summary>Removing the last known default never invents a database-specific replacement.</summary>
    [Fact]
    public void Resetting_the_last_default_retains_the_explicit_target_guard()
    {
        // Arrange
        using var source = CreateContext();
        using var target = CreateContext(defaultCharSet: null, defaultCollation: null);
        var differ = target.GetService<IMigrationsModelDiffer>();

        // Act
        var operations = differ.GetDifferences(
            DesignModel(source)
                .GetRelationalModel(),
            DesignModel(target)
                .GetRelationalModel());

        var exception = Record.Exception(() => GenerateSql(target, operations));

        // Assert
        Assert.IsType<InvalidOperationException>(exception);
        var database = Assert.Single(operations.OfType<AlterDatabaseOperation>());
        Assert.Null(database.Collation);
        Assert.Equal(UnicodeCollation, database.OldDatabase.Collation);
    }

    /// <summary>Unchanged explicit property metadata remains a true no-op.</summary>
    /// <param name="guidColumn">Whether the explicit facets belong to a Char36 Guid.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Identical_explicit_column_and_global_defaults_have_no_difference(
        bool guidColumn
    )
    {
        // Arrange
        var shape = new TextShape
        {
            ColumnCollation = true,
            GuidColumn = guidColumn,
        };

        using var source = CreateContext(shape);
        using var target = CreateContext(shape);

        // Act
        var difference = Compare(source, target);

        // Assert
        Assert.False(difference.HasDifferences);
        Assert.Empty(difference.Operations);
        Assert.Empty(difference.Sql);
    }

    /// <summary>Changing explicit column collation records schema intent in either direction.</summary>
    /// <param name="guidColumn">Whether to test a Char36 Guid property.</param>
    /// <param name="reverse">Whether inherited collation becomes explicit rather than the reverse.</param>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Centralizing_or_explicitly_binding_column_collation_preserves_a_real_model_difference(
        bool guidColumn,
        bool reverse
    )
    {
        // Arrange
        var inherited = new TextShape { GuidColumn = guidColumn };
        var explicitColumn = inherited with { ColumnCollation = true };
        using var source = CreateContext(reverse
            ? inherited
            : explicitColumn);

        using var target = CreateContext(reverse
            ? explicitColumn
            : inherited);

        // Act
        var difference = Compare(source, target);

        // Assert
        Assert.True(difference.HasDifferences);
        var column = Assert.Single(difference.Operations.OfType<AlterColumnOperation>());
        Assert.Equal(guidColumn
                ? "ExternalId"
                : "Value",
            column.Name);

        Assert.Equal(reverse
                ? UnicodeCollation
                : null,
            column.Collation);

        Assert.Equal(!reverse
                ? UnicodeCollation
                : null,
            column.OldColumn.Collation);
    }

    /// <summary>A conflicting table default is the counterexample to global-name-only normalization.</summary>
    /// <param name="guidColumn">Whether the selected property is a Char36 Guid.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Removing_column_override_under_a_different_table_default_is_not_schema_equivalent(
        bool guidColumn
    )
    {
        // Arrange
        var original = new TextShape
        {
            GuidColumn = guidColumn,
            TableCollation = BinaryCollation,
            ColumnCollation = true,
        };

        using var source = CreateContext(original);
        using var target = CreateContext(original with { ColumnCollation = false });

        // Act
        var difference = Compare(source, target);

        // Assert
        Assert.True(difference.HasDifferences);
        var column = Assert.Single(difference.Operations.OfType<AlterColumnOperation>());
        Assert.Equal(UnicodeCollation, column.OldColumn.Collation);
        Assert.Null(column.Collation);
        Assert.Contains("MODIFY COLUMN", difference.Sql, StringComparison.Ordinal);
        Assert.Equal(BinaryCollation,
            DesignModel(target)
                .FindEntityType(typeof(TextRecord))
                ?
                .FindAnnotation(RelationalAnnotationNames.Collation)
                ?.Value);
    }

    /// <summary>Column length, nullability and defaults survive a simultaneous collation centralization.</summary>
    [Fact]
    public void Centralizing_collation_does_not_remove_other_column_changes()
    {
        // Arrange
        using var source = CreateContext(new TextShape { ColumnCollation = true });
        using var target = CreateContext(new TextShape
        {
            Length = 128,
            Nullable = true,
            DefaultSql = "'replacement'",
        });

        // Act
        var difference = Compare(source, target);

        // Assert
        var column = Assert.Single(difference.Operations.OfType<AlterColumnOperation>());
        Assert.True(column.IsNullable);
        Assert.Equal(128, column.MaxLength);
        Assert.Equal("'replacement'", column.DefaultValueSql);
        Assert.Equal(64, column.OldColumn.MaxLength);
        Assert.False(column.OldColumn.IsNullable);
        Assert.Contains("varchar(128)", difference.Sql, StringComparison.Ordinal);
    }

    /// <summary>A later global change preserves the distinction between fixed and inherited properties.</summary>
    [Fact]
    public void Future_default_change_retains_explicit_column_intent_without_converting_existing_columns()
    {
        // Arrange
        var shape = new TextShape { ColumnCollation = true };
        using var source = CreateContext(shape);
        using var target = CreateContext(shape, defaultCollation: BinaryCollation);

        // Act
        var difference = Compare(source, target);

        // Assert
        Assert.Single(difference.Operations.OfType<AlterDatabaseOperation>());
        Assert.Empty(difference.Operations.OfType<AlterColumnOperation>());
        Assert.Equal(UnicodeCollation,
            DesignModel(target)
                .FindEntityType(typeof(TextRecord))
                ?
                .FindProperty(nameof(TextRecord.Value))
                ?.GetCollation());

        Assert.DoesNotContain("MODIFY COLUMN", difference.Sql, StringComparison.Ordinal);
    }

    /// <summary>A simultaneous default change cannot conceal the independently changed column binding.</summary>
    [Fact]
    public void Centralization_and_global_default_change_remain_separate_operations()
    {
        // Arrange
        using var source = CreateContext(new TextShape { ColumnCollation = true });
        using var target = CreateContext(defaultCollation: BinaryCollation);

        // Act
        var difference = Compare(source, target);

        // Assert
        Assert.True(difference.HasDifferences);
        var database = Assert.Single(difference.Operations.OfType<AlterDatabaseOperation>());
        var column = Assert.Single(difference.Operations.OfType<AlterColumnOperation>());
        Assert.Equal(BinaryCollation, database.Collation);
        Assert.Equal(UnicodeCollation, column.OldColumn.Collation);
        Assert.Null(column.Collation);
    }

    /// <summary>An absent target default remains unknown rather than a fabricated inheritance-equivalence proof.</summary>
    [Fact]
    public void Centralizing_with_no_target_default_does_not_guess_a_replacement()
    {
        // Arrange
        using var source = CreateContext(new TextShape { ColumnCollation = true });
        using var target = CreateContext(defaultCharSet: null, defaultCollation: null);
        var differ = target.GetService<IMigrationsModelDiffer>();

        // Act
        var operations = differ.GetDifferences(DesignModel(source)
                .GetRelationalModel(),
            DesignModel(target)
                .GetRelationalModel());

        var exception = Record.Exception(() => GenerateSql(target, operations));

        // Assert
        var column = Assert.Single(operations.OfType<AlterColumnOperation>());
        Assert.Equal(UnicodeCollation, column.OldColumn.Collation);
        Assert.Null(column.Collation);
        Assert.IsType<InvalidOperationException>(exception);
    }
}
