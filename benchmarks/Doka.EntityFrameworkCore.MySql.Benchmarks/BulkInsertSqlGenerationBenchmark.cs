using System.Data.Common;
using Microsoft.EntityFrameworkCore.Update;

namespace Doka.EntityFrameworkCore.MySql.Benchmarks;

/// <summary>
/// Isolates provider-owned multi-row SQL generation and batch-shape comparison
/// from change tracking, connector, network, and server work.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(launchCount: 1, warmupCount: 3, iterationCount: 5)]
public class BulkInsertSqlGenerationBenchmark : IDisposable
{
    private const string ConnectionString =
        "Server=localhost;Database=benchmark_bulk_sql;User ID=root;Password=benchmark;";

    private DbContext _context = null!;
    private MySqlUpdateSqlGenerator _generator = null!;
    private IReadOnlyModificationCommand[] _oneCommand = null!;
    private IReadOnlyModificationCommand[] _oneHundredCommands = null!;
    private IReadOnlyModificationCommand[] _oneThousandCommands = null!;
    private IReadOnlyModificationCommand _firstShape = null!;
    private IReadOnlyModificationCommand _secondShape = null!;
    private StringBuilder _builder = null!;

    [GlobalSetup]
    public void GlobalSetup()
    {
        var options = new DbContextOptionsBuilder<DbContext>().UseMySql(
                ConnectionString,
                MySqlServerVersion.MySql(new Version(8, 4, 0)))
            .Options;

        _context = new DbContext(options);
        _generator = (MySqlUpdateSqlGenerator)_context.GetService<IUpdateSqlGenerator>();
        _oneCommand = CreateCommands(1);
        _oneHundredCommands = CreateCommands(100);
        _oneThousandCommands = CreateCommands(1000);
        _firstShape = CreateShapeCommand();
        _secondShape = CreateShapeCommand();
        _builder = new StringBuilder(capacity: 8192);
        _ = Generate(_oneThousandCommands);
    }

    [GlobalCleanup]
    public void GlobalCleanup() => Dispose();

    public void Dispose()
    {
        _context?.Dispose();
        GC.SuppressFinalize(this);
    }

    [Benchmark]
    public int GenerateWriteOnly1Row() => Generate(_oneCommand);

    [Benchmark]
    public int GenerateWriteOnly100Rows() => Generate(_oneHundredCommands);

    [Benchmark]
    public int GenerateWriteOnly1000Rows() => Generate(_oneThousandCommands);

    [Benchmark]
    public int GenerateWriteOnly1000RowsWithPerRowFiltering()
    {
        var checksum = 0;
        for (var index = 0; index < _oneThousandCommands.Length; index++)
        {
            checksum += _oneThousandCommands[index]
                .ColumnModifications
                .Where(operation => operation.IsWrite)
                .ToList()
                .Count;
        }

        return Generate(_oneThousandCommands) + checksum;
    }

    [Benchmark]
    public bool CompareSameShape1000Times()
    {
        var result = true;
        for (var index = 0; index < 1000; index++)
        {
            result &= MySqlModificationCommandBatch.CanBeInsertedInSameStatement(_firstShape, _secondShape);
        }

        return result;
    }

    [Benchmark]
    public bool CompareSameShape1000TimesWithLinq()
    {
        var result = true;
        for (var index = 0; index < 1000; index++)
        {
            result &= HaveSameShapeWithLinq(_firstShape, _secondShape);
        }

        return result;
    }

    private static bool HaveSameShapeWithLinq(
        IReadOnlyModificationCommand first,
        IReadOnlyModificationCommand second
    ) => first
            .ColumnModifications
            .Where(operation => operation.IsWrite)
            .Select(operation => operation.ColumnName)
            .SequenceEqual(
                second
                    .ColumnModifications
                    .Where(operation => operation.IsWrite)
                    .Select(operation => operation.ColumnName),
                StringComparer.Ordinal)
        && first
            .ColumnModifications
            .Where(operation => operation.IsRead)
            .Select(operation => operation.ColumnName)
            .SequenceEqual(
                second
                    .ColumnModifications
                    .Where(operation => operation.IsRead)
                    .Select(operation => operation.ColumnName),
                StringComparer.Ordinal);

    private int Generate(
        IReadOnlyList<IReadOnlyModificationCommand> commands
    )
    {
        _builder.Clear();
        _generator.AppendBulkInsertOperation(_builder, commands, commandPosition: 0, out _);

        return _builder.Length;
    }

    private static IReadOnlyModificationCommand[] CreateCommands(
        int count
    )
    {
        var commands = new IReadOnlyModificationCommand[count];
        for (var index = 0; index < commands.Length; index++)
        {
            commands[index] = new BenchmarkModificationCommand(
            [
                CreateColumn("WriteA", read: false, write: true, $"p{index}_a"),
                CreateColumn("WriteB", read: false, write: true, $"p{index}_b"),
            ]);
        }

        return commands;
    }

    private static BenchmarkModificationCommand CreateShapeCommand() => new(
    [
        CreateColumn("WriteA", read: false, write: true),
        CreateColumn("Ignored", read: false, write: false),
        CreateColumn("ReadA", read: true, write: false),
        CreateColumn("WriteB", read: false, write: true),
        CreateColumn("ReadB", read: true, write: false),
    ]);

    private static ColumnModification CreateColumn(
        string name,
        bool read,
        bool write,
        string? parameterName = null
    )
    {
        var parameters = new ColumnModificationParameters(
            name,
            originalValue: null,
            value: 1,
            property: null,
            columnType: "int",
            typeMapping: IntTypeMapping.Default,
            read,
            write,
            key: false,
            condition: false,
            sensitiveLoggingEnabled: false,
            isNullable: false)
        {
            GenerateParameterName = parameterName is null ? null : () => parameterName,
        };

        return new ColumnModification(parameters);
    }

    private sealed class BenchmarkModificationCommand : IReadOnlyModificationCommand
    {
        public BenchmarkModificationCommand(
            IReadOnlyList<IColumnModification> columnModifications
        )
        {
            ColumnModifications = columnModifications;
        }

        public ITable? Table => null;

        public IStoreStoredProcedure? StoreStoredProcedure => null;

        public string TableName => "BenchmarkRows";

        public string? Schema => null;

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
}
