namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

/// <summary>Protects real LINQ membership whose candidate is a nullable-property predicate.</summary>
public sealed class MySqlJsonMembershipNullPredicateTests
{
    /// <summary>Supplies both engine families, NULL-test forms and membership directions.</summary>
    public static TheoryData<bool, bool, bool> NullPredicateCases
    {
        get
        {
            var cases = new TheoryData<bool, bool, bool>();

            foreach (var mariaDb in new[] { false, true })
            {
                foreach (var notNull in new[] { false, true })
                {
                    foreach (var negated in new[] { false, true })
                    {
                        cases.Add(mariaDb, notNull, negated);
                    }
                }
            }

            return cases;
        }
    }

    /// <summary>NULL predicates retain their full scope when EF translates them as the membership candidate.</summary>
    /// <param name="mariaDb">Whether the MariaDB profile is used.</param>
    /// <param name="notNull">Whether the candidate tests IS NOT NULL rather than IS NULL.</param>
    /// <param name="negated">Whether membership is negated.</param>
    [Theory]
    [MemberData(nameof(NullPredicateCases))]
    public void Nullable_property_predicate_is_grouped_as_a_complete_membership_candidate(
        bool mariaDb,
        bool notNull,
        bool negated
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);
        var rows = context.Set<NullMembershipRow>();
        var query = (notNull, negated) switch
        {
            (false, false) => rows.Where(row => row
                .Values
                .Select(value => value > 0)
                .Contains(row.Optional == null)),
            (false, true) => rows.Where(row => !row
                .Values
                .Select(value => value > 0)
                .Contains(row.Optional == null)),
            (true, false) => rows.Where(row => row
                .Values
                .Select(value => value > 0)
                .Contains(row.Optional != null)),
            (true, true) => rows.Where(row => !row
                .Values
                .Select(value => value > 0)
                .Contains(row.Optional != null)),
        };

        // Act
        var sql = query.ToQueryString();

        // Assert
        Assert.Contains("SELECT COUNT(*)", sql, StringComparison.Ordinal);

        // WHY: These are the native incident's LINQ shapes. The closing parenthesis
        // must follow IS NULL/IS NOT NULL, otherwise <=> changes what the NULL test examines.
        Assert.Contains($"WHERE (`v`.`value` > 0) <=> (`n`.`Optional` IS {(notNull ? "NOT " : string.Empty)}NULL)",
            sql,
            StringComparison.Ordinal);

        Assert.Contains(negated
                ? ") = 0"
                : ") > 0",
            sql,
            StringComparison.Ordinal);

        Assert.DoesNotContain(" IN (", sql, StringComparison.Ordinal);
        Assert.Equal(ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);
    }

    /// <summary>Creates a SQL-generation-only context under the established transient provider policy.</summary>
    private static MembershipContext CreateContext(
        bool mariaDb
    )
    {
        var builder = MySqlFunctionalTestOptions.CreateTransientBuilder<MembershipContext>();
        builder.UseMySql("Server=localhost;Database=unused;User ID=root;Password=unused;",
            mariaDb
                ? MySqlServerVersion.MariaDb(new Version(12, 3, 3))
                : MySqlServerVersion.MySql(new Version(8, 4, 13)));

        return new MembershipContext(builder.Options);
    }

    /// <summary>Defines the primitive collection and independently nullable candidate source.</summary>
    private sealed class MembershipContext : DbContext
    {
        /// <summary>Initializes the context with its supplied options and model settings.</summary>
        public MembershipContext(
            DbContextOptions<MembershipContext> options
        ) : base(options) { }

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        ) => modelBuilder
            .Entity<NullMembershipRow>()
            .ToTable("NullMembershipRows");
    }

    /// <summary>Separates the collection values from the nullable property used by the membership candidate.</summary>
    private sealed class NullMembershipRow
    {
        /// <summary>Gets or sets the row key.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets the nullable candidate source.</summary>
        public int? Optional { get; set; }

        /// <summary>Gets or sets the independently projected collection values.</summary>
        public int[] Values { get; set; } = [];
    }
}
