namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

public sealed partial class MySqlLockingReadTests
{
    private static readonly string[] s_nestedBindings = ["Ordinary", "Recursive", "Materialized"];

    /// <summary>Reports each binding independently under both supported SQL dialects.</summary>
    public static TheoryData<bool, string> NestedBindings
    {
        get
        {
            var cases = new TheoryData<bool, string>();

            foreach (var mariaDb in s_dialects)
            {
                foreach (var binding in s_nestedBindings)
                {
                    cases.Add(mariaDb, binding);
                }
            }

            return cases;
        }
    }

    /// <summary>Reports each nested binding and locking policy independently.</summary>
    public static TheoryData<bool, string, string> NestedBindingModes
    {
        get
        {
            var cases = new TheoryData<bool, string, string>();

            foreach (var mariaDb in s_dialects)
            {
                foreach (var mode in s_modes)
                {
                    foreach (var binding in s_nestedBindings)
                    {
                        cases.Add(mariaDb, mode, binding);
                    }
                }
            }

            return cases;
        }
    }

    /// <summary>A nested binding cannot send a physical outer locking query through the unlocked WITH path.</summary>
    /// <param name="mariaDb">Whether the MariaDB dialect is configured.</param>
    /// <param name="mode">The public locking mode.</param>
    /// <param name="binding">The nested source binding.</param>
    [Theory]
    [MemberData(nameof(NestedBindingModes))]
    public void Nested_binding_in_scalar_projection_is_rejected_before_database_io(
        bool mariaDb,
        string mode,
        string binding
    )
    {
        // Arrange
        var connections = new ConnectionAttempts();
        using var context = CreateContext(mariaDb, connections);
        var source = BindNestedSource(context.Set<LockParent>(), binding);
        var query = Mark(context.Set<LockRow>(), mode)
            .Select(row => new
            {
                row.Id,
                Count = source.Count(parent => parent.Id >= row.ParentId),
            });

        // Act
        var exception = Record.Exception(query.ToQueryString);

        // Assert
        Assert.Equal("Locking reads cannot target materialized sources.",
            Assert.IsType<InvalidOperationException>(exception)
                .Message);

        Assert.Equal(0, connections.Count);
    }

    /// <summary>A binding inside EXISTS is rejected even when the final query still reads a physical root table.</summary>
    /// <param name="mariaDb">Whether the MariaDB dialect is configured.</param>
    /// <param name="mode">The public locking mode.</param>
    [Theory]
    [MemberData(nameof(DialectModes))]
    public void Nested_cte_in_exists_predicate_is_rejected_before_database_io(
        bool mariaDb,
        string mode
    )
    {
        // Arrange
        var connections = new ConnectionAttempts();
        using var context = CreateContext(mariaDb, connections);
        var source = context
            .Set<LockParent>()
            .WithCte(shared => shared);

        var query = Mark(context.Set<LockRow>(), mode)
            .Where(row => source.Any(parent => parent.Id == row.ParentId));

        // Act
        var exception = Record.Exception(query.ToQueryString);

        // Assert
        Assert.Equal("Locking reads cannot target materialized sources.",
            Assert.IsType<InvalidOperationException>(exception)
                .Message);

        Assert.Equal(0, connections.Count);
    }

    /// <summary>An ordinary scalar aggregate remains a separate SQL scope and keeps the physical outer lock.</summary>
    /// <param name="mariaDb">Whether the MariaDB dialect is configured.</param>
    /// <param name="mode">The public locking mode.</param>
    [Theory]
    [MemberData(nameof(DialectModes))]
    public void Physical_scalar_subquery_preserves_only_the_outer_locking_clause(
        bool mariaDb,
        string mode
    )
    {
        // Arrange
        var connections = new ConnectionAttempts();
        using var context = CreateContext(mariaDb, connections);
        var query = Mark(context.Set<LockRow>(), mode)
            .Select(row => new
            {
                row.Id,
                Count = context
                    .Set<LockParent>()
                    .Count(parent => parent.Id >= row.ParentId),
            });

        // Act
        var sql = ExecutableSql(query.ToQueryString())
            .TrimEnd()
            .TrimEnd(';');

        // Assert
        Assert.Contains("COUNT(*)", sql, StringComparison.Ordinal);
        Assert.Contains("`LockParents`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("WITH", sql, StringComparison.Ordinal);
        Assert.EndsWith(Clause(mariaDb, mode), sql, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(sql, Clause(mariaDb, mode)));
        Assert.Equal(0, connections.Count);
    }

    /// <summary>Each nested binding remains a valid SQL source when no locking policy is requested.</summary>
    /// <param name="mariaDb">Whether the MariaDB dialect is configured.</param>
    /// <param name="binding">The nested source binding.</param>
    [Theory]
    [MemberData(nameof(NestedBindings))]
    public void Unmarked_nested_binding_preserves_its_correlated_scalar_query(
        bool mariaDb,
        string binding
    )
    {
        // Arrange
        var connections = new ConnectionAttempts();
        using var context = CreateContext(mariaDb, connections);
        var source = BindNestedSource(context.Set<LockParent>(), binding);
        var query = context
            .Set<LockRow>()
            .Select(row => new
            {
                row.Id,
                Count = source.Count(parent => parent.Id >= row.ParentId),
            });

        // Act
        var sql = ExecutableSql(query.ToQueryString());

        // Assert
        Assert.StartsWith("WITH", sql.TrimStart(), StringComparison.Ordinal);
        Assert.Contains("COUNT(*)", sql, StringComparison.Ordinal);
        Assert.Contains("`LockRows`", sql, StringComparison.Ordinal);
        Assert.Contains("`LockParents`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("FOR UPDATE", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("FOR SHARE", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("LOCK IN SHARE MODE", sql, StringComparison.Ordinal);
        Assert.Equal(0, connections.Count);
    }

    /// <summary>Builds one nested public binding without changing the shared offline context model.</summary>
    private static IQueryable<LockParent> BindNestedSource(
        IQueryable<LockParent> source,
        string binding
    ) => binding switch
    {
        "Ordinary" => source.WithCte(shared => shared),
        "Recursive" => source.WithRecursiveCte(frontier => frontier, shared => shared, 2),
        "Materialized" => source.WithMaterializedSource(shared => shared),
        _ => throw new ArgumentOutOfRangeException(nameof(binding)),
    };
}
