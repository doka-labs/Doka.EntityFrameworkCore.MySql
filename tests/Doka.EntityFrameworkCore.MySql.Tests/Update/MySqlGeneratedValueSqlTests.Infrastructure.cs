using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.EntityFrameworkCore.Update;

namespace Doka.EntityFrameworkCore.MySql.Tests;

public sealed partial class MySqlGeneratedValueSqlTests
{
    private static ReadbackContext CreateContext(
        bool mariaDb = false
    ) => new(new DbContextOptionsBuilder<ReadbackContext>()
        .UseMySql(
            "Server=localhost;Database=doka;User ID=root;Password=unused;",
            mariaDb
                ? MySqlServerVersion.MariaDb(new Version(11, 8, 0))
                : MySqlServerVersion.MySql(new Version(8, 4, 0)))
        .Options);

    private static IReadOnlyModificationCommand[] CreateCommands(
        ReadbackContext context,
        string tableName,
        int count,
        int firstKey = 1
    )
    {
        var table = context.Model.GetRelationalModel().Tables.Single(candidate => candidate.Name == tableName);
        var commands = new IReadOnlyModificationCommand[count];

        for (var row = 0; row < count; row++)
        {
            var modifications = new List<IColumnModification>();
            var key = firstKey + row;

            for (var index = 0; index < table.PrimaryKey!.Columns.Count; index++)
            {
                var column = table.PrimaryKey.Columns[index];
                var propertyType = column.PropertyMappings[0].Property.ClrType;
                var value = propertyType == typeof(Guid)
                    ? new Guid(key, 0, 0, new byte[8])
                    : propertyType == typeof(string)
                        ? "key-" + key
                        : propertyType == typeof(ConvertedKey)
                            ? new ConvertedKey(key)
                            : Convert.ChangeType(key, propertyType, System.Globalization.CultureInfo.InvariantCulture);

                modifications.Add(CreateModification(column, value, $"p{row}_{index}", read: false, write: true));
            }

            modifications.Add(CreateModification(table.FindColumn("Payload")!, key, $"p{row}_payload", false, true));
            modifications.Add(CreateModification(
                table.FindColumn("Generated")!,
                null,
                $"p{row}_generated",
                true,
                false));
            commands[row] = new TestCommand(table, modifications);
        }

        return commands;
    }

    /// <summary>
    /// Uses EF's actual update entries and command factory for temporary/client-generated key controls.
    /// </summary>
    private static IReadOnlyModificationCommand[] CreateTrackedCommands(
        ReadbackContext context,
        string tableName,
        IUpdateEntry[] entries
    )
    {
        var table = context.Model.GetRelationalModel().Tables.Single(candidate => candidate.Name == tableName);
        var parameter = 0;

        return entries.Select(entry =>
        {
            var command = context.GetService<IModificationCommandFactory>().CreateModificationCommand(
                new ModificationCommandParameters(
                    table,
                    sensitiveLoggingEnabled: false,
                    detailedErrorsEnabled: false,
                    comparer: null,
                    generateParameterName: () => "tracked" + parameter++,
                    logger: context.GetService<IDiagnosticsLogger<DbLoggerCategory.Update>>()));

            command.AddEntry(entry, mainEntry: true);

            return (IReadOnlyModificationCommand)command;
        }).ToArray();
    }

    private static TestCommand UnsafeCommand(
        ReadbackContext context,
        IReadOnlyModificationCommand command,
        string failure
    )
    {
        var modifications = command.ColumnModifications.ToList();
        var table = command.Table!;
        var keyColumn = table.PrimaryKey!.Columns[0];

        switch (failure)
        {
            case "null-key":
                modifications[0] = CreateModification(keyColumn, null, "unsafe", false, true);
                break;
            case "missing-key":
                modifications.RemoveAt(0);
                break;
            case "read-key":
                modifications[0] = CreateModification(keyColumn, 2, "unsafe", true, true);
                break;
            case "alternate-key-only":
                modifications.RemoveAt(0);
                modifications[0] = CreateModification(
                    table.FindColumn("Payload")!,
                    2,
                    "unsafe",
                    false,
                    true,
                    key: true);
                break;
            case "different-table":
                return (TestCommand)CreateCommands(context, "StringRows", 1)[0];
            case "extra-read-column":
                modifications.Add(CreateModification(table.FindColumn("Payload")!, 2, "read", true, false));
                break;
            case "different-write-order":
                (modifications[0], modifications[1]) = (modifications[1], modifications[0]);
                break;
            case "no-physical-table":
                return new TestCommand(null, modifications);
            case "wrong-state":
                return new TestCommand(table, modifications, state: EntityState.Modified);
            case "wrong-table-name":
                return new TestCommand(table, modifications, tableName: "OtherRows");
            case "wrong-schema":
                return new TestCommand(table, modifications, schema: "other");
            case "foreign-column":
                var foreignTable = context.Model.GetRelationalModel().Tables
                    .Single(candidate => candidate.Name == "StringRows");

                modifications[^1] = CreateModification(
                    foreignTable.FindColumn("Generated")!,
                    null,
                    "foreign_generated",
                    true,
                    false);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(failure));
        }

        return new TestCommand(table, modifications);
    }

    private static ColumnModification CreateModification(
        IColumn column,
        object? value,
        string parameter,
        bool read,
        bool write,
        bool key = false
    )
    {
        var property = column.PropertyMappings[0].Property;
        var parameters = new ColumnModificationParameters(
            column,
            originalValue: null,
            value,
            property,
            property.GetRelationalTypeMapping(),
            read,
            write,
            key || column.Table.PrimaryKey!.Columns.Contains(column),
            condition: false,
            sensitiveLoggingEnabled: false,
            isNullable: property.IsNullable)
        {
            GenerateParameterName = () => parameter,
        };

        return new ColumnModification(parameters);
    }

    private sealed class ReadbackContext : DbContext
    {
        public ReadbackContext(
            DbContextOptions<ReadbackContext> options
        ) : base(options) { }

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            AddTable<int>(modelBuilder, "Rows").HasAlternateKey("Payload");
            AddTable<int>(modelBuilder, "ExplicitInnoDbRows").UseStorageEngine("InnoDB");
            AddTable<Guid>(modelBuilder, "GuidRows");
            AddTable<string>(modelBuilder, "StringRows").Property<string>("Id").UseCollation("utf8mb4_general_ci");
            AddTable<ConvertedKey>(modelBuilder, "ConvertedRows").Property<ConvertedKey>("Id")
                .HasConversion(new ValueConverter<ConvertedKey, int>(
                    key => key.Value,
                    value => new ConvertedKey(value)));

            AddTable<ConvertedKey>(modelBuilder, "ConvertedIdentityRows").Property<ConvertedKey>("Id")
                .HasConversion(new ValueConverter<ConvertedKey, int>(
                    key => key.Value,
                    value => new ConvertedKey(value)))
                .HasMySqlValueGenerationStrategy(MySqlValueGenerationStrategy.AutoIncrement);

            AddNumericIdentityTable<sbyte>(modelBuilder, "SByteIdentityRows");
            AddNumericIdentityTable<byte>(modelBuilder, "ByteIdentityRows");
            AddNumericIdentityTable<short>(modelBuilder, "Int16IdentityRows");
            AddNumericIdentityTable<ushort>(modelBuilder, "UInt16IdentityRows");
            AddNumericIdentityTable<int>(modelBuilder, "Int32IdentityRows");
            AddNumericIdentityTable<uint>(modelBuilder, "UInt32IdentityRows");
            AddNumericIdentityTable<long>(modelBuilder, "Int64IdentityRows");
            AddNumericIdentityTable<ulong>(modelBuilder, "UInt64IdentityRows");
            var composite = AddTable<int>(modelBuilder, "CompositeRows");
            composite.Property<int>("Tenant");
            composite.HasKey("Id", "Tenant");
            AddTable<int>(modelBuilder, "TriggeredRows")
                .ToTable("TriggeredRows", table => table.HasTrigger("ChangesKey"));
            if (!Database.IsMariaDb())
            {
                AddTable<int>(modelBuilder, "TemporalRows").ToTable("TemporalRows", table => table.IsTemporal());
            }

            AddTable<int>(modelBuilder, "NontransactionalRows").UseStorageEngine("MyISAM");
            AddSharedTable<TriggeredMapping>(
                modelBuilder,
                "SharedTriggeredRows",
                trigger: true,
                conflictingEngine: false);
            AddSharedTable<ConflictingEngineMapping>(
                modelBuilder,
                "SharedEngineRows",
                trigger: false,
                conflictingEngine: true);
            modelBuilder.Entity<GuidIdentityRow>(entity =>
            {
                entity.ToTable("GuidIdentityRows");
                entity.HasKey(row => row.Id);
                entity.Property(row => row.Generated).HasComputedColumnSql("`Payload` + 1", stored: true);
            });
            modelBuilder.Entity<ShadowRow>(entity =>
            {
                entity.ToTable("ShadowRows");
                entity.Property<int>("Id").ValueGeneratedNever();
                entity.HasKey("Id");
                entity.Property(row => row.Generated).HasComputedColumnSql("`Payload` + 1", stored: true);
            });
            modelBuilder.Entity<ZeroSentinelIdentityRow>(entity =>
            {
                entity.ToTable("ZeroSentinelIdentityRows");
                entity.HasKey(row => row.Id);
                entity.Property(row => row.Id).HasSentinel(-1);
                entity.Property(row => row.Generated).HasComputedColumnSql("`Payload` + 1", stored: true);
            });
            modelBuilder.Entity<IdentityRow>(entity =>
            {
                entity.ToTable("IdentityRows");
                entity.HasKey(row => row.Id);
                entity.Property(row => row.Generated).HasComputedColumnSql("`Payload` + 1", stored: true);
            });
        }

        private static void AddSharedTable<TMapping>(
            ModelBuilder modelBuilder,
            string name,
            bool trigger,
            bool conflictingEngine
        )
        {
            var principal = modelBuilder.Entity<SharedPrincipal<TMapping>>();
            principal.ToTable(name);
            principal.HasKey(row => row.Id);
            principal.Property(row => row.Id).ValueGeneratedNever();
            principal.Property(row => row.Generated).HasComputedColumnSql("`Payload` + 1", stored: true);
            principal.UseStorageEngine("InnoDB");
            var dependent = modelBuilder.Entity<SharedDependent<TMapping>>();
            dependent.ToTable(name, table =>
            {
                if (trigger)
                {
                    table.HasTrigger("SharedChangesKey");
                }
            });

            dependent.HasKey(row => row.Id);
            dependent.Property(row => row.Id).ValueGeneratedNever();
            dependent.HasOne<SharedPrincipal<TMapping>>().WithOne()
                .HasForeignKey<SharedDependent<TMapping>>(row => row.Id);

            if (conflictingEngine)
            {
                dependent.UseStorageEngine("MyISAM");
            }
        }

        private static EntityTypeBuilder<Dictionary<string, object>> AddTable<TKey>(
            ModelBuilder modelBuilder,
            string name
        )
        {
            var entity = modelBuilder.SharedTypeEntity<Dictionary<string, object>>(name);
            entity.ToTable(name);
            entity.Property<TKey>("Id").ValueGeneratedNever();
            entity.HasKey("Id");
            entity.Property<int>("Payload");
            entity.Property<int>("Generated").HasComputedColumnSql("`Payload` + 1", stored: true);

            return entity;
        }

        private static void AddNumericIdentityTable<TKey>(
            ModelBuilder modelBuilder,
            string name
        )
        {
            var entity = AddTable<TKey>(modelBuilder, name);

            // WHY: The convention intentionally does not select every signed/unsigned
            // integer CLR type, but explicit AUTO_INCREMENT still needs a stable-key guard.
            entity.Property<TKey>("Id")
                .HasMySqlValueGenerationStrategy(MySqlValueGenerationStrategy.AutoIncrement);
        }
    }

    private sealed class SharedPrincipal<TMapping>
    {
        public int Id { get; set; }

        public int Payload { get; set; }

        public int Generated { get; set; }
    }

    private sealed class SharedDependent<TMapping>
    {
        public int Id { get; set; }

        public int Detail { get; set; }
    }

    private sealed class TriggeredMapping { }

    private sealed class ConflictingEngineMapping { }

    private sealed class GuidIdentityRow
    {
        public Guid Id { get; set; }

        public int Payload { get; set; }

        public int Generated { get; set; }
    }

    private sealed class ShadowRow
    {
        public int Payload { get; set; }

        public int Generated { get; set; }
    }

    private sealed class ZeroSentinelIdentityRow
    {
        public int Id { get; set; }

        public int Payload { get; set; }

        public int Generated { get; set; }
    }

    private sealed class IdentityRow
    {
        public int Id { get; set; }

        public int Payload { get; set; }

        public int Generated { get; set; }
    }

    private readonly record struct ConvertedKey(int Value);

    private sealed class TestCommand : IReadOnlyModificationCommand
    {
        public TestCommand(
            ITable? table,
            IReadOnlyList<IColumnModification> modifications,
            EntityState state = EntityState.Added,
            string? tableName = null,
            string? schema = null
        )
        {
            Table = table;
            ColumnModifications = modifications;
            EntityState = state;
            TableName = tableName ?? table?.Name ?? "Rows";
            Schema = schema ?? table?.Schema;
        }

        public ITable? Table { get; }

        public IStoreStoredProcedure? StoreStoredProcedure => null;

        public string TableName { get; }

        public string? Schema { get; }

        public IReadOnlyList<IColumnModification> ColumnModifications { get; }

        public IReadOnlyList<IUpdateEntry> Entries => [];

        public EntityState EntityState { get; }

        public IColumnBase? RowsAffectedColumn => null;

        public void PropagateResults(
            RelationalDataReader relationalReader
        ) => throw new NotSupportedException();

        public void PropagateOutputParameters(
            DbParameterCollection parameterCollection,
            int baseParameterIndex
        ) => throw new NotSupportedException();
    }
}
