using Microsoft.EntityFrameworkCore.Update;

namespace Doka.EntityFrameworkCore.MySql.Tests;

public sealed partial class MySqlUpdateSqlGeneratorTests
{
    /// <summary>
    /// Preserves the exact asymmetric modification pairs used to guard statement-shape matching.
    /// </summary>
    private static (TestModificationCommand First, TestModificationCommand Second) CreateBatchShapePair(
        string scenario
    )
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


        return scenario switch
        {
            "same-shape" => (baseline, sameShape),
            "reordered-write" => (baseline, reorderedWrite),
            "different-read" => (baseline, differentRead),
            "extra-write" => (baseline, extraWrite),
            "extra-write-reversed" => (extraWrite, baseline),
            "missing-read" => (baseline, missingRead),
            "missing-read-reversed" => (missingRead, baseline),
            "reordered-read" => (baseline, reorderedRead),
            "other-table" => (baseline, CreateCommand("OtherRows", schema: null)),
            "other-schema" => (baseline, CreateCommand("Rows", "other")),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
        };
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
