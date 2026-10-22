using System.Data;
using System.Data.Common;
using System.Linq.Expressions;
using System.Reflection;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore.Update;

namespace Doka.EntityFrameworkCore.MySql.Benchmarks;

/// <summary>
/// Measures complete stored-procedure generation and output propagation with
/// EF-created tracked commands, mapped parameters, and an unmapped output slot.
/// </summary>
[MemoryDiagnoser]
public class StoredProcedureEnumerationBenchmark : IDisposable
{
    private ProcedureContext _context = null!;
    private ProcedureContext _generatedContext = null!;
    private MySqlUpdateSqlGenerator _generator = null!;
    private MySqlModificationCommand _command = null!;
    private IColumnModification _mappedOutput = null!;
    private Func<IReadOnlyModificationCommand, RelationalDataReader, int?> _readRowsAffected = null!;
    private RelationalDataReader _reader = null!;
    private DataTable _table = null!;
    private DataTable _generatedTable = null!;
    private DbDataReader _generatedReader = null!;
    private Func<object> _inspectGeneratedReadback = null!;
    private StringBuilder _sql = null!;

    /// <summary>Gets or sets the complete entity column count.</summary>
    [Params(4, 50)]
    public int ColumnCount { get; set; }

    /// <summary>Gets or sets whether MariaDB or MySQL services construct the command.</summary>
    [Params(false, true)]
    public bool MariaDb { get; set; }

    /// <summary>Gets or sets whether the affected-row output requires a full parameter scan.</summary>
    [Params(false, true)]
    public bool RowsAffectedLast { get; set; }

    /// <summary>
    /// Creates real procedure metadata and tracked modifications, then positions
    /// a reusable BCL reader on one synthetic output row outside timed work.
    /// </summary>
    [GlobalSetup]
    public void GlobalSetup()
    {
        _context = CreateContext(MariaDb);

        var name = EntityName(ColumnCount, RowsAffectedLast);
        var values = new Dictionary<string, object>(StringComparer.Ordinal) { ["Id"] = 1 };

        // WHY: Generate the requested column population from ordinals rather than an intermediate range sequence.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < ColumnCount - 1; index++)
        {
            values[ValueName(index)] = index + 1;
        }

        var tracked = _context.Set<Dictionary<string, object>>(name).Attach(values);
        tracked.State = EntityState.Modified;
        var metadata = _context.Model.FindEntityType(name)!;
        var table = metadata.GetTableMappings().Single().Table;
        var procedure = metadata.GetUpdateStoredProcedureMappings().Single().StoreStoredProcedure;
        var entry = _context.GetService<IUpdateAdapterFactory>().Create().Entries.Single();
        _command = (MySqlModificationCommand)_context.GetService<IModificationCommandFactory>()
            .CreateModificationCommand(new ModificationCommandParameters(
                table,
                procedure,
                sensitiveLoggingEnabled: false,
                detailedErrorsEnabled: false,
                comparer: null,
                generateParameterName: _context.NextParameterName,
                logger: null));

        _command.AddEntry(entry, mainEntry: true);
        _generator = (MySqlUpdateSqlGenerator)_context.GetService<IUpdateSqlGenerator>();
        _sql = new StringBuilder(capacity: 16384);
        _table = CreateOutputTable(_command);
        var dataReader = _table.CreateDataReader();
        if (!dataReader.Read())
        {
            throw new InvalidOperationException("The stored-procedure benchmark requires one output row.");
        }

        // WHY: This provider method reads the current row without advancing it.
        // Reusing the reader isolates propagation from unrelated reader allocation.
        _reader = new RelationalDataReader();
        _reader.Initialize(
            _context.GetService<IRelationalConnection>(),
            new MySqlCommand(),
            dataReader,
            Guid.Empty,
            logger: null);
        _mappedOutput = _command.ColumnModifications
            .First(modification => modification is { IsRead: true, Property: not null });

        // WHY: A typed delegate reaches the complete private method without a
        // copied implementation, production test hook, or timed reflection call.
        _readRowsAffected = typeof(MySqlModificationCommandBatch)
            .GetMethod("GetRowsAffectedOutputParameter", BindingFlags.Static | BindingFlags.NonPublic)!
            .CreateDelegate<Func<IReadOnlyModificationCommand, RelationalDataReader, int?>>();

        PrepareGeneratedReadback();
        _ = GenerateSql();
        _ = PropagateOutputs();
        _ = ReadRowsAffected();
        _ = InspectGeneratedReadback();
        Console.WriteLine(
            $"Procedure collections ({MariaDb}, {ColumnCount}, affected-last={RowsAffectedLast}): "
            + $"columns={_command.ColumnModifications.GetType().FullName}; "
            + $"parameters={procedure.Parameters.GetType().FullName}");
        Console.WriteLine(
            $"Procedure fingerprint ({MariaDb}, {ColumnCount}, {RowsAffectedLast}): {CaptureFingerprint()}");
    }

    /// <summary>Generates the complete SET/CALL/output SELECT command sequence.</summary>
    /// <returns>A checksum of SQL length, result mapping, and transaction requirement.</returns>
    [Benchmark]
    public long GenerateSql()
    {
        _sql.Clear();
        var mapping = _generator.AppendStoredProcedureCall(_sql, _command, 0, out var transaction);

        return _sql.Length + ((long)mapping << 32) + (transaction ? 1L << 48 : 0);
    }

    /// <summary>Propagates all mapped OUT/INOUT values through the real provider command.</summary>
    /// <returns>A mapped output value, preventing the result from becoming unobserved.</returns>
    [Benchmark]
    public int PropagateOutputs()
    {
        _command.PropagateStoredProcedureOutputParameters(_reader);

        return (int)_mappedOutput.Value!;
    }

    /// <summary>Reads affected rows through the complete batch helper, including output-slot scanning.</summary>
    /// <returns>The expected affected-row count.</returns>
    [Benchmark]
    public int? ReadRowsAffected() => _readRowsAffected(_command, _reader);

    /// <summary>
    /// Validates the complete generated-value result schema against a real keyed
    /// command batch; the private struct result is boxed equally in both revisions.
    /// </summary>
    /// <returns>The opaque validated result, preventing dead-code elimination.</returns>
    [Benchmark]
    public object InspectGeneratedReadback() => _inspectGeneratedReadback();

    /// <summary>Hashes exact SQL, result contracts, and propagated values outside measured calls.</summary>
    /// <returns>A SHA-256 fingerprint for unchanged-driver baseline/candidate comparison.</returns>
    public string CaptureFingerprint()
    {
        _sql.Clear();
        var mapping = _generator.AppendStoredProcedureCall(_sql, _command, 0, out var transaction);
        var output = PropagateOutputs();
        var evidence = new StringBuilder();
        evidence.Append(MariaDb).Append('|').Append(ColumnCount).Append('|').Append(RowsAffectedLast)
            .Append('|').Append((int)mapping)
            .Append('|').Append(transaction).Append('|').Append(output).Append('|').Append(ReadRowsAffected())
            .Append('|').Append(_sql);

        foreach (var modification in _command.ColumnModifications)
        {
            evidence.Append('|').Append(modification.ColumnName).Append(':')
                .Append(modification.IsRead).Append(':').Append(modification.Value);
        }

        var generated = InspectGeneratedReadback();
        foreach (var field in generated.GetType()
                     .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                     .OrderBy(field => field.Name, StringComparer.Ordinal))
        {
            evidence.Append('|').Append(field.Name).Append(':').Append(field.GetValue(generated));
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(evidence.ToString())));
    }

    /// <summary>Releases the positioned reader, BCL output table, and context.</summary>
    [GlobalCleanup]
    public void Dispose()
    {
        _reader?.Dispose();
        _table?.Dispose();
        _generatedReader?.Dispose();
        _generatedTable?.Dispose();
        _generatedContext?.Dispose();
        _context?.Dispose();
        GC.SuppressFinalize(this);
    }

    private static DataTable CreateOutputTable(
        MySqlModificationCommand command
    )
    {
        var table = new DataTable("Outputs") { Locale = CultureInfo.InvariantCulture };
        var values = new List<object>();

        foreach (var modification in command.ColumnModifications)
        {
            _ = modification.ParameterName;
            _ = modification.OriginalParameterName;

            if (modification.Column is not IStoreStoredProcedureParameter
                {
                    Direction: ParameterDirection.Output or ParameterDirection.InputOutput,
                })
            {
                continue;
            }

            var ordinal = values.Count;
            table.Columns.Add("Output" + ordinal, typeof(int));
            values.Add(ReferenceEquals(modification.Column, command.RowsAffectedColumn) ? 1 : 2000 + ordinal);
        }

        table.Rows.Add(values.ToArray());

        return table;
    }

    private void PrepareGeneratedReadback()
    {
        // WHY: This positional readback contract belongs to MySQL's keyed
        // fallback, not MariaDB's distinct native RETURNING result protocol.
        _generatedContext = CreateContext(mariaDb: false);
        var name = "ReadbackRows" + ColumnCount;
        var metadata = _generatedContext.Model.FindEntityType(name)!;
        var table = metadata.GetTableMappings().Single().Table;
        var key = metadata.FindPrimaryKey()!;
        var adapter = _generatedContext.GetService<IUpdateAdapterFactory>().Create();
        var factory = _generatedContext.GetService<IModificationCommandFactory>();
        var batch = (MySqlModificationCommandBatch)_generatedContext
            .GetService<IModificationCommandBatchFactory>().Create();

        // WHY: Two distinct tracked keys are required to exercise complete generated-result correlation.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var row = 0; row < 2; row++)
        {
            var values = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["Id"] = row + 1,
                ["Generated"] = 0,
            };

            // WHY: Populate the exact requested width while deriving distinct payloads from row and column ordinals.
            // ReSharper disable once ForCanBeConvertedToForeach
            // ReSharper disable once LoopCanBeConvertedToQuery
            for (var index = 0; index < ColumnCount - 2; index++)
            {
                values[ValueName(index)] = row + index + 1;
            }

            _generatedContext.Set<Dictionary<string, object>>(name).Add(values);
            var entry = adapter.TryGetEntry(key, [row + 1])!;
            var command = factory.CreateModificationCommand(new ModificationCommandParameters(
                table,
                sensitiveLoggingEnabled: false,
                detailedErrorsEnabled: false,
                comparer: null,
                generateParameterName: _generatedContext.NextParameterName,
                logger: null));

            command.AddEntry(entry, mainEntry: true);
            if (!batch.TryAddCommand(command))
            {
                throw new InvalidOperationException("The generated-value benchmark batch must accept both commands.");
            }
        }

        batch.Complete(moreBatchesExpected: false);
        _generatedTable = new DataTable("Generated") { Locale = CultureInfo.InvariantCulture };
        _generatedTable.Columns.Add("Generated", typeof(int));
        _generatedTable.Columns.Add("Ordinal", typeof(int));
        _generatedReader = _generatedTable.CreateDataReader();
        var method = typeof(MySqlModificationCommandBatch)
            .GetMethod("CreateGeneratedValueReadback", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var call = Expression.Call(
            Expression.Constant(batch),
            method,
            Expression.Constant(0),
            Expression.Constant(_generatedReader, typeof(DbDataReader)));

        // WHY: The result type is a private provider struct. The one-time
        // delegate adds identical result-boxing overhead, not copied logic or
        // timed reflection. Evidence reports this overhead separately.
        _inspectGeneratedReadback = Expression.Lambda<Func<object>>(Expression.Convert(call, typeof(object))).Compile();
    }

    private static ProcedureContext CreateContext(
        bool mariaDb
    )
    {
        var options = new DbContextOptionsBuilder<ProcedureContext>().UseMySql(
                "Server=localhost;Database=benchmark_procedure_enumeration;"
                + "User ID=root;Password=unused;AllowUserVariables=true;",
                mariaDb
                    ? MySqlServerVersion.MariaDb(new Version(11, 8, 0))
                    : MySqlServerVersion.MySql(new Version(8, 4, 0)))
            .Options;

        return new ProcedureContext(options);
    }

    private static string EntityName(
        int columnCount,
        bool rowsAffectedLast
    ) => "ProcedureRows" + columnCount + (rowsAffectedLast ? "Last" : "First");

    private static string ValueName(
        int index
    ) => "Value" + index.ToString("D2", CultureInfo.InvariantCulture);

    private sealed class ProcedureContext : DbContext
    {
        private int _parameter;

        /// <summary>Creates the isolated mapped procedure model.</summary>
        public ProcedureContext(
            DbContextOptions<ProcedureContext> options
        ) : base(options) { }

        /// <summary>Allocates stable parameter names during untimed setup.</summary>
        public string NextParameterName() => "p" + _parameter++;

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            foreach (var columnCount in new[] { 4, 50 })
            {
                var readback = modelBuilder.SharedTypeEntity<Dictionary<string, object>>("ReadbackRows" + columnCount);
                readback.ToTable("ReadbackRows" + columnCount);
                readback.IndexerProperty<int>("Id").ValueGeneratedNever();
                readback.HasKey("Id");
                readback.IndexerProperty<int>("Generated").HasComputedColumnSql("42", stored: true);

                // WHY: Add exactly the non-key, non-generated columns required by the selected width.
                // ReSharper disable once ForCanBeConvertedToForeach
                // ReSharper disable once LoopCanBeConvertedToQuery
                for (var index = 0; index < columnCount - 2; index++)
                {
                    readback.IndexerProperty<int>(ValueName(index));
                }

                foreach (var rowsAffectedLast in new[] { false, true })
                {
                    ConfigureProcedure(modelBuilder, columnCount, rowsAffectedLast);
                }
            }
        }

        private static void ConfigureProcedure(
            ModelBuilder modelBuilder,
            int columnCount,
            bool rowsAffectedLast
        )
        {
            var name = EntityName(columnCount, rowsAffectedLast);
            var entity = modelBuilder.SharedTypeEntity<Dictionary<string, object>>(name);
            entity.ToTable(name);
            entity.IndexerProperty<int>("Id").ValueGeneratedNever();
            entity.HasKey("Id");

            // WHY: Ordinals select the repeating generation pattern used by the procedure descriptor fixture.
            // ReSharper disable once ForCanBeConvertedToForeach
            // ReSharper disable once LoopCanBeConvertedToQuery
            for (var index = 0; index < columnCount - 1; index++)
            {
                var property = entity.IndexerProperty<int>(ValueName(index));
                if (index % 3 != 0)
                {
                    property.ValueGeneratedOnAddOrUpdate();
                }

                if (index % 3 == 1)
                {
                    // WHY: An INOUT parameter writes the current value as well as
                    // reading a generated one; EF must not ignore that update input.
                    property.Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Save);
                }
            }

            // WHY: EF only allows a rows-affected output on UPDATE/DELETE,
            // so this real update mapping exercises that unread output slot.
            entity.UpdateUsingStoredProcedure(name + "_Update", procedure =>
            {
                // WHY: First exercises the short scan and skipped reader slot;
                // last exercises a full scan of the actual procedure parameters.
                if (!rowsAffectedLast)
                {
                    procedure.HasRowsAffectedParameter(parameter => parameter.HasName("RowsAffected"));
                }

                procedure.HasOriginalValueParameter("Id");

                // WHY: Reuse each property ordinal to configure matching IN, OUT and INOUT parameter positions.
                // ReSharper disable once ForCanBeConvertedToForeach
                // ReSharper disable once LoopCanBeConvertedToQuery
                for (var index = 0; index < columnCount - 1; index++)
                {
                    var property = ValueName(index);
                    switch (index % 3)
                    {
                        case 0:
                            procedure.HasParameter(property);
                            break;

                        case 1:
                            procedure.HasParameter(property, parameter => parameter.IsInputOutput());
                            break;

                        default:
                            procedure.HasParameter(property, parameter => parameter.IsOutput());
                            break;
                    }
                }

                if (rowsAffectedLast)
                {
                    procedure.HasRowsAffectedParameter(parameter => parameter.HasName("RowsAffected"));
                }
            });
        }
    }
}
