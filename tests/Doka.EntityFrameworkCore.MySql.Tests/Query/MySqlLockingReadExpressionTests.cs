namespace Doka.EntityFrameworkCore.MySql.Tests;

/// <summary>Verifies locking intent before query simplification and on the completed relational root.</summary>
public sealed class MySqlLockingReadExpressionTests
{
    /// <summary>Supplies every mode and its idempotent repeated form separately.</summary>
    public static TheoryData<int, bool> ModeCases
    {
        get
        {
            var cases = new TheoryData<int, bool>();

            foreach (var mode in new[] { 1, 2, 3, 4 })
            {
                cases.Add(mode, false);
                cases.Add(mode, true);
            }

            return cases;
        }
    }

    /// <summary>Supplies every conflicting ordered pair rather than relying on one precedence example.</summary>
    public static TheoryData<int, int> ConflictCases
    {
        get
        {
            var cases = new TheoryData<int, int>();

            foreach (var first in new[] { 1, 2, 3, 4 })
            {
                foreach (var second in new[] { 1, 2, 3, 4 })
                {
                    if (first != second)
                    {
                        cases.Add(first, second);
                    }
                }
            }

            return cases;
        }
    }

    /// <summary>One typed mode survives marker removal, including repeated identical markers.</summary>
    /// <param name="mode">The internal mode represented through a public test data value.</param>
    /// <param name="repeated">Whether to repeat the same public marker.</param>
    [Theory]
    [MemberData(nameof(ModeCases))]
    public void Lowering_retains_one_mode_and_removes_only_its_deferred_markers(
        int mode,
        bool repeated
    )
    {
        // Arrange
        using var context = CreateContext();
        var source = context.Set<LockingRow>();
        var marked = Mark(source, mode);
        var query = (repeated
            ? Mark(marked, mode)
            : marked).Where(row => row.Id > 0);

        var compilation = CreateCompilation(context);

        // Act
        var actual = MySqlLockingReadLowering.Process(query.Expression, compilation);

        // Assert
        Assert.Equal((MySqlLockingReadMode)mode, compilation.LockingReadMode);
        var method = Assert.IsType<MethodCallExpression>(actual, exactMatch: false);
        Assert.Equal(nameof(Queryable.Where), method.Method.Name);
        Assert.IsType<EntityQueryRootExpression>(method.Arguments[0]);
        Assert.Same(((MethodCallExpression)query.Expression).Arguments[1], method.Arguments[1]);
    }

    /// <summary>Conflicting modes fail independently of their order before EF can choose a SQL shape.</summary>
    /// <param name="first">The first mode applied.</param>
    /// <param name="second">The conflicting mode.</param>
    [Theory]
    [MemberData(nameof(ConflictCases))]
    public void Every_conflicting_mode_pair_is_rejected(
        int first,
        int second
    )
    {
        // Arrange
        using var context = CreateContext();
        var query = Mark(Mark(context.Set<LockingRow>(), first), second);
        var compilation = CreateCompilation(context);

        // Act
        var exception = Record.Exception(() => MySqlLockingReadLowering.Process(query.Expression, compilation));

        // Assert
        Assert.IsType<InvalidOperationException>(exception);
        Assert.Equal(MySqlLockingReadMode.None, compilation.LockingReadMode);
    }

    /// <summary>Aggregate terminals cannot erase the requested locking policy during EF simplification.</summary>
    /// <param name="any">Whether to use Any rather than Count.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Scalar_terminals_are_rejected_before_their_source_can_disappear(
        bool any
    )
    {
        // Arrange
        using var context = CreateContext();
        var marked = context
            .Set<LockingRow>()
            .ForUpdate();

        var terminal = Expression.Call(
            (any
                ? QueryableMethods.AnyWithoutPredicate
                : QueryableMethods.CountWithoutPredicate)
            .MakeGenericMethod(typeof(LockingRow)),
            marked.Expression);

        var compilation = CreateCompilation(context);

        // Act
        var exception = Record.Exception(() => MySqlLockingReadLowering.Process(terminal, compilation));

        // Assert
        Assert.IsType<InvalidOperationException>(exception);
        Assert.Equal(MySqlLockingReadMode.None, compilation.LockingReadMode);
    }

    /// <summary>A marker inside a predicate subquery never acquires the outer query's scope.</summary>
    [Fact]
    public void Nested_marked_scalar_source_is_rejected()
    {
        // Arrange
        using var context = CreateContext();
        var query = context
            .Set<LockingRow>()
            .Where(row => context
                .Set<LockingRow>()
                .ForUpdate()
                .Any(other => other.Id == row.Id));

        var compilation = CreateCompilation(context);

        // Act
        var exception = Record.Exception(() => MySqlLockingReadLowering.Process(query.Expression, compilation));

        // Assert
        Assert.IsType<InvalidOperationException>(exception);
        Assert.Equal(MySqlLockingReadMode.None, compilation.LockingReadMode);
    }

    /// <summary>An ordinary scalar aggregate remains an independent, unmarked scope.</summary>
    [Fact]
    public void Unmarked_scalar_subquery_does_not_inherit_the_outer_mode()
    {
        // Arrange
        using var context = CreateContext();
        var query = context
            .Set<LockingRow>()
            .ForUpdate()
            .Where(row => context
                    .Set<LockingRow>()
                    .Count(other => other.Id == row.Id)
                > 0);

        var compilation = CreateCompilation(context);
        var originalPredicate = ((MethodCallExpression)query.Expression).Arguments[1];

        // Act
        var actual = MySqlLockingReadLowering.Process(query.Expression, compilation);

        // Assert
        Assert.Equal(MySqlLockingReadMode.Update, compilation.LockingReadMode);
        Assert.Same(originalPredicate,
            Assert
                .IsType<MethodCallExpression>(actual, exactMatch: false)
                .Arguments[1]);
    }

    /// <summary>Without a Doka marker, unsupported locking-only shapes remain ordinary unchanged EF expressions.</summary>
    [Fact]
    public void Ordinary_expression_is_returned_by_identity()
    {
        // Arrange
        using var context = CreateContext();
        var query = context
            .Set<LockingRow>()
            .Distinct()
            .GroupBy(row => row.Name);

        var compilation = CreateCompilation(context);

        // Act
        var actual = MySqlLockingReadLowering.Process(query.Expression, compilation);

        // Assert
        Assert.Same(query.Expression, actual);
        Assert.Equal(MySqlLockingReadMode.None, compilation.LockingReadMode);
    }

    /// <summary>Pregenerated SQL cannot silently lose locking metadata; ordinary pregeneration stays unaffected.</summary>
    /// <param name="locking">Whether the source requests a locking read.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Pregenerated_query_boundary_rejects_only_locking_sources(
        bool locking
    )
    {
        // Arrange
        using var context = CreateContext();
        var factory = context.GetService<IQueryCompilationContextFactory>();

        // WHY: The actual experimental factory is needed to prove the fail-closed boundary;
        // this test does not claim new pregenerated-query support.
#pragma warning disable EF9100
        var compilation = (MySqlQueryCompilationContext)factory.CreatePrecompiled(async: false);
#pragma warning restore EF9100
        var query = locking
            ? context
                .Set<LockingRow>()
                .ForUpdate()
            : context.Set<LockingRow>();

        Expression? lowered = null;

        // Act
        var exception =
            Record.Exception(() => lowered = MySqlLockingReadLowering.Process(query.Expression, compilation));

        // Assert
        if (locking)
        {
            Assert.IsType<InvalidOperationException>(exception);
            Assert.Null(lowered);
        }
        else
        {
            Assert.Null(exception);
            Assert.Same(query.Expression, lowered);
        }

        Assert.Equal(MySqlLockingReadMode.None, compilation.LockingReadMode);
    }

    /// <summary>The final root receives metadata on a copy rather than mutating a shared relational tree.</summary>
    [Fact]
    public void Final_validation_copies_the_root_and_retains_unrelated_annotations()
    {
        // Arrange
        using var context = CreateContext();
        var compilation = CreateCompilation(context);
        compilation.LockingReadMode = MySqlLockingReadMode.Share;
        var select = CreateSelect(context, annotatedTable: true);
        var shaped = new ShapedQueryExpression(select, Expression.Constant(1));

        // Act
        var actual = MySqlLockingReadQueryValidator.Validate(shaped, compilation);

        // Assert
        var root = Assert.IsType<SelectExpression>(Assert.IsType<ShapedQueryExpression>(actual)
            .QueryExpression);

        Assert.NotSame(select, root);
        Assert.NotSame(select.Tables[0], root.Tables[0]);
        Assert.Null(select
            .Tables[0]
            .FindAnnotation(MySqlLockingReadMetadata.AnnotationName));

        Assert.Equal(MySqlLockingReadMode.Share, MySqlLockingReadMetadata.GetMode(root));
        Assert.Equal("retained",
            root
                .Tables[0]
                .FindAnnotation("test:Existing")
                ?.Value);
    }

    /// <summary>Null-driven parameter processing must preserve the typed execution policy through Select.Update.</summary>
    [Fact]
    public void Parameter_nullability_rewrite_preserves_locking_metadata()
    {
        // Arrange
        using var context = CreateContext();
        var compilation = CreateCompilation(context);
        compilation.LockingReadMode = MySqlLockingReadMode.Update;
        var factory = context.GetService<ISqlExpressionFactory>();
        var mapping = context
            .GetService<IRelationalTypeMappingSource>()
            .FindMapping(typeof(string))!;

        var predicate = factory.Equal(
            new ColumnExpression("Name", "r", typeof(string), mapping, nullable: true),
            new SqlParameterExpression("needle", typeof(string), mapping));

        var input = new ShapedQueryExpression(CreateSelect(context, predicate), Expression.Constant(1));
        var shaped = (ShapedQueryExpression)MySqlLockingReadQueryValidator.Validate(input, compilation);

        // WHY: The actual EF10 SQL processor constructor pins metadata survival through its
        // internal parameter-processing seam; production diagnostics remain unchanged.
#pragma warning disable EF1001
        var processor = context
            .GetService<IRelationalParameterBasedSqlProcessorFactory>()
            .Create(new RelationalParameterBasedSqlProcessorParameters(false,
                ParameterTranslationMode.MultipleParameters));
#pragma warning restore EF1001

        var parameters = new ParametersCacheDecorator(new Dictionary<string, object?> { ["needle"] = null });

        // Act
        var actual = processor.Process(shaped.QueryExpression, parameters);

        // Assert
        var root = Assert.IsType<SelectExpression>(actual);
        Assert.Equal(MySqlLockingReadMode.Update, MySqlLockingReadMetadata.GetMode(root));
        Assert.IsType<SqlUnaryExpression>(root.Predicate);
    }

    /// <summary>A later rewrite cannot turn a locking root into a tableless command and drop its execution policy.</summary>
    [Fact]
    public void Late_scope_removal_fails_closed_instead_of_losing_the_policy()
    {
        // Arrange
        using var context = CreateContext();
        var direct = CreateSelect(context);
        var tableless = direct.Update([],
            direct.Predicate,
            direct.GroupBy,
            direct.Having,
            direct.Projection,
            direct.Orderings,
            direct.Offset,
            direct.Limit);

        // Act
        var exception = Record.Exception(() =>
            MySqlLockingReadQueryValidator.ValidateProcessedSelect(tableless, MySqlLockingReadMode.Update));

        // Assert
        Assert.IsType<InvalidOperationException>(exception);
        Assert.Empty(tableless.Tables);
    }

    /// <summary>A native aggregate introduced by a function translator cannot evade the final shape guard.</summary>
    /// <param name="name">The built-in aggregate name.</param>
    [Theory]
    [InlineData("COUNT")]
    [InlineData("sum")]
    [InlineData("AVG")]
    [InlineData("MIN")]
    [InlineData("MAX")]
    public void Final_root_aggregate_is_rejected(
        string name
    )
    {
        // Arrange
        using var context = CreateContext();
        var compilation = CreateCompilation(context);
        compilation.LockingReadMode = MySqlLockingReadMode.Update;
        var factory = context.GetService<ISqlExpressionFactory>();
        var aggregate = factory.Function(name,
            [factory.Constant(1)],
            nullable: false,
            argumentsPropagateNullability: [false],
            typeof(int));

        var shaped = new ShapedQueryExpression(CreateSelect(context, projection: aggregate), Expression.Constant(1));

        // Act
        var exception = Record.Exception(() => MySqlLockingReadQueryValidator.Validate(shaped, compilation));

        // Assert
        Assert.IsType<InvalidOperationException>(exception);
    }

    /// <summary>The same native aggregate remains valid in an independent scalar subquery.</summary>
    [Fact]
    public void Final_root_accepts_an_unmarked_scalar_aggregate_subquery()
    {
        // Arrange
        using var context = CreateContext();
        var compilation = CreateCompilation(context);
        compilation.LockingReadMode = MySqlLockingReadMode.Update;
        var factory = context.GetService<ISqlExpressionFactory>();
        var aggregate = factory.Function("COUNT",
            [factory.Constant(1)],
            nullable: false,
            argumentsPropagateNullability: [false],
            typeof(int));

        var nested = CreateSelect(context, projection: aggregate);
        var shaped = new ShapedQueryExpression(
            CreateSelect(context, projection: new ScalarSubqueryExpression(nested)),
            Expression.Constant(1));

        // Act
        var actual = MySqlLockingReadQueryValidator.Validate(shaped, compilation);

        // Assert
        var root = Assert.IsType<SelectExpression>(Assert.IsType<ShapedQueryExpression>(actual)
            .QueryExpression);

        Assert.Equal(MySqlLockingReadMode.Update, MySqlLockingReadMetadata.GetMode(root));
        Assert.Equal(MySqlLockingReadMode.None, MySqlLockingReadMetadata.GetMode(nested));
        Assert.Null(nested
            .Tables[0]
            .FindAnnotation(MySqlLockingReadMetadata.AnnotationName));
    }

    /// <summary>Allowed unsupported engines still cannot receive a locking syntax they do not implement.</summary>
    /// <param name="mariaDb">Whether the target is MariaDB.</param>
    /// <param name="major">The major version.</param>
    /// <param name="minor">The minor version.</param>
    /// <param name="patch">The patch version.</param>
    /// <param name="mode">The requested locking mode.</param>
    /// <param name="allowed">Whether that exact syntax exists.</param>
    [Theory]
    [InlineData(false, 8, 0, 0, 2, false)]
    [InlineData(false, 8, 0, 1, 2, true)]
    [InlineData(false, 8, 0, 0, 3, false)]
    [InlineData(false, 8, 0, 1, 3, true)]
    [InlineData(false, 8, 0, 0, 4, false)]
    [InlineData(false, 8, 0, 1, 4, true)]
    [InlineData(true, 10, 2, 99, 3, false)]
    [InlineData(true, 10, 3, 0, 3, true)]
    [InlineData(true, 10, 5, 99, 4, false)]
    [InlineData(true, 10, 6, 0, 4, true)]
    public void Exact_syntax_boundaries_apply_even_with_allow_unsupported(
        bool mariaDb,
        int major,
        int minor,
        int patch,
        int mode,
        bool allowed
    )
    {
        // Arrange
        var version = new Version(major, minor, patch);
        var server = mariaDb
            ? MySqlServerVersion.MariaDb(version, MySqlServerVersionCompatibilityMode.AllowUnsupported)
            : MySqlServerVersion.MySql(version, MySqlServerVersionCompatibilityMode.AllowUnsupported);

        using var context = CreateContext(server);
        var compilation = CreateCompilation(context);
        compilation.LockingReadMode = (MySqlLockingReadMode)mode;
        var shaped = new ShapedQueryExpression(CreateSelect(context), Expression.Constant(1));

        // Act
        var exception = Record.Exception(() => MySqlLockingReadQueryValidator.Validate(shaped, compilation));

        // Assert
        if (allowed)
        {
            Assert.Null(exception);
        }
        else
        {
            Assert.IsType<InvalidOperationException>(exception);
        }
    }

    private static IQueryable<LockingRow> Mark(
        IQueryable<LockingRow> source,
        int mode
    ) => (MySqlLockingReadMode)mode switch
    {
        MySqlLockingReadMode.Update => source.ForUpdate(),
        MySqlLockingReadMode.Share => source.ForShare(),
        MySqlLockingReadMode.NoWait => source.ForUpdateNoWait(),
        MySqlLockingReadMode.SkipLocked => source.ForUpdateSkipLocked(),
        _ => throw new ArgumentOutOfRangeException(nameof(mode)),
    };

    private static MySqlQueryCompilationContext CreateCompilation(
        DbContext context
    ) => (MySqlQueryCompilationContext)context
        .GetService<IQueryCompilationContextFactory>()
        .Create(async: false);

    private static LockingContext CreateContext(
        MySqlServerVersion? server = null
    ) => new(new DbContextOptionsBuilder<LockingContext>()

        // WHY: SQL-tree fixtures own transient providers; unrelated global cache history is not an assertion.
        .EnableServiceProviderCaching(false)
        .ConfigureWarnings(warnings => warnings.Log(CoreEventId.ManyServiceProvidersCreatedWarning))
        .UseMySql("Server=localhost;Database=locking_expression;User ID=unused;Password=unused;",
            server ?? MySqlServerVersion.MySql(new Version(8, 4, 0)))
        .Options);

    /// <summary>Builds a completed root through EF's actual relational constructor without executing SQL.</summary>
    private static SelectExpression CreateSelect(
        LockingContext context,
        SqlExpression? predicate = null,
        SqlExpression? projection = null,
        bool annotatedTable = false
    )
    {
        var table = context
            .Model
            .GetRelationalModel()
            .FindTable("LockingExpressionRows", schema: null)!;

        var mapping = context
            .GetService<IRelationalTypeMappingSource>()
            .FindMapping(typeof(int))!;

        // WHY: Direct relational-node construction isolates annotation/copy semantics from LINQ simplification.
#pragma warning disable EF1001
        var source = new TableExpression("r", table);

        if (annotatedTable)
        {
            source = (TableExpression)source.AddAnnotation("test:Existing", "retained");
        }

        return new SelectExpression(alias: null,
            [source],
            predicate,
            groupBy: [],
            having: null,
            [
                new ProjectionExpression(
                    projection ?? new ColumnExpression("Id", "r", typeof(int), mapping, nullable: false),
                    "Id"),
            ],
            distinct: false,
            orderings: [],
            offset: null,
            limit: null,
            new SqlAliasManager());
#pragma warning restore EF1001
    }

    private sealed class LockingContext : DbContext
    {
        /// <summary>Initializes the context with its supplied options and model settings.</summary>
        public LockingContext(
            DbContextOptions<LockingContext> options
        ) : base(options) { }

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        ) => modelBuilder.Entity<LockingRow>(entity =>
        {
            entity.ToTable("LockingExpressionRows");
            entity.HasKey(row => row.Id);
            entity.UseStorageEngine("InnoDB");
        });
    }

    private sealed class LockingRow
    {
        public int Id { get; set; }
        public string? Name { get; set; }
    }
}
