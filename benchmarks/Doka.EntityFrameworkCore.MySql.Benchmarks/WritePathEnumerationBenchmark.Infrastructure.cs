using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Update;

namespace Doka.EntityFrameworkCore.MySql.Benchmarks;

public partial class WritePathEnumerationBenchmark
{
    /// <summary>Identifies a real provider write/readback protocol.</summary>
    public enum WriteMode
    {
        /// <summary>Known keys and no server-generated values.</summary>
        MySqlWriteOnly,
        /// <summary>Known keys with physical-key-correlated generated-value readback.</summary>
        MySqlKeyedReadback,
        /// <summary>A modeled trigger requires the safe per-row generated-value fallback.</summary>
        MySqlTriggerFallback,
        /// <summary>MariaDB reads generated values through native RETURNING.</summary>
        MariaDbReturning,
    }

    /// <summary>Describes one meaningful full-method write population.</summary>
    /// <param name="Mode">The engine and generated-value protocol.</param>
    /// <param name="ColumnCount">The complete physical entity column count.</param>
    /// <param name="RowCount">The number of tracked commands.</param>
    public sealed record WriteScenario(
        WriteMode Mode,
        int ColumnCount,
        int RowCount
    )
    {
        /// <inheritdoc />
        public override string ToString() => $"{Mode}_{ColumnCount}cols_{RowCount}rows";
    }

    private static WriteContext CreateContext(
        bool mariaDb
    )
    {
        var options = new DbContextOptionsBuilder<WriteContext>().UseMySql(
                "Server=localhost;Database=benchmark_write_enumeration;User ID=root;Password=unused;",
                mariaDb
                    ? MySqlServerVersion.MariaDb(new Version(11, 8, 0))
                    : MySqlServerVersion.MySql(new Version(8, 4, 0)),
                mysql => mysql.MaxBatchSize(1000))
            .Options;

        return new WriteContext(options);
    }

    private static List<IReadOnlyModificationCommand> CreateCommands(
        WriteContext context,
        WriteScenario scenario
    )
    {
        var name = TableName(scenario.Mode, scenario.ColumnCount);
        var entities = context.Set<Dictionary<string, object>>(name);
        var metadata = context.Model.FindEntityType(name)!;
        var key = metadata.FindPrimaryKey()!;
        var table = context.Model.GetRelationalModel().FindTable(name, schema: null)!;
        var updateAdapter = context.GetService<IUpdateAdapterFactory>().Create();
        var factory = context.GetService<IModificationCommandFactory>();
        var commands = new List<IReadOnlyModificationCommand>(scenario.RowCount);

        // WHY: Each ordinal creates a distinct tracked key and its corresponding real modification command.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var row = 0; row < scenario.RowCount; row++)
        {
            var values = CreateValues(row + 1, scenario.ColumnCount);
            entities.Add(values);
            var entry = updateAdapter.TryGetEntry(key, [row + 1])!;
            var command = factory.CreateModificationCommand(new ModificationCommandParameters(
                table,
                sensitiveLoggingEnabled: false,
                detailedErrorsEnabled: false,
                comparer: null,
                generateParameterName: context.NextParameterName,
                logger: null));

            command.AddEntry(entry, mainEntry: true);
            commands.Add(command);
        }

        return commands;
    }

    private static IReadOnlyModificationCommand[] CreateShapeCommands(
        WriteContext context,
        int columnCount
    )
    {
        var name = "Shape" + columnCount;
        var entities = context.Set<Dictionary<string, object>>(name);
        var key = context.Model.FindEntityType(name)!.FindPrimaryKey()!;
        var table = context.Model.GetRelationalModel().FindTable(name, schema: null)!;
        var adapter = context.GetService<IUpdateAdapterFactory>().Create();
        var factory = context.GetService<IModificationCommandFactory>();
        var commands = new IReadOnlyModificationCommand[4];

        // WHY: The ordinal selects each shape-control case and writes directly to its fixed array slot.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < commands.Length; index++)
        {
            var values = CreateValues(index + 1, columnCount);
            if (index > 1)
            {
                // WHY: An unset generated default changes EF's actual write/read
                // shape; no fake or manually reordered modification list is used.
                values[index == 2 ? "Value00" : ValueName(columnCount - 3)] = 0;
            }

            entities.Add(values);
            var entry = adapter.TryGetEntry(key, [index + 1])!;
            var command = factory.CreateModificationCommand(new ModificationCommandParameters(
                table,
                sensitiveLoggingEnabled: false,
                detailedErrorsEnabled: false,
                comparer: null,
                generateParameterName: context.NextParameterName,
                logger: null));

            command.AddEntry(entry, mainEntry: true);
            commands[index] = command;
        }

        return commands;
    }

    private static Dictionary<string, object> CreateValues(
        int key,
        int columnCount
    )
    {
        var values = new Dictionary<string, object>(StringComparer.Ordinal) { ["Id"] = key, ["Generated"] = 0 };

        // WHY: Derive payload names and values from the exact requested non-key column count.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < columnCount - 2; index++)
        {
            values[ValueName(index)] = index + key;
        }

        return values;
    }

    private static void WarmCommand(
        IReadOnlyModificationCommand command
    )
    {
        var modifications = command.ColumnModifications;

        // WHY: Warm lazy parameter names without adding interface-enumerator work to fixture initialization.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < modifications.Count; index++)
        {
            _ = modifications[index].ParameterName;
            _ = modifications[index].OriginalParameterName;
        }
    }

    private static string TableName(
        WriteMode mode,
        int columnCount
    ) => mode + "Rows" + columnCount;

    private static string ValueName(
        int index
    ) => "Value" + index.ToString("D2", CultureInfo.InvariantCulture);

    private sealed class WriteContext : DbContext
    {
        private int _parameter;

        /// <summary>Creates the isolated tracked model used by the write measurements.</summary>
        public WriteContext(
            DbContextOptions<WriteContext> options
        ) : base(options) { }

        /// <summary>Allocates a unique command parameter name during untimed setup.</summary>
        public string NextParameterName() => "p" + _parameter++;

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            foreach (var columnCount in new[] { 3, 50 })
            {
                foreach (var mode in Enum.GetValues<WriteMode>())
                {
                    var entity = ConfigureTable(modelBuilder, TableName(mode, columnCount), columnCount);
                    if (mode != WriteMode.MySqlWriteOnly)
                    {
                        entity.Property<int>("Generated")
                            .HasDefaultValueSql("42")
                            .ValueGeneratedOnAdd()
                            .HasSentinel(0);
                    }

                    if (mode == WriteMode.MySqlTriggerFallback)
                    {
                        entity.ToTable(TableName(mode, columnCount), table => table.HasTrigger("ChangesKey"));
                    }
                }

                var shape = ConfigureTable(modelBuilder, "Shape" + columnCount, columnCount);

                // WHY: Configure every payload ordinal with the same default so unset values change real EF shapes.
                // ReSharper disable once ForCanBeConvertedToForeach
                // ReSharper disable once LoopCanBeConvertedToQuery
                for (var index = 0; index < columnCount - 2; index++)
                {
                    shape.Property<int>(ValueName(index))
                        .HasDefaultValue(0)
                        .ValueGeneratedOnAdd()
                        .HasSentinel(0);
                }
            }
        }

        private static EntityTypeBuilder<Dictionary<string, object>> ConfigureTable(
            ModelBuilder modelBuilder,
            string name,
            int columnCount
        )
        {
            var entity = modelBuilder.SharedTypeEntity<Dictionary<string, object>>(name);
            entity.ToTable(name);
            entity.IndexerProperty<int>("Id").ValueGeneratedNever();
            entity.HasKey("Id");
            entity.IndexerProperty<int>("Generated");

            // WHY: The requested width includes key and generated columns; add the remaining ordinal-named payloads.
            // ReSharper disable once ForCanBeConvertedToForeach
            // ReSharper disable once LoopCanBeConvertedToQuery
            for (var index = 0; index < columnCount - 2; index++)
            {
                entity.IndexerProperty<int>(ValueName(index));
            }

            return entity;
        }
    }
}
