using System.Text;
using Microsoft.EntityFrameworkCore.Update;

namespace Doka.EntityFrameworkCore.MySql.Tests;

/// <summary>
/// Covers provider identity-column classification at the update SQL boundary.
/// </summary>
public sealed class MySqlUpdateSqlGeneratorTests
{
    /// <summary>
    /// Verifies every key, read, write, property, and value-generation branch
    /// that decides whether generated values use LAST_INSERT_ID().
    /// </summary>
    [Fact]
    public void Identity_classification_requires_the_complete_generated_key_shape()
    {
        using var context = new IdentityContext(CreateOptions());
        var entityType = context.Model.FindEntityType(typeof(IdentityEntity))!;
        var autoIncrementProperty = entityType.FindProperty(nameof(IdentityEntity.Id))!;
        var neverGeneratedProperty = entityType.FindProperty(nameof(IdentityEntity.ExplicitId))!;
        var computedProperty = entityType.FindProperty(nameof(IdentityEntity.Computed))!;

        Assert.False(IsIdentity(autoIncrementProperty, isKey: false, isRead: true, isWrite: false));
        Assert.False(IsIdentity(autoIncrementProperty, isKey: true, isRead: false, isWrite: false));
        Assert.False(IsIdentity(autoIncrementProperty, isKey: true, isRead: true, isWrite: true));
        Assert.True(IsIdentity(property: null, isKey: true, isRead: true, isWrite: false));
        Assert.True(IsIdentity(autoIncrementProperty, isKey: true, isRead: true, isWrite: false));
        Assert.True(IsIdentity(neverGeneratedProperty, isKey: true, isRead: true, isWrite: false));
        Assert.False(IsIdentity(computedProperty, isKey: true, isRead: true, isWrite: false));
    }

    [Fact]
    public void Bulk_insert_without_write_columns_preserves_exact_multirow_sql_and_mapping()
    {
        using var context = new IdentityContext(CreateOptions());
        var generator = (MySqlUpdateSqlGenerator)context.GetService<IUpdateSqlGenerator>();
        var builder = new StringBuilder();
        IReadOnlyModificationCommand[] commands =
        [
            CreateCommand("Rows", schema: null),
            CreateCommand("Rows", schema: null),
            CreateCommand("Rows", schema: null),
        ];

        var mapping = generator.AppendBulkInsertOperation(
            builder,
            commands,
            commandPosition: 0,
            out var requiresTransaction);

        Assert.Equal(ResultSetMapping.NoResults, mapping);
        Assert.False(requiresTransaction);
        Assert.Equal(
            """
            INSERT INTO `Rows` ()
            VALUES (),
            (),
            ();

            """,
            builder.ToString(),
            ignoreLineEndingDifferences: true);
    }

    [Fact]
    public void Bulk_insert_with_write_columns_preserves_exact_multirow_sql_and_mapping()
    {
        using var context = new IdentityContext(CreateOptions());
        var generator = (MySqlUpdateSqlGenerator)context.GetService<IUpdateSqlGenerator>();
        var builder = new StringBuilder();
        IReadOnlyModificationCommand[] commands =
        [
            CreateWriteCommand("Rows", "p0_a", "p0_b"),
            CreateWriteCommand("Rows", "p1_a", "p1_b"),
            CreateWriteCommand("Rows", "p2_a", "p2_b"),
        ];

        var mapping = generator.AppendBulkInsertOperation(
            builder,
            commands,
            commandPosition: 0,
            out var requiresTransaction);

        Assert.Equal(ResultSetMapping.NoResults, mapping);
        Assert.False(requiresTransaction);
        Assert.Equal(
            """
            INSERT INTO `Rows` (`WriteA`, `WriteB`)
            VALUES (@p0_a, @p0_b),
            (@p1_a, @p1_b),
            (@p2_a, @p2_b);

            """,
            builder.ToString(),
            ignoreLineEndingDifferences: true);
    }

    [Fact]
    public void Bulk_insert_with_mixed_columns_reuses_filtered_buffer_and_preserves_exact_sql()
    {
        using var context = new IdentityContext(CreateOptions());
        var generator = (MySqlUpdateSqlGenerator)context.GetService<IUpdateSqlGenerator>();
        var builder = new StringBuilder();
        IReadOnlyModificationCommand[] commands =
        [
            CreateMixedWriteCommand("Rows", "p0_a", "p0_b"),
            CreateMixedWriteCommand("Rows", "p1_a", "p1_b"),
            CreateMixedWriteCommand("Rows", "p2_a", "p2_b"),
        ];

        var mapping = generator.AppendBulkInsertOperation(
            builder,
            commands,
            commandPosition: 0,
            out var requiresTransaction);

        Assert.Equal(ResultSetMapping.NoResults, mapping);
        Assert.False(requiresTransaction);
        Assert.Equal(
            """
            INSERT INTO `Rows` (`WriteA`, `WriteB`)
            VALUES (@p0_a, @p0_b),
            (@p1_a, @p1_b),
            (@p2_a, @p2_b);

            """,
            builder.ToString(),
            ignoreLineEndingDifferences: true);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void Bulk_insert_rejects_mismatched_write_column_count(
        int laterWriteColumnCount
    )
    {
        using var context = new IdentityContext(CreateOptions());
        var generator = (MySqlUpdateSqlGenerator)context.GetService<IUpdateSqlGenerator>();
        var builder = new StringBuilder();
        var laterColumnNames = laterWriteColumnCount == 1
            ? new[] { "WriteA", }
            : ["WriteA", "WriteB", "WriteC"];
        var laterModifications = laterColumnNames
            .Select(
                (
                    columnName,
                    index
                ) => (IColumnModification)CreateWriteModification(columnName, $"p1_{index}"))
            .ToArray();
        IReadOnlyModificationCommand[] commands =
        [
            CreateMixedWriteCommand("Rows", "p0_a", "p0_b"),
            new TestModificationCommand(
                "Rows",
                schema: null,
                laterModifications),
        ];

        var exception = Assert.Throws<InvalidOperationException>(
            () => generator.AppendBulkInsertOperation(
                builder,
                commands,
                commandPosition: 0,
                out _));

        Assert.Equal("Modification command shapes do not match.", exception.Message);
    }

    [Fact]
    public void Bulk_insert_returning_preserves_columns_that_are_read_and_written()
    {
        using var context = new IdentityContext(
            CreateOptions(MySqlServerVersion.MariaDb(new Version(11, 8, 0))));
        var generator = (MySqlUpdateSqlGenerator)context.GetService<IUpdateSqlGenerator>();
        var builder = new StringBuilder();
        IReadOnlyModificationCommand[] commands =
        [
            CreateReadWriteCommand("Rows", "p0_value"),
            CreateReadWriteCommand("Rows", "p1_value"),
        ];

        var mapping = generator.AppendBulkInsertOperation(
            builder,
            commands,
            commandPosition: 0,
            out var requiresTransaction);

        Assert.Equal(ResultSetMapping.NotLastInResultSet, mapping);
        Assert.False(requiresTransaction);
        Assert.Equal(
            """
            INSERT INTO `Rows` (`Value`)
            VALUES (@p0_value),
            (@p1_value)
            RETURNING `Value`;

            """,
            builder.ToString(),
            ignoreLineEndingDifferences: true);
    }

    [Fact]
    public void Bulk_insert_with_readback_rejects_an_uninitialized_provider_profile()
    {
        using var context = new IdentityContext(CreateOptions());
        var generator = new MySqlUpdateSqlGenerator(
            context.GetService<UpdateSqlGeneratorDependencies>(),
            [new MySqlSingletonOptions()]);
        var builder = new StringBuilder();
        IReadOnlyModificationCommand[] commands =
        [
            CreateReadWriteCommand("Rows", "p0_value"),
            CreateReadWriteCommand("Rows", "p1_value"),
        ];

        var exception = Assert.Throws<InvalidOperationException>(
            () => generator.AppendBulkInsertOperation(
                builder,
                commands,
                commandPosition: 0,
                out _));

        Assert.Equal(
            "The MySQL update SQL generator requires an initialized provider profile.",
            exception.Message);
        Assert.Empty(builder.ToString());
    }

    [Fact]
    public void Batch_shape_comparison_preserves_filtered_column_order_contract()
    {
        var baseline = CreateCommand(
            "Rows",
            schema: null,
            ("WriteA", false, true),
            ("Ignored", false, false),
            ("ReadA", true, false),
            ("WriteB", false, true),
            ("ReadB", true, false));
        var sameShape = CreateCommand(
            "Rows",
            schema: null,
            ("WriteA", false, true),
            ("ReadA", true, false),
            ("IgnoredElsewhere", false, false),
            ("WriteB", false, true),
            ("ReadB", true, false));
        var reorderedWrite = CreateCommand(
            "Rows",
            schema: null,
            ("WriteB", false, true),
            ("WriteA", false, true),
            ("ReadA", true, false),
            ("ReadB", true, false));
        var differentRead = CreateCommand(
            "Rows",
            schema: null,
            ("WriteA", false, true),
            ("WriteB", false, true),
            ("ReadA", true, false),
            ("ReadC", true, false));
        var extraWrite = CreateCommand(
            "Rows",
            schema: null,
            ("WriteA", false, true),
            ("WriteB", false, true),
            ("WriteC", false, true),
            ("ReadA", true, false),
            ("ReadB", true, false));
        var missingRead = CreateCommand(
            "Rows",
            schema: null,
            ("WriteA", false, true),
            ("WriteB", false, true),
            ("ReadA", true, false));
        var reorderedRead = CreateCommand(
            "Rows",
            schema: null,
            ("WriteA", false, true),
            ("WriteB", false, true),
            ("ReadB", true, false),
            ("ReadA", true, false));

        Assert.True(MySqlModificationCommandBatch.CanBeInsertedInSameStatement(baseline, sameShape));
        Assert.False(MySqlModificationCommandBatch.CanBeInsertedInSameStatement(baseline, reorderedWrite));
        Assert.False(MySqlModificationCommandBatch.CanBeInsertedInSameStatement(baseline, differentRead));
        Assert.False(MySqlModificationCommandBatch.CanBeInsertedInSameStatement(baseline, extraWrite));
        Assert.False(MySqlModificationCommandBatch.CanBeInsertedInSameStatement(extraWrite, baseline));
        Assert.False(MySqlModificationCommandBatch.CanBeInsertedInSameStatement(baseline, missingRead));
        Assert.False(MySqlModificationCommandBatch.CanBeInsertedInSameStatement(missingRead, baseline));
        Assert.False(MySqlModificationCommandBatch.CanBeInsertedInSameStatement(baseline, reorderedRead));
        Assert.False(
            MySqlModificationCommandBatch.CanBeInsertedInSameStatement(
                baseline,
                CreateCommand("OtherRows", schema: null)));
        Assert.False(
            MySqlModificationCommandBatch.CanBeInsertedInSameStatement(
                baseline,
                CreateCommand("Rows", "other")));
    }

    private static bool IsIdentity(
        IProperty? property,
        bool isKey,
        bool isRead,
        bool isWrite
    )
    {
        var typeMapping = property?.GetRelationalTypeMapping() ?? IntTypeMapping.Default;
        var parameters = new ColumnModificationParameters(
            columnName: property?.Name ?? "ShadowId",
            originalValue: null,
            value: null,
            property,
            columnType: typeMapping.StoreType,
            typeMapping,
            read: isRead,
            write: isWrite,
            key: isKey,
            condition: false,
            sensitiveLoggingEnabled: false,
            isNullable: false);

        return MySqlUpdateSqlGenerator.IsIdentityColumn(new ColumnModification(parameters));
    }

    private static TestModificationCommand CreateCommand(
        string tableName,
        string? schema,
        params (string Name, bool Read, bool Write)[] columns
    )
    {
        var modifications = new IColumnModification[columns.Length];
        for (var index = 0; index < columns.Length; index++)
        {
            var column = columns[index];
            modifications[index] = new ColumnModification(
                new ColumnModificationParameters(
                    column.Name,
                    originalValue: null,
                    value: 1,
                    property: null,
                    columnType: "int",
                    typeMapping: IntTypeMapping.Default,
                    read: column.Read,
                    write: column.Write,
                    key: false,
                    condition: false,
                    sensitiveLoggingEnabled: false,
                    isNullable: false));
        }

        return new TestModificationCommand(tableName, schema, modifications);
    }

    private static TestModificationCommand CreateWriteCommand(
        string tableName,
        string firstParameterName,
        string secondParameterName
    ) => new(
        tableName,
        schema: null,
        [
            CreateWriteModification("WriteA", firstParameterName),
            CreateWriteModification("WriteB", secondParameterName),
        ]);

    private static TestModificationCommand CreateMixedWriteCommand(
        string tableName,
        string firstParameterName,
        string secondParameterName
    ) => new(
        tableName,
        schema: null,
        [
            CreateWriteModification("WriteA", firstParameterName),
            CreateIgnoredModification("Ignored"),
            CreateWriteModification("WriteB", secondParameterName),
        ]);

    private static TestModificationCommand CreateReadWriteCommand(
        string tableName,
        string parameterName
    ) => new(
        tableName,
        schema: null,
        [CreateWriteModification("Value", parameterName, read: true)]);

    private static ColumnModification CreateIgnoredModification(
        string columnName
    ) => new(
        new ColumnModificationParameters(
            columnName,
            originalValue: null,
            value: 1,
            property: null,
            columnType: "int",
            typeMapping: IntTypeMapping.Default,
            read: false,
            write: false,
            key: false,
            condition: false,
            sensitiveLoggingEnabled: false,
            isNullable: false));

    private static ColumnModification CreateWriteModification(
        string columnName,
        string parameterName,
        bool read = false
    )
    {
        var parameters = new ColumnModificationParameters(
            columnName,
            originalValue: null,
            value: 1,
            property: null,
            columnType: "int",
            typeMapping: IntTypeMapping.Default,
            read,
            write: true,
            key: false,
            condition: false,
            sensitiveLoggingEnabled: false,
            isNullable: false)
        {
            GenerateParameterName = () => parameterName,
        };

        return new ColumnModification(parameters);
    }

    private static DbContextOptions<IdentityContext> CreateOptions(
        MySqlServerVersion? serverVersion = null
    ) => new DbContextOptionsBuilder<IdentityContext>()
        .UseMySql(
            "Server=localhost;Database=doka;User ID=root;Password=password;",
            serverVersion ?? MySqlServerVersion.MySql(new Version(8, 4, 0)))
        .Options;

    private sealed class IdentityContext : DbContext
    {
        public IdentityContext(
            DbContextOptions<IdentityContext> options
        ) : base(options) { }

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<IdentityEntity>(entity =>
            {
                entity.HasKey(candidate => candidate.Id);
                entity
                    .Property(candidate => candidate.ExplicitId)
                    .ValueGeneratedNever();
                entity
                    .Property(candidate => candidate.Computed)
                    .ValueGeneratedOnAddOrUpdate();
            });
        }
    }

    private sealed class TestModificationCommand : IReadOnlyModificationCommand
    {
        public TestModificationCommand(
            string tableName,
            string? schema,
            IReadOnlyList<IColumnModification> columnModifications
        )
        {
            TableName = tableName;
            Schema = schema;
            ColumnModifications = columnModifications;
        }

        public ITable? Table => null;

        public IStoreStoredProcedure? StoreStoredProcedure => null;

        public string TableName { get; }

        public string? Schema { get; }

        public IReadOnlyList<IColumnModification> ColumnModifications { get; }

        public IReadOnlyList<IUpdateEntry> Entries => [];

        public EntityState EntityState => EntityState.Added;

        public IColumnBase? RowsAffectedColumn => null;

        public void PropagateResults(
            RelationalDataReader relationalReader
        ) => throw new NotSupportedException();

        public void PropagateOutputParameters(
            DbParameterCollection parameterCollection,
            int baseParameterIndex
        ) => throw new NotSupportedException();
    }

    private sealed class IdentityEntity
    {
        public int Id { get; set; }

        public int ExplicitId { get; set; }

        public int Computed { get; set; }
    }
}
