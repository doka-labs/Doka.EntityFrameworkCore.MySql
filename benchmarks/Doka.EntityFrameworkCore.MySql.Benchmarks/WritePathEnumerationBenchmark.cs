using System.Reflection;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore.Update;

namespace Doka.EntityFrameworkCore.MySql.Benchmarks;

/// <summary>
/// Measures complete write-path operations using commands and column collections
/// created by EF Core's tracked update pipeline rather than array-backed stubs.
/// </summary>
[MemoryDiagnoser]
public partial class WritePathEnumerationBenchmark : IDisposable
{
    private WriteContext _context = null!;
    private MySqlUpdateSqlGenerator _generator = null!;
    private IModificationCommandBatchFactory _batchFactory = null!;
    private List<IReadOnlyModificationCommand> _commands = null!;
    private IReadOnlyModificationCommand _firstShape = null!;
    private IReadOnlyModificationCommand _matching = null!;
    private IReadOnlyModificationCommand _earlyMismatch = null!;
    private IReadOnlyModificationCommand _lateMismatch = null!;
    private StringBuilder _sql = null!;

    /// <summary>Gets or sets the engine, physical column count, and command population.</summary>
    [ParamsSource(nameof(Scenarios))]
    public WriteScenario Scenario { get; set; } = null!;

    /// <summary>Gets the meaningful write-mode, width, and batch-size combinations.</summary>
    public static IEnumerable<WriteScenario> Scenarios
    {
        get
        {
            foreach (var mode in Enum.GetValues<WriteMode>())
            {
                foreach (var columns in new[] { 3, 50 })
                {
                    foreach (var rows in new[] { 1, 100, 1000 })
                    {
                        yield return new WriteScenario(mode, columns, rows);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Builds tracked commands and warms lazy EF metadata, parameter names, table
    /// eligibility, and SQL buffers outside the measured operations.
    /// </summary>
    [GlobalSetup]
    public void GlobalSetup()
    {
        ArgumentNullException.ThrowIfNull(Scenario);

        _context = CreateContext(Scenario.Mode == WriteMode.MariaDbReturning);
        _generator = (MySqlUpdateSqlGenerator)_context.GetService<IUpdateSqlGenerator>();
        _batchFactory = _context.GetService<IModificationCommandBatchFactory>();
        _commands = CreateCommands(_context, Scenario);
        var shapeCommands = CreateShapeCommands(_context, Scenario.ColumnCount);
        _firstShape = shapeCommands[0];
        _matching = shapeCommands[1];
        _earlyMismatch = shapeCommands[2];
        _lateMismatch = shapeCommands[3];
        _sql = new StringBuilder(capacity: 1024);

        // WHY: Both revisions must measure steady-state provider work, not the
        // first population of EF's command columns or Doka's weak metadata cache.
        foreach (var command in _commands)
        {
            WarmCommand(command);
        }

        foreach (var command in shapeCommands)
        {
            WarmCommand(command);
        }

        // WHY: A successful SQL call is not evidence that its named protocol ran.
        // Reject fixture drift before timing a write-only path as generated readback.
        var expectedReadColumns = Scenario.Mode == WriteMode.MySqlWriteOnly ? 0 : 1;
        if (_commands[0].ColumnModifications.Count(modification => modification.IsRead) != expectedReadColumns
            || CompareMatchingShapes() != Scenario.RowCount
            || CompareEarlyMismatches() != 0
            || CompareLateMismatches() != 0)
        {
            throw new InvalidOperationException($"The tracked command contract does not match {Scenario}.");
        }

        _ = GenerateSql();
        _ = CalculateReadbackCost();
        _ = AssembleBatches();

        var batch = _batchFactory.Create();
        Console.WriteLine(
            $"Write collections ({Scenario}): commands={_commands.GetType().FullName}; "
            + $"columns={_commands[0].ColumnModifications.GetType().FullName}; "
            + $"keys={_commands[0].Table!.PrimaryKey!.Columns.GetType().FullName}; "
            + $"batch={batch.ModificationCommands.GetType().FullName}");
        Console.WriteLine($"Write fingerprint ({Scenario}): {CaptureFingerprint()}");
    }

    /// <summary>Generates complete INSERT/readback SQL for the prepared population.</summary>
    /// <returns>A checksum of SQL length, result mapping, and transaction requirement.</returns>
    [Benchmark]
    public long GenerateSql()
    {
        _sql.Clear();
        var mapping = _generator.AppendBulkInsertOperation(_sql, _commands, 0, out var transaction);

        return _sql.Length + ((long)mapping << 32) + (transaction ? 1L << 48 : 0);
    }

    /// <summary>Calculates the complete packet-readback cost of every prepared command.</summary>
    /// <returns>A checksum of eligible commands and additional packet bytes.</returns>
    [Benchmark]
    public long CalculateReadbackCost()
    {
        long total = 0;

        // WHY: Keep the benchmark driver allocation-free so measurements reflect complete provider costing.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < _commands.Count; index++)
        {
            total += _generator.TryGetKeyedReadbackCost(_commands[index], out var bytes) ? bytes + 1 : 0;
        }

        return total;
    }

    /// <summary>
    /// Executes complete batch admission and finalization, including packet-budget
    /// rollover for populations wider than one physical batch permits.
    /// </summary>
    /// <returns>A checksum of finalized batches, command counts, and transaction flags.</returns>
    [Benchmark]
    public long AssembleBatches() => AssembleBatches(evidence: null);

    /// <summary>Checks matching tracked command shapes through the complete provider predicate.</summary>
    /// <returns>The number of accepted matching pairs.</returns>
    [Benchmark]
    public int CompareMatchingShapes() => CompareShapes(_matching);

    /// <summary>Checks an early defaulted-column mismatch through the complete provider predicate.</summary>
    /// <returns>The number of accepted mismatching pairs, which must remain zero.</returns>
    [Benchmark]
    public int CompareEarlyMismatches() => CompareShapes(_earlyMismatch);

    /// <summary>Checks a late defaulted-column mismatch through the complete provider predicate.</summary>
    /// <returns>The number of accepted mismatching pairs, which must remain zero.</returns>
    [Benchmark]
    public int CompareLateMismatches() => CompareShapes(_lateMismatch);

    /// <summary>
    /// Captures a stable hash of complete generated SQL, result contracts, costs,
    /// shape outcomes, and completed-batch SQL for before/after comparison.
    /// </summary>
    /// <returns>A SHA-256 fingerprint; this diagnostic is not a measured operation.</returns>
    public string CaptureFingerprint()
    {
        _sql.Clear();
        var mapping = _generator.AppendBulkInsertOperation(_sql, _commands, 0, out var transaction);
        var evidence = new StringBuilder();
        evidence.Append(Scenario).Append('|').Append((int)mapping).Append('|').Append(transaction)
            .Append('|').Append(CalculateReadbackCost()).Append('|').Append(_sql);
        evidence.Append('|').Append(CompareMatchingShapes()).Append('|').Append(CompareEarlyMismatches())
            .Append('|').Append(CompareLateMismatches());
        _ = AssembleBatches(evidence);

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(evidence.ToString())));
    }

    /// <summary>Releases the context and its tracked command populations.</summary>
    [GlobalCleanup]
    public void Dispose()
    {
        _context?.Dispose();
        GC.SuppressFinalize(this);
    }

    private int CompareShapes(
        IReadOnlyModificationCommand candidate
    )
    {
        var accepted = 0;

        // WHY: Repeat the shape check exactly RowCount times without measuring an extra range enumerator.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < Scenario.RowCount; index++)
        {
            accepted += MySqlModificationCommandBatch.CanBeInsertedInSameStatement(_firstShape, candidate) ? 1 : 0;
        }

        return accepted;
    }

    private long AssembleBatches(
        StringBuilder? evidence
    )
    {
        var batch = _batchFactory.Create();
        long checksum = 0;
        var index = 0;

        while (index < _commands.Count)
        {
            if (batch.TryAddCommand(_commands[index]))
            {
                index++;
                continue;
            }

            if (batch.ModificationCommands.Count == 0)
            {
                throw new InvalidOperationException("The benchmark's first command must fit an empty batch.");
            }

            CompleteBatch(moreBatchesExpected: true);
            batch = _batchFactory.Create();
        }

        CompleteBatch(moreBatchesExpected: false);

        return checksum;

        void CompleteBatch(
            bool moreBatchesExpected
        )
        {
            batch.Complete(moreBatchesExpected);
            checksum += batch.ModificationCommands.Count + 1 + (batch.RequiresTransaction ? 1 : 0);

            if (evidence is not null)
            {
                // WHY: StoreCommand is protected EF infrastructure. Reflection is
                // confined to untimed evidence capture, not batch measurements.
                var command = (RawSqlCommand)typeof(ReaderModificationCommandBatch)
                    .GetProperty("StoreCommand", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
                    .GetValue(batch)!;

                evidence.Append('|').Append(batch.ModificationCommands.Count).Append('|')
                    .Append(batch.RequiresTransaction).Append('|').Append(command.RelationalCommand.CommandText);
            }
        }
    }
}
