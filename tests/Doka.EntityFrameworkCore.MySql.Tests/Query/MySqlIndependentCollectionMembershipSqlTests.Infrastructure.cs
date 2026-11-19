namespace Doka.EntityFrameworkCore.MySql.Tests;

/// <summary>Builds real finalized query trees without opening a database connection.</summary>
public sealed partial class MySqlIndependentCollectionMembershipSqlTests : IDisposable
{
    private readonly ServiceProvider _services = new ServiceCollection()
        .AddEntityFrameworkDokaMySql()
        .BuildServiceProvider();

    /// <inheritdoc />
    public void Dispose()
    {
        _services.Dispose();
        GC.SuppressFinalize(this);
    }

    private static SelectExpression CreateMembership(
        MembershipContext context,
        string rootKind,
        bool nullPath,
        string decoder,
        string scenario,
        bool negated
    )
    {
        var sql = context.GetService<ISqlExpressionFactory>();
        var value = new ColumnExpression("value", "j", typeof(string), MySqlStringTypeMapping.Default, nullable: true);
        var ordinal = new ColumnExpression("ordinal", "j", typeof(int), IntTypeMapping.Default, nullable: false);
        var payload = decoder switch
        {
            "binary" or "char" => "[\"00000001-0001-0002-0304-05060708090a\"]",
            "enum" => "[\"QWN0aXZl\"]",
            "native" or "native-wrapper" => "[\"alpha\"]",
            _ => "[\"YWxwaGE=\"]",
        };

        var root = rootKind == "constant"
            ? sql.Constant(payload, MySqlStringTypeMapping.Default)
            : new SqlParameterExpression("values", typeof(string), MySqlStringTypeMapping.Default);

        IReadOnlyList<PathSegment>? path = nullPath
            ? null
            : [];

        if (scenario == "entity-root")
        {
            root = new ColumnExpression("Document",
                "e",
                typeof(string),
                MySqlStringTypeMapping.Default,
                nullable: false);
        }
        else if (scenario == "wrapped-root")
        {
            root = sql.Function(
                "JSON_EXTRACT",
                [root, sql.Constant("$")],
                true,
                [true, false],
                typeof(string),
                MySqlStringTypeMapping.Default);
        }
        else if (scenario == "static-path")
        {
            path = [new PathSegment("items")];
        }
        else if (scenario == "outer-index-path")
        {
            // WHY: An independent parameter can still depend on an outer row through its JSON path.
            path =
            [
                new PathSegment(
                    new ColumnExpression("Index", "e", typeof(int), IntTypeMapping.Default, nullable: false)),
            ];
        }

        var jsonTable = new MySqlJsonTableExpression(
            "j",
            root,
            path,
            [
                new MySqlJsonTableExpression.ColumnInfo("value", MySqlStringTypeMapping.Default, []),
                new MySqlJsonTableExpression.ColumnInfo("ordinal", IntTypeMapping.Default, ForOrdinality: true),
            ]);

        SqlExpression projection;
        SqlExpression item;
        if (decoder == "binary")
        {
            var replaced = sql.Function(
                "REPLACE",
                [value, sql.Constant("-"), sql.Constant(string.Empty)],
                true,
                [true, false, false],
                typeof(string),
                MySqlStringTypeMapping.Default);

            projection = sql.Function("UNHEX",
                [replaced],
                true,
                [true],
                typeof(Guid),
                MySqlGuidBinaryTypeMapping.Default);

            item = sql.Constant(Guid.Parse("00000001-0001-0002-0304-05060708090a"), MySqlGuidBinaryTypeMapping.Default);
        }
        else if (decoder == "char")
        {
            var mapping = context.Model
                .FindEntityType(typeof(MembershipRow))!
                .FindProperty(nameof(MembershipRow.TextId))!
                .GetRelationalTypeMapping();

            var quoted = sql.Function("JSON_QUOTE",
                [value],
                true,
                [true],
                typeof(string),
                MySqlStringTypeMapping.Default);

            projection = sql.Function("JSON_UNQUOTE", [quoted], true, [true], typeof(Guid), mapping);
            item = sql.Constant(Guid.Parse("00000001-0001-0002-0304-05060708090a"), mapping);
        }
        else if (decoder == "native")
        {
            projection = value;
            item = sql.Constant("alpha", MySqlStringTypeMapping.Default);
        }
        else if (decoder == "native-wrapper")
        {
            var quoted = sql.Function("JSON_QUOTE",
                [value],
                true,
                [true],
                typeof(string),
                MySqlStringTypeMapping.Default);

            projection = sql.Function("JSON_UNQUOTE",
                [quoted],
                true,
                [true],
                typeof(string),
                MySqlStringTypeMapping.Default);

            item = sql.Constant("alpha", MySqlStringTypeMapping.Default);
        }
        else
        {
            var mapping = decoder == "enum"
                ? context.Model
                    .FindEntityType(typeof(MembershipRow))!
                    .FindProperty(nameof(MembershipRow.Status))!
                    .GetRelationalTypeMapping()
                : MySqlStringTypeMapping.Default;

            var type = decoder == "enum"
                ? typeof(MembershipStatus)
                : typeof(string);

            projection = sql.Function(
                MySqlSentinelContract.GetName(MySqlSentinelKind.StringJsonDecode),
                [value],
                true,
                [true],
                type,
                mapping);

            item = decoder == "enum"
                ? sql.Constant(MembershipStatus.Active, mapping)
                : sql.Constant("alpha", mapping);
        }

        var predicate = scenario is "predicate" or "predicate-limit"
            ? sql.IsNotNull(value)
            : null;

        var ordering = scenario is "natural-order" or "reverse-order"
            ? new[] { new OrderingExpression(ordinal, scenario == "natural-order") }
            : [];

        var inner = CreateSelect(
            projection,
            [jsonTable],
            predicate,
            scenario == "distinct",
            ordering,
            scenario is "limit" or "predicate-limit"
                ? sql.Constant(2, IntTypeMapping.Default)
                : null,
            scenario == "offset"
                ? sql.Constant(1, IntTypeMapping.Default)
                : null,
            scenario == "group"
                ? [projection]
                : [],
            scenario == "having"
                ? sql.Constant(true,
                    context
                        .GetService<IRelationalTypeMappingSource>()
                        .FindMapping(typeof(bool)))
                : null);

        var membership = sql.In(item, inner);
        var result = negated
            ? sql.Not(membership)
            : membership;

        var table = context
            .Model
            .GetRelationalModel()
            .Tables
            .Single(table => table.Name == "MembershipRows");

        return CreateSelect(result, [new TableExpression("e", table)]);
    }

    private static string Generate(
        MembershipContext context,
        SelectExpression query
    ) => context
        .GetService<IQuerySqlGeneratorFactory>()
        .Create()
        .GetCommand(query)
        .CommandText;

    /// <summary>Uses the real finalized-tree boundary exercised by the provider generator.</summary>
    private static SelectExpression CreateSelect(
        SqlExpression projection,
        IReadOnlyList<TableExpressionBase> tables,
        SqlExpression? predicate = null,
        bool distinct = false,
        IReadOnlyList<OrderingExpression>? orderings = null,
        SqlExpression? limit = null,
        SqlExpression? offset = null,
        IReadOnlyList<SqlExpression>? groupBy = null,
        SqlExpression? having = null
    )
    {
        // WHY: The regression concerns finalized SQL-tree eligibility; LINQ cannot express every hidden-path boundary.
#pragma warning disable EF1001
        return new SelectExpression(
            alias: null,
            tables,
            predicate,
            groupBy ?? [],
            having,
            [new ProjectionExpression(projection, "Value")],
            distinct,
            orderings ?? [],
            offset,
            limit,
            new SqlAliasManager());
#pragma warning restore EF1001
    }

    private MembershipContext CreateContext(
        bool mariaDb
    ) => new(new DbContextOptionsBuilder<MembershipContext>()

        // WHY: Every theory row owns its EF graph; SQL-tree assertions do not test
        // global provider caching and must not depend on unrelated process history.
        .UseInternalServiceProvider(_services)
        .UseMySql(
            "Server=localhost;Database=independent_membership_unit;User ID=test;Password=unused;",
            mariaDb
                ? MySqlServerVersion.MariaDb(new Version(12, 3, 0))
                : MySqlServerVersion.MySql(new Version(8, 4, 0)))
        .Options);

    private sealed class MembershipContext : DbContext
    {
        /// <summary>Initializes the context with its supplied options and model settings.</summary>
        public MembershipContext(
            DbContextOptions<MembershipContext> options
        ) : base(options) { }

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder
                .Entity<MembershipRow>()
                .ToTable("MembershipRows");

            modelBuilder
                .Entity<MembershipRow>()
                .Property(row => row.Status)
                .HasConversion<string>();

            modelBuilder
                .Entity<MembershipRow>()
                .Property(row => row.TextId)
                .HasMySqlGuidFormat(MySqlGuidFormat.Char36);
        }
    }

    private sealed class MembershipRow
    {
        public int Id { get; set; }
        public int Index { get; set; }
        public Guid TextId { get; set; }
        public string Document { get; set; } = "[]";
        public MembershipStatus Status { get; set; }
    }

    private enum MembershipStatus
    {
        Active,
    }
}
