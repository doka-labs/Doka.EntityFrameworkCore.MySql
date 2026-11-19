namespace Doka.EntityFrameworkCore.MySql.Benchmarks;

/// <summary>Measures complete public direct writes with prepared inputs and native setup oracles, without performance gates.</summary>
[MemoryDiagnoser]
[InvocationCount(1, unrollFactor: 1)]
public partial class DirectWriteBenchmark : IAsyncDisposable
{
    private const string NarrowSourceTable = "DirectWriteNarrowSource";
    private const string NarrowTargetTable = "DirectWriteNarrowTarget";
    private const string WideSourceTable = "DirectWriteWideSource";
    private const string WideTargetTable = "DirectWriteWideTarget";
    private static readonly string[] s_narrowUpdates = [nameof(NarrowTarget.Value1)];
    private static readonly string[] s_wideUpdates = [nameof(WideTarget.C01)];
    private string _connectionString = string.Empty;
    private DirectWriteContext? _context;

    private string _database = string.Empty;
    private NarrowTarget[] _narrowInput = [];
    private IQueryable<NarrowSource>? _narrowSource;
    private MySqlConnection? _observer;
    private bool _ownsDatabase;
    private int _rowCount;
    private string _serverConnectionString = string.Empty;
    private ServiceProvider? _services;
    private WideTarget[] _wideInput = [];
    private IQueryable<WideSource>? _wideSource;
    private int _width;

    /// <summary>Gets or sets the independently measured row population and actual mapped width.</summary>
    [Params("1x3", "14000x50")]
    public string Shape { get; set; } = "1x3";

    private DirectWriteContext Context => _context
        ?? throw new InvalidOperationException("The direct-write benchmark context was not prepared.");

    private MySqlConnection Observer
        => _observer ?? throw new InvalidOperationException("The native setup observer was not prepared.");

    private string SourceTable => _width == 3
        ? NarrowSourceTable
        : WideSourceTable;

    private string TargetTable => _width == 3
        ? NarrowTargetTable
        : WideTargetTable;

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        try
        {
            await CleanupAsync()
                .ConfigureAwait(false);
        }
        finally
        {
            GC.SuppressFinalize(this);
        }
    }

    /// <summary>Prepares and independently verifies the complete native UPSERT path before measurement.</summary>
    [GlobalSetup(Target = nameof(UpsertRange))]
    public Task SetupUpsert()

        // WHY: BenchmarkDotNet lifecycle hooks do not supply a cancellation token.
        => SetupAsync(upsert: true, CancellationToken.None);

    /// <summary>Prepares and independently verifies the complete native INSERT SELECT path before measurement.</summary>
    [GlobalSetup(Target = nameof(InsertFromQuery))]
    public Task SetupInsertSelect()

        // WHY: BenchmarkDotNet lifecycle hooks do not supply a cancellation token.
        => SetupAsync(upsert: false, CancellationToken.None);

    /// <summary>Removes only the owned target rows outside the measured public operation.</summary>
    [IterationSetup]
    public void ResetTarget()
    {
        // WHY: BenchmarkDotNet provides a synchronous iteration hook, with one invocation
        // explicitly configured so a later call cannot accidentally measure an unchanged conflict.
        // ReSharper disable once MethodHasAsyncOverload
        ResetTargetAsync(CancellationToken.None)
            .GetAwaiter()
            .GetResult();
    }

    /// <summary>Releases the warm context, its pool and only this fixture's uniquely named database.</summary>
    [GlobalCleanup]
    public Task Cleanup() => DisposeAsync()
        .AsTask();

    /// <summary>Measures the complete public UPSERT method, retaining its native Int64 affected-count contract.</summary>
    /// <returns>The checked sum of native command counts, without truncation or insert/update classification.</returns>
    [Benchmark]
    public Task<long> UpsertRange()

        // WHY: Only caller inputs are prepared. Binding, bounded batching, native commands,
        // owned transaction/cleanup and public count aggregation remain measured.
        // BenchmarkDotNet does not supply a caller token to benchmark methods.
        => ExecuteUpsertAsync(CancellationToken.None);

    /// <summary>Measures the complete public typed INSERT SELECT method without source materialization.</summary>
    /// <returns>The native Int32 affected-row count returned by the public API.</returns>
    [Benchmark]
    public Task<int> InsertFromQuery()

        // WHY: Public setter/projection construction, parameterization and execution remain
        // measured. BenchmarkDotNet supplies no token; no managed source rows are prepared.
        => ExecuteInsertAsync(CancellationToken.None);

    private Task<long> ExecuteUpsertAsync(
        CancellationToken cancellationToken
    ) => _width == 3
        ? Context.ExecuteUpsertRangeAsync(_narrowInput, s_narrowUpdates, cancellationToken)
        : Context.ExecuteUpsertRangeAsync(_wideInput, s_wideUpdates, cancellationToken);

    private Task<int> ExecuteInsertAsync(
        CancellationToken cancellationToken
    ) => _width == 3
        ? (_narrowSource ?? throw new InvalidOperationException("The narrow source was not prepared."))
        .ExecuteInsertFromQueryAsync(Context.Set<NarrowTarget>(), SetNarrowColumns, cancellationToken)
        : (_wideSource ?? throw new InvalidOperationException("The wide source was not prepared."))
        .ExecuteInsertFromQueryAsync(Context.Set<WideTarget>(), SetWideColumns, cancellationToken);

    private async Task SetupAsync(
        bool upsert,
        CancellationToken cancellationToken
    )
    {
        (_rowCount, _width) = Shape switch
        {
            "1x3" => (1, 3),
            "14000x50" => (14_000, 50),
            _ => throw new InvalidOperationException("The direct-write benchmark requires the 1x3 or 14000x50 shape."),
        };

        try
        {
            await PrepareSchemaAsync(cancellationToken)
                .ConfigureAwait(false);

            if (upsert)
            {
                PrepareUpsertInput();
            }

            await ValidateOperationAsync(upsert, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception primaryFailure)
        {
            try
            {
                await CleanupAsync()
                    .ConfigureAwait(false);
            }
            catch (Exception cleanupFailure)
            {
                throw new AggregateException(primaryFailure, cleanupFailure);
            }

            throw;
        }
    }

    private void PrepareContext(
        DirectWriteContext context
    )
    {
        _context = context;
        _narrowSource = _width == 3
            ? context
                .Set<NarrowSource>()
                .Where(row => row.Id <= _rowCount)
            : null;

        _wideSource = _width == 50
            ? context
                .Set<WideSource>()
                .Where(row => row.Id <= _rowCount)
            : null;
    }
}
