namespace Doka.EntityFrameworkCore.MySql.Benchmarks;

/// <summary>
/// Measures complete scoped-command validation and allocation-free enumeration controls.
/// </summary>
/// <remarks>
/// Inputs are prepared before measurement. These controls distinguish interface
/// enumeration candidates from string and JSON DOM loops that already use no
/// heap-allocated enumerator.
/// </remarks>
[MemoryDiagnoser]
public class EnumerationControlBenchmark : IDisposable
{
    private readonly string[] _setup = ["SET @doka_probe = 1;"];
    private readonly string[] _cleanup = ["SET @doka_probe = NULL;"];
    private readonly string[] _wideSetup = Enumerable.Repeat("SET @doka_probe = 1;", 32).ToArray();
    private readonly string[] _wideCleanup = Enumerable.Repeat("SET @doka_probe = NULL;", 32).ToArray();
    private readonly JsonDocument _document = JsonDocument.Parse(
        "{\"items\":[{\"id\":1,\"name\":\"one\"},{\"id\":2,\"name\":\"two\"}],\"active\":true}");

    /// <summary>Validates and constructs one minimal setup/body/cleanup scope.</summary>
    [Benchmark]
    public MySqlMigrationCommandSpec CreateSmallScope() => MySqlMigrationCommandSpec.CreateScoped(
        _setup,
        "SELECT @doka_probe;",
        _cleanup);

    /// <summary>Validates and constructs a scope with many setup and cleanup fragments.</summary>
    [Benchmark]
    public MySqlMigrationCommandSpec CreateWideScope() => MySqlMigrationCommandSpec.CreateScoped(
        _wideSetup,
        "SELECT @doka_probe;",
        _wideCleanup);

    /// <summary>Exercises the complete SQL string escaping method as an unchanged control.</summary>
    [Benchmark]
    public string EscapeSqlLiteral() => MySqlSqlLiteralGenerator.Generate("Hallo 'Test' \\ \"quoted\"\n");

    /// <summary>Hashes the complete JSON document through the provider's structural comparer.</summary>
    [Benchmark]
    public int HashJsonDocument() => MySqlJsonValueComparers.JsonElementComparer.GetHashCode(_document.RootElement);

    /// <summary>Releases the document owned by this benchmark fixture.</summary>
    [GlobalCleanup]
    public void Dispose()
    {
        _document.Dispose();
        GC.SuppressFinalize(this);
    }
}
