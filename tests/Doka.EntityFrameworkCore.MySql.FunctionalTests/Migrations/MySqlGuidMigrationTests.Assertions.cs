namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

/// <summary>Contains the assertions responsibility for MySqlGuidMigrationTests.</summary>
public sealed partial class MySqlGuidMigrationTests
{
    private static void AssertTransition(
        GuidMigrationProbe result,
        string oldStoreType,
        string newStoreType,
        Type oldClrType,
        Type newClrType,
        string generatedType
    )
    {
        var operations = result.Operations;

        var dropForeignKey = Assert.Single(operations.OfType<DropForeignKeyOperation>());
        var alterColumns = operations
            .OfType<AlterColumnOperation>()
            .ToArray();

        var addForeignKey = Assert.Single(operations.OfType<AddForeignKeyOperation>());

        Assert.Equal(ForeignKeyName, dropForeignKey.Name);
        Assert.Equal("TextGuidDocumentRevisions", dropForeignKey.Table);
        Assert.Collection(
            alterColumns.OrderBy(operation => operation.Table, StringComparer.Ordinal),
            operation => AssertColumnTransition(
                operation,
                "TextGuidDocumentRevisions",
                "DocumentId",
                oldStoreType,
                newStoreType,
                oldClrType,
                newClrType),
            operation => AssertColumnTransition(
                operation,
                "TextGuidDocuments",
                "Id",
                oldStoreType,
                newStoreType,
                oldClrType,
                newClrType));
        Assert.Equal(ForeignKeyName, addForeignKey.Name);
        Assert.Equal("TextGuidDocumentRevisions", addForeignKey.Table);
        Assert.Equal(["DocumentId"], addForeignKey.Columns);
        Assert.Equal("TextGuidDocuments", addForeignKey.PrincipalTable);
        Assert.NotNull(addForeignKey.PrincipalColumns);
        Assert.Equal(["Id"], addForeignKey.PrincipalColumns);
        Assert.Equal(ReferentialAction.Cascade, addForeignKey.OnDelete);
        Assert.True(operations.IndexOf(dropForeignKey) < operations.IndexOf(alterColumns[0]));
        Assert.True(operations.IndexOf(dropForeignKey) < operations.IndexOf(alterColumns[1]));
        Assert.True(operations.IndexOf(addForeignKey) > operations.IndexOf(alterColumns[0]));
        Assert.True(operations.IndexOf(addForeignKey) > operations.IndexOf(alterColumns[1]));
        Assert.Empty(operations.OfType<DropIndexOperation>());
        Assert.Empty(operations.OfType<CreateIndexOperation>());

        var migrationCode = result.Code;

        Assert.Contains(generatedType, migrationCode, StringComparison.Ordinal);
        AssertCompilationSucceeded(result.Compilation);

        Assert.Null(result.SqlFailure);
    }

    private static void AssertApplicationNativeBinaryTransitionRejected(
        GuidMigrationProbe result
    )
    {
        var operations = result.Operations;
        var alterColumns = operations
            .OfType<AlterColumnOperation>()
            .ToArray();

        Assert.Equal(2, alterColumns.Length);
        Assert.All(alterColumns, operation =>
        {
            var sourceFormat = operation.OldColumn.FindAnnotation(MySqlAnnotationNames.GuidFormat)?.Value;
            var targetFormat = operation.FindAnnotation(MySqlAnnotationNames.GuidFormat)?.Value;

            Assert.True(sourceFormat is null ^ targetFormat is null);
            Assert.True(
                Equals(sourceFormat, MySqlGuidFormat.Binary16)
                || Equals(targetFormat, MySqlGuidFormat.Binary16));
        });

        AssertExplicitMigrationFailure(result.SqlFailure, "explicit data migration");
    }

    private static void AssertColumnTransition(
        AlterColumnOperation operation,
        string table,
        string column,
        string oldStoreType,
        string newStoreType,
        Type oldClrType,
        Type newClrType
    )
    {
        Assert.Equal(table, operation.Table);
        Assert.Equal(column, operation.Name);
        Assert.Equal(newStoreType, operation.ColumnType);
        Assert.Equal(oldStoreType, operation.OldColumn.ColumnType);
        Assert.Equal(newClrType, operation.ClrType);
        Assert.Equal(oldClrType, operation.OldColumn.ClrType);
    }

    private static void AssertNullabilityTransition(
        GuidMigrationProbe result,
        MySqlGuidFormat guidFormat,
        string storeType,
        bool isNullable,
        bool oldIsNullable,
        object? expectedDefault
    )
    {
        var operations = result.Operations;
        var alterColumn = Assert.Single(
            operations.OfType<AlterColumnOperation>(),
            operation => operation.Name == "AssigneeId");

        AssertGuidColumn(alterColumn, storeType, isNullable, guidFormat);
        AssertGuidColumn(alterColumn.OldColumn, storeType, oldIsNullable, guidFormat);
        Assert.Equal(expectedDefault, alterColumn.DefaultValue);
        Assert.Null(alterColumn.OldColumn.DefaultValue);

        var migrationCode = result.Code;

        Assert.Contains("AlterColumn<Guid>", migrationCode, StringComparison.Ordinal);
        Assert.Contains("oldClrType: typeof(Guid)", migrationCode, StringComparison.Ordinal);
        Assert.DoesNotContain("AlterColumn<string>", migrationCode, StringComparison.Ordinal);
        Assert.DoesNotContain("AlterColumn<byte[]>", migrationCode, StringComparison.Ordinal);
        AssertCompilationSucceeded(result.Compilation);

        if (oldIsNullable && !isNullable)
        {
            AssertExplicitMigrationFailure(result.SqlFailure, "explicit DefaultValue or DefaultValueSql");
        }
    }

    private static void AssertNativeGuidFormatTransition(
        GuidMigrationProbe result,
        MySqlGuidFormat sourceFormat,
        string sourceStoreType,
        MySqlGuidFormat targetFormat,
        string targetStoreType,
        bool containsNullableColumn
    )
    {
        var operations = result.Operations;
        var dropForeignKey = Assert.Single(operations.OfType<DropForeignKeyOperation>());
        var alterColumns = operations
            .OfType<AlterColumnOperation>()
            .ToArray();

        var addForeignKey = Assert.Single(operations.OfType<AddForeignKeyOperation>());

        Assert.Equal(2, alterColumns.Length);
        Assert.All(alterColumns, operation =>
        {
            AssertGuidColumn(operation, targetStoreType, operation.IsNullable, targetFormat);
            AssertGuidColumn(
                operation.OldColumn,
                sourceStoreType,
                operation.OldColumn.IsNullable,
                sourceFormat);
            Assert.Equal(operation.IsNullable, operation.OldColumn.IsNullable);
            Assert.True(operations.IndexOf(dropForeignKey) < operations.IndexOf(operation));
            Assert.True(operations.IndexOf(addForeignKey) > operations.IndexOf(operation));
        });

        if (containsNullableColumn)
        {
            Assert.Contains(alterColumns, operation => operation.IsNullable);
            Assert.Contains(alterColumns, operation => !operation.IsNullable);
        }
        else
        {
            Assert.All(alterColumns, operation => Assert.False(operation.IsNullable));
        }

        var migrationCode = result.Code;

        Assert.Contains("AlterColumn<Guid>", migrationCode, StringComparison.Ordinal);
        Assert.Contains("oldClrType: typeof(Guid)", migrationCode, StringComparison.Ordinal);
        Assert.DoesNotContain("AlterColumn<string>", migrationCode, StringComparison.Ordinal);
        Assert.DoesNotContain("AlterColumn<byte[]>", migrationCode, StringComparison.Ordinal);
        AssertCompilationSucceeded(result.Compilation);
        AssertExplicitMigrationFailure(result.SqlFailure, "explicit data migration");
    }

    private static void AssertNativeGuidFormatAndNullabilityTransition(
        GuidMigrationProbe result,
        MySqlGuidFormat sourceFormat,
        string sourceStoreType,
        MySqlGuidFormat targetFormat,
        string targetStoreType,
        bool sourceIsNullable
    )
    {
        var operations = result.Operations;
        var dropForeignKey = Assert.Single(operations.OfType<DropForeignKeyOperation>());
        var alterColumns = operations
            .OfType<AlterColumnOperation>()
            .ToArray();

        var addForeignKey = Assert.Single(operations.OfType<AddForeignKeyOperation>());

        Assert.Equal(2, alterColumns.Length);
        Assert.All(alterColumns, operation =>
        {
            AssertGuidColumn(operation, targetStoreType, operation.IsNullable, targetFormat);
            AssertGuidColumn(
                operation.OldColumn,
                sourceStoreType,
                operation.OldColumn.IsNullable,
                sourceFormat);
            Assert.True(operations.IndexOf(dropForeignKey) < operations.IndexOf(operation));
            Assert.True(operations.IndexOf(addForeignKey) > operations.IndexOf(operation));
        });

        var foreignKeyColumn = Assert.Single(
            alterColumns,
            operation => operation.Name == "AssigneeId");

        Assert.Equal(!sourceIsNullable, foreignKeyColumn.IsNullable);
        Assert.Equal(sourceIsNullable, foreignKeyColumn.OldColumn.IsNullable);
        Assert.Null(foreignKeyColumn.DefaultValue);
        Assert.Null(foreignKeyColumn.OldColumn.DefaultValue);

        var migrationCode = result.Code;

        Assert.Contains("AlterColumn<Guid>", migrationCode, StringComparison.Ordinal);
        Assert.Contains("oldClrType: typeof(Guid)", migrationCode, StringComparison.Ordinal);
        Assert.DoesNotContain("AlterColumn<string>", migrationCode, StringComparison.Ordinal);
        Assert.DoesNotContain("AlterColumn<byte[]>", migrationCode, StringComparison.Ordinal);
        AssertCompilationSucceeded(result.Compilation);
        AssertExplicitMigrationFailure(result.SqlFailure, "explicit data migration");
    }

    /// <summary>Verifies the fail-closed storage transition diagnostic captured during generation.</summary>
    private static void AssertExplicitMigrationFailure(
        Exception? failure,
        string expectedContract
    )
    {
        var exception = Assert.IsType<InvalidOperationException>(failure);

        Assert.Contains(expectedContract, exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("GuidEvolution", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("GuidWork", exception.Message, StringComparison.Ordinal);
    }

    private static void AssertGuidColumn(
        ColumnOperation operation,
        string storeType,
        bool isNullable,
        MySqlGuidFormat guidFormat = MySqlGuidFormat.Char36
    )
    {
        Assert.Equal(typeof(Guid), operation.ClrType);
        Assert.Equal(storeType, operation.ColumnType);
        Assert.Equal(isNullable, operation.IsNullable);
        Assert.Equal(
            guidFormat,
            operation.FindAnnotation(MySqlAnnotationNames.GuidFormat)?.Value);
    }

    /// <summary>Asserts the compiler result already captured in the migration pipeline's Act phase.</summary>
    private static void AssertCompilationSucceeded(
        (bool Success, string[] Errors) result
    ) => Assert.True(result.Success, string.Join(Environment.NewLine, result.Errors));
}
