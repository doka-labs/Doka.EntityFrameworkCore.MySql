using System.Linq.Expressions;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.Internal;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;

namespace Doka.EntityFrameworkCore.MySql.Benchmarks;

/// <summary>
/// Measures complete query rewriting, SQL generation, and uncached compilation
/// with the EF-owned collection shapes prepared outside the measured operation.
/// </summary>
[MemoryDiagnoser]
public partial class QueryEnumerationBenchmark : IDisposable
{
    private QueryContext _context = null!;
    private ISqlExpressionFactory _sql = null!;
    private IRelationalTypeMappingSource _mappings = null!;
    private Func<object> _operation = null!;
    private Expression _input = null!;
    private string? _compiledQuerySql;

    /// <summary>Gets or sets the independently measured query pipeline.</summary>
    [Params(
        "json-static-single", "json-static-nested", "json-dynamic-nested", "json-table-50",
        "json-decode-50", "limit-two", "limit-nested", "apply-50", "apply-delete-50",
        "dynamic-offset", "compile-materialized", "compile-string-aggregate", "compile-json-document",
        "compile-includes-union", "compile-member-init-lineage", "compile-materialized-include",
        "guard-union-all-overlap", "guard-union-all-disjoint")]
    public string Scenario { get; set; } = "json-static-single";

    /// <summary>Creates real provider services, expression trees, and stable output controls.</summary>
    [GlobalSetup]
    public void GlobalSetup()
    {
        var options = new DbContextOptionsBuilder<QueryContext>()
            .UseMySql(
                "Server=localhost;Database=enumeration_query;User ID=unused;",
                MySqlServerVersion.MariaDb(new Version(11, 8, 0)))
            .Options;

        _context = new QueryContext(options);
        _ = _context.Model;
        _sql = _context.GetService<ISqlExpressionFactory>();
        _mappings = _context.GetService<IRelationalTypeMappingSource>();
        _operation = CreateOperation();

        // WHY: Identity and output diagnostics are setup work, not per-invocation
        // allocations. The fingerprint compares whole outputs across provider builds.
        var result = _operation();
        var output = _compiledQuerySql ?? (result is Expression expression ? expression.Print() : result.ToString()!);
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(output)));
        Console.WriteLine($"QueryEnumeration {Scenario}: input={_input.GetType().FullName}; output-sha256={digest}");

        if (_input is SelectExpression select)
        {
            Console.WriteLine($"tables={select.Tables.GetType().FullName}; "
                + $"projections={select.Projection.GetType().FullName}");
        }
    }

    /// <summary>Executes the selected complete production method, guard, or EF compilation pipeline.</summary>
    /// <returns>The command text, expression, compiled query, or boxed guard result.</returns>
    [Benchmark]
    public object ExecuteCompletePipeline() => _operation();

    /// <summary>Releases the offline provider context.</summary>
    [GlobalCleanup]
    public void Dispose()
    {
        _context?.Dispose();
        GC.SuppressFinalize(this);
    }

    private Func<object> CreateOperation()
    {
        switch (Scenario)
        {
            case "json-static-single":
            case "json-static-nested":
            case "json-dynamic-nested":
            case "json-table-50":
                _input = Scenario == "json-table-50" ? CreateJsonTable() : CreateJsonScalar();
                return () => _context.GetService<IQuerySqlGeneratorFactory>().Create().GetCommand(_input).CommandText;

            case "json-decode-50":
                _input = CreateJsonTable();
                return () => MySqlJsonTableDocumentDecodingExpressionVisitor.Rewrite(_input, _sql);

            case "limit-two":
            case "limit-nested":
                return CreateLimitOperation();

            case "apply-50":
            case "apply-delete-50":
                _input = CreateApply();
                return () => new MySqlApplyRewritingExpressionVisitor(_sql, flattenJsonTablesOnly: false).Visit(_input);

            case "dynamic-offset":
                _input = CreateDynamicOffset();
                return () => new MySqlDynamicOffsetRewritingExpressionVisitor(
                    _sql, _mappings, new SqlAliasManager()).Visit(_input);

            case "compile-materialized":
                return Compile(_context.Rows
                    .Select(row => new SourceProjection { Id = row.Id, Name = row.Name })
                    .WithMaterializedSource(shared => shared.Join(shared, left => left.Id, right => right.Id,
                        (left, right) => left.Name)));

            case "compile-string-aggregate":
                return Compile(_context.Rows.GroupBy(row => row.GroupId)
                    .Select(group => string.Join("|", group.OrderBy(row => row.Name)
                        .Select(row => string.Join("/", new[] { row.Name, row.Name, row.Name })))));

            case "compile-json-document":
                return Compile(_context.Rows.SelectMany(row => row.Document.Items).Select(item => item.Token));

            case "compile-includes-union":
                return Compile(_context.PlainRows.Where(row => row.Id < 10)
                    .Union(_context.PlainRows.Where(row => row.Id > 20)).Include(row => row.Children));

            case "compile-member-init-lineage":
                return CreateMemberInitLineageOperation();

            case "compile-materialized-include":
                return CreateMaterializedIncludeOperation();

            case "guard-union-all-overlap":
            case "guard-union-all-disjoint":
                return CreateUnionAllGuardOperation();

            default:
                throw new ArgumentOutOfRangeException(nameof(Scenario));
        }
    }

    /// <summary>Runs EF's uncached extraction and compilation boundary for a prepared LINQ query.</summary>
    private Func<object> Compile<T>(IQueryable<T> query)
    {
        _input = query.Expression;
        _compiledQuerySql = query.ToQueryString();

        // WHY: CreateCompiledQuery performs EF's captured-value/query-root extraction
        // before compiling without consulting the compiled-query cache. Calling the
        // compilation context directly skips that required captured-DbSet boundary.
        // Verified against EF Core 10.0.8 QueryCompiler.CreateCompiledQuery.
#pragma warning disable EF1001
        var compiler = _context.GetService<IQueryCompiler>();

        return () => compiler.CreateCompiledQuery<IEnumerable<T>>(_input);
#pragma warning restore EF1001
    }

    private Func<object> CreateLimitOperation()
    {
        var arguments = new List<SqlExpression>
        {
            new SqlParameterExpression("first", typeof(int), IntTypeMapping.Default),
            new SqlParameterExpression("second", typeof(int), IntTypeMapping.Default),
        };

        SqlExpression limit = Least(arguments);
        if (Scenario == "limit-nested")
        {
            // WHY: Each level wraps the preceding expression, so construction is intentionally sequential.
            // ReSharper disable once ForCanBeConvertedToForeach
            // ReSharper disable once LoopCanBeConvertedToQuery
            for (var depth = 0; depth < 8; depth++)
            {
                limit = Least([limit, _sql.Constant(100 - depth)]);
            }
        }

        _input = Select([new ProjectionExpression(_sql.Constant(1), "Value")], limit: limit);
        var parameters = new Dictionary<string, object?> { ["first"] = 30, ["second"] = 20 };
        // WHY: Production parameter processing uses this EF internal options
        // record; using the same boundary keeps this a complete-method probe.
#pragma warning disable EF1001
        var processor = _context.GetService<IRelationalParameterBasedSqlProcessorFactory>()
            .Create(new RelationalParameterBasedSqlProcessorParameters(
                false, ParameterTranslationMode.MultipleParameters));
#pragma warning restore EF1001

        Console.WriteLine($"limit-arguments={arguments.GetType().FullName}; "
            + $"expression-arguments={((SqlFunctionExpression)limit).Arguments!.GetType().FullName}");

        return () => processor.Process(_input, new ParametersCacheDecorator(parameters));
    }

    private SqlFunctionExpression Least(IReadOnlyList<SqlExpression> arguments)
        => (SqlFunctionExpression)_sql.Function("LEAST", arguments, nullable: false,
            argumentsPropagateNullability: arguments.Select(_ => false), typeof(int), IntTypeMapping.Default);
}
