namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>Verifies null-test operands retain their grouping inside native JSON membership comparisons.</summary>
[IntegrationDatabaseTest]
public sealed class MySqlJsonPredicateMembershipIntegrationTests
{
    /// <summary>Gets outer-column null tests and their negations for every selected supported engine.</summary>
    public static TheoryData<string, bool, bool, int[]> RightOperandCases
    {
        get
        {
            var cases = new TheoryData<string, bool, bool, int[]>();

            foreach (var target in IntegrationTestEnvironment
                .GetSelectedTargets()
                .Where(target => IntegrationTestEnvironment
                    .GetSupportedTargets()
                    .Contains(target)))
            {
                cases.Add(target.ToString(), false, false, [1, 5]);
                cases.Add(target.ToString(), false, true, [2, 3, 4]);
                cases.Add(target.ToString(), true, false, [2, 4]);
                cases.Add(target.ToString(), true, true, [1, 3, 5]);
            }

            return cases;
        }
    }

    /// <summary>Gets nullable-element null tests and their negations for every selected supported engine.</summary>
    public static TheoryData<string, bool, bool, int[]> ProjectionCases
    {
        get
        {
            var cases = new TheoryData<string, bool, bool, int[]>();

            foreach (var target in IntegrationTestEnvironment
                .GetSelectedTargets()
                .Where(target => IntegrationTestEnvironment
                    .GetSupportedTargets()
                    .Contains(target)))
            {
                cases.Add(target.ToString(), false, false, [1, 5]);
                cases.Add(target.ToString(), false, true, [2, 3, 4]);
                cases.Add(target.ToString(), true, false, [1, 2, 4]);
                cases.Add(target.ToString(), true, true, [3, 5]);
            }

            return cases;
        }
    }

    /// <summary>An outer null predicate remains one complete right operand of null-safe membership.</summary>
    /// <param name="targetName">The selected database engine profile.</param>
    /// <param name="isNotNull">Whether the item tests non-null rather than null.</param>
    /// <param name="negated">Whether the query selects rows without a matching member.</param>
    /// <param name="expectedIds">The independent expected row identifiers.</param>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(RightOperandCases))]
    public async Task Outer_null_predicate_preserves_membership_and_its_negation(
        string targetName,
        bool isNotNull,
        bool negated,
        int[] expectedIds
    )
    {
        // Arrange
        await using var store = new PredicateStore(Enum.Parse<IntegrationDatabaseTarget>(targetName));
        await using var context = store.CreateContext();
        await context.Database.EnsureCreatedAsync(CancellationToken.None);
        await SeedAsync(context, CancellationToken.None);
        var matchingRows = (isNotNull, negated) switch
        {
            (false, false) => context.Rows.Where(row => row
                .Values
                .Select(value => value > 0)
                .Contains(row.Optional == null)),
            (false, true) => context.Rows.Where(row => !row
                .Values
                .Select(value => value > 0)
                .Contains(row.Optional == null)),
            (true, false) => context.Rows.Where(row => row
                .Values
                .Select(value => value > 0)
                .Contains(row.Optional != null)),
            (true, true) => context.Rows.Where(row => !row
                .Values
                .Select(value => value > 0)
                .Contains(row.Optional != null)),
        };

        var query = matchingRows
            .OrderBy(row => row.Id)
            .Select(row => row.Id);

        // Act
        var sql = query.ToQueryString();
        var ids = await query.ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Equal(expectedIds, ids);
        AssertMembershipSql(sql, negated);

        // WHY: Without this boundary MySQL applies the null test to the <=> result.
        // Checking isolated tokens would miss the proven wrong results.
        var nullPredicate = isNotNull
            ? "IS NOT NULL"
            : "IS NULL";

        Assert.Matches($@"<=> \(`[^`]+`\.`Optional` {nullPredicate}\)", sql);
    }

    /// <summary>A projected null predicate remains one complete left operand of null-safe membership.</summary>
    /// <param name="targetName">The selected database engine profile.</param>
    /// <param name="isNotNull">Whether the projection tests non-null rather than null.</param>
    /// <param name="negated">Whether the query selects rows without a matching member.</param>
    /// <param name="expectedIds">The independent expected row identifiers.</param>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(ProjectionCases))]
    public async Task Nullable_element_null_predicate_preserves_membership_and_its_negation(
        string targetName,
        bool isNotNull,
        bool negated,
        int[] expectedIds
    )
    {
        // Arrange
        await using var store = new PredicateStore(Enum.Parse<IntegrationDatabaseTarget>(targetName));
        await using var context = store.CreateContext();
        await context.Database.EnsureCreatedAsync(CancellationToken.None);
        await SeedAsync(context, CancellationToken.None);
        var matchingRows = (isNotNull, negated) switch
        {
            (false, false) => context.Rows.Where(row => row
                .NullableValues
                .Select(value => value == null)
                .Contains(true)),
            (false, true) => context.Rows.Where(row => !row
                .NullableValues
                .Select(value => value == null)
                .Contains(true)),
            (true, false) => context.Rows.Where(row => row
                .NullableValues
                .Select(value => value != null)
                .Contains(true)),
            (true, true) => context.Rows.Where(row => !row
                .NullableValues
                .Select(value => value != null)
                .Contains(true)),
        };

        var query = matchingRows
            .OrderBy(row => row.Id)
            .Select(row => row.Id);

        // Act
        var sql = query.ToQueryString();
        var ids = await query.ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Equal(expectedIds, ids);
        AssertMembershipSql(sql, negated);

        // WHY: Both operands share one SQL emitter. Pin the complete projected null
        // test so a future emitter change cannot silently discard its boundary.
        var nullPredicate = isNotNull
            ? "IS NOT NULL"
            : "IS NULL";

        Assert.Matches($@"WHERE \(`[^`]+`\.`value` {nullPredicate}\) <=> \(", sql);
    }

    /// <summary>Asserts the scalar-count translation and its empty-collection negation contract.</summary>
    private static void AssertMembershipSql(
        string sql,
        bool negated
    )
    {
        Assert.Contains("SELECT COUNT(*)", sql, StringComparison.Ordinal);
        Assert.Contains("JSON_TABLE(", sql, StringComparison.Ordinal);
        Assert.Contains(negated
                ? ") = 0"
                : ") > 0",
            sql,
            StringComparison.Ordinal);

        Assert.DoesNotContain(" IN (", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("JSON_CONTAINS(", sql, StringComparison.Ordinal);
    }

    /// <summary>Persists both independent membership discriminators through the normal EF JSON mappings.</summary>
    private static async Task SeedAsync(
        PredicateContext context,
        CancellationToken cancellationToken
    )
    {
        // WHY: Null/non-null outer values, mixed nullable members and an empty array
        // distinguish each polarity from its complement without relying on SQL as an oracle.
        context.Rows.AddRange(new List<PredicateRow>
        {
            new()
            {
                Id = 1,
                Values = [1, 12],
                NullableValues = [1, null],
                Optional = null,
            },
            new()
            {
                Id = 2,
                Values = [2, 3],
                NullableValues = [2, 3],
                Optional = 0,
            },
            new()
            {
                Id = 3,
                Values = [],
                NullableValues = [],
                Optional = null,
            },
            new()
            {
                Id = 4,
                Values = [112],
                NullableValues = [112],
                Optional = 0,
            },
            new()
            {
                Id = 5,
                Values = [12],
                NullableValues = [null],
                Optional = null,
            },
        });

        await context.SaveChangesAsync(cancellationToken);
        context.ChangeTracker.Clear();
    }

    /// <summary>Maps both primitive collection shapes without application-specific conversions.</summary>
    private sealed class PredicateContext : DbContext
    {
        /// <summary>Initializes the context with its supplied options and model settings.</summary>
        public PredicateContext(
            DbContextOptions<PredicateContext> options
        ) : base(options) { }

        /// <summary>Gets the normally persisted predicate-membership rows.</summary>
        public DbSet<PredicateRow> Rows => Set<PredicateRow>();

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<PredicateRow>(entity =>
            {
                entity.ToTable("JsonPredicateMembershipRows");
                entity
                    .Property(row => row.Id)
                    .ValueGeneratedNever();

                entity.PrimitiveCollection(row => row.Values);
                entity.PrimitiveCollection(row => row.NullableValues);
            });
        }
    }

    /// <summary>Separates nullable item predicates from nullable collection-element predicates.</summary>
    private sealed class PredicateRow
    {
        /// <summary>Gets or sets the identifier used by the independent result oracle.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets non-null members projected to a Boolean comparison.</summary>
        public int[] Values { get; set; } = [];

        /// <summary>Gets or sets members projected through IS NULL or IS NOT NULL.</summary>
        public int?[] NullableValues { get; set; } = [];

        /// <summary>Gets or sets the outer value tested by right-hand membership predicates.</summary>
        public int? Optional { get; set; }
    }

    /// <summary>Owns only one unique database and its isolated EF service-provider identity.</summary>
    private sealed class PredicateStore : IAsyncDisposable
    {
        private readonly string _connectionString;

        private readonly ServiceProvider _services;
        private readonly IntegrationDatabaseTarget _target;

        /// <summary>Takes ownership of the supplied database resources and configuration.</summary>
        public PredicateStore(
            IntegrationDatabaseTarget target
        )
        {
            _target = target;
            _connectionString = new MySqlConnectionStringBuilder(
                IntegrationTestEnvironment.GetBaseConnectionString(target))
            {
                Database = "DokaJsonPredicate_"
                    + Guid
                        .NewGuid()
                        .ToString("N", CultureInfo.InvariantCulture),
                Pooling = false,
                AutoEnlist = false,
            }.ConnectionString;

            _services = new ServiceCollection()
                .AddEntityFrameworkDokaMySql()
                .BuildServiceProvider();
        }

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            try
            {
                // WHY: Cleanup must remove the unique schema even after a failed or cancelled query.
                await IntegrationDatabaseUtilities.DropDatabaseAsync(_connectionString);
            }
            finally
            {
                await _services.DisposeAsync();
            }
        }

        /// <summary>Creates a context bound to this test's database and model-cache identity.</summary>
        internal PredicateContext CreateContext()
        {
            var options = IntegrationTestDbContextOptions
                .Create<PredicateContext>()
                .UseInternalServiceProvider(_services)
                .UseMySql(_connectionString, IntegrationTestEnvironment.GetServerVersion(_target))
                .Options;

            return new PredicateContext(options);
        }
    }
}
