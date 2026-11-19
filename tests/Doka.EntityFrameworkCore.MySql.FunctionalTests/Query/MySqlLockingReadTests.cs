namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

/// <summary>Verifies deferred locking operators, exact SQL scope and offline API contracts.</summary>
public sealed partial class MySqlLockingReadTests
{
    /// <summary>Returns diagnostic lookalikes independently under both dialects.</summary>
    public static IEnumerable<TheoryDataRow<bool, string>> MagicTags => s_dialects.SelectMany(mariaDb => new[]
    {
        new TheoryDataRow<bool, string>(mariaDb, "__efcore_for_update__"),
        new TheoryDataRow<bool, string>(mariaDb, "__efcore_for_share__"),
        new TheoryDataRow<bool, string>(mariaDb, "__efcore_for_update_nowait__"),
        new TheoryDataRow<bool, string>(mariaDb, "__efcore_for_update_skiplocked__"),
    });

    /// <summary>Reports direct-block source compositions without conflating native lock ownership with SQL shape.</summary>
    public static IEnumerable<TheoryDataRow<bool, string>> DirectShapes => s_dialects.SelectMany(mariaDb => new[]
    {
        new TheoryDataRow<bool, string>(mariaDb, "inner"),
        new TheoryDataRow<bool, string>(mariaDb, "left"),
        new TheoryDataRow<bool, string>(mariaDb, "reference"),
        new TheoryDataRow<bool, string>(mariaDb, "straight"),
    });

    /// <summary>All four public operators require reference rows and preserve the deferred IQueryable shape.</summary>
    /// <param name="mode">The public locking mode.</param>
    [Theory]
    [MemberData(nameof(Modes))]
    public void Public_operator_preserves_the_reference_query_contract(
        string mode
    )
    {
        // Arrange
        var connections = new ConnectionAttempts();
        using var context = CreateContext(false, connections);
        IQueryable<LockRow> source = context.Set<LockRow>();
        var methodName = mode switch
        {
            "Update" => nameof(MySqlQueryableExtensions.ForUpdate),
            "Share" => nameof(MySqlQueryableExtensions.ForShare),
            "NoWait" => nameof(MySqlQueryableExtensions.ForUpdateNoWait),
            "SkipLocked" => nameof(MySqlQueryableExtensions.ForUpdateSkipLocked),
            _ => throw new ArgumentOutOfRangeException(nameof(mode)),
        };

        // Act
        var result = Mark(source, mode);
        var method = typeof(MySqlQueryableExtensions).GetMethod(methodName)!;
        var arguments = method.GetGenericArguments();

        // Assert
        Assert.IsAssignableFrom<IQueryable<LockRow>>(result);
        Assert.Same(source.Provider, result.Provider);
        var parameter = Assert.Single(arguments);
        Assert.Equal(GenericParameterAttributes.ReferenceTypeConstraint,
            parameter.GenericParameterAttributes & GenericParameterAttributes.ReferenceTypeConstraint);

        Assert.Single(method.GetParameters());
        Assert.Equal(0, connections.Count);
    }

    /// <summary>Every supported mode retains filter parameters and paging before its native final-block clause.</summary>
    /// <param name="mariaDb">Whether the MariaDB dialect is configured.</param>
    /// <param name="mode">The public locking mode.</param>
    [Theory]
    [MemberData(nameof(DialectModes))]
    public void Offline_sql_preserves_filter_order_paging_and_the_final_clause(
        bool mariaDb,
        string mode
    )
    {
        // Arrange
        var connections = new ConnectionAttempts();
        using var context = CreateContext(mariaDb, connections);
        var minimumId = 7;
        var query = Mark(context
                    .Set<LockRow>()
                    .Where(row => row.Id > minimumId),
                mode)
            .OrderBy(row => row.Name)
            .ThenBy(row => row.Id)
            .Skip(2)
            .Take(3)
            .AsNoTracking();

        // Act
        var sql = query.ToQueryString();
        var executable = ExecutableSql(sql)
            .TrimEnd()
            .TrimEnd(';');

        // Assert
        Assert.EndsWith(Clause(mariaDb, mode), executable, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(executable, Clause(mariaDb, mode)));
        Assert.Contains("minimumId", sql, StringComparison.Ordinal);
        Assert.Contains("WHERE", executable, StringComparison.Ordinal);
        Assert.Contains("ORDER BY", executable, StringComparison.Ordinal);
        Assert.Contains("LIMIT", executable, StringComparison.Ordinal);
        Assert.True(executable.LastIndexOf("LIMIT", StringComparison.Ordinal)
            < executable.LastIndexOf(Clause(mariaDb, mode), StringComparison.Ordinal));

        Assert.Equal(0, connections.Count);
    }

    /// <summary>Repeating one policy is idempotent and ordinary comments remain diagnostic.</summary>
    /// <param name="mariaDb">Whether the MariaDB dialect is configured.</param>
    /// <param name="mode">The public locking mode.</param>
    [Theory]
    [MemberData(nameof(DialectModes))]
    public void Repeating_one_mode_preserves_one_clause_and_diagnostic_tags(
        bool mariaDb,
        string mode
    )
    {
        // Arrange
        var connections = new ConnectionAttempts();
        using var context = CreateContext(mariaDb, connections);
        var query = Mark(Mark(context.Set<LockRow>(), mode), mode)
            .TagWith("ordinary locking diagnostic");

        // Act
        var sql = query.ToQueryString();

        // Assert
        Assert.Contains("-- ordinary locking diagnostic", sql, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(ExecutableSql(sql), Clause(mariaDb, mode)));
        Assert.Equal(0, connections.Count);
    }

    /// <summary>Every distinct ordered pair rejects ambiguity instead of selecting a mode by visitor order.</summary>
    /// <param name="mariaDb">Whether the MariaDB dialect is configured.</param>
    /// <param name="first">The inner policy.</param>
    /// <param name="second">The outer policy.</param>
    [Theory]
    [MemberData(nameof(ConflictingModes))]
    public void Different_modes_conflict_in_every_order(
        bool mariaDb,
        string first,
        string second
    )
    {
        // Arrange
        var connections = new ConnectionAttempts();
        using var context = CreateContext(mariaDb, connections);

        // Act
        var exception = Record.Exception(() => Mark(Mark(context.Set<LockRow>(), first), second)
            .ToQueryString());

        // Assert
        var rejected = Assert.IsType<InvalidOperationException>(exception);
        Assert.Contains("lock", rejected.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, connections.Count);
    }

    /// <summary>Magic-looking tags cannot select a locking mode.</summary>
    /// <param name="mariaDb">Whether the MariaDB dialect is configured.</param>
    /// <param name="tag">The diagnostic text that must not affect execution policy.</param>
    [Theory]
    [MemberData(nameof(MagicTags))]
    public void Tag_lookalikes_never_create_a_lock_clause(
        bool mariaDb,
        string tag
    )
    {
        // Arrange
        var connections = new ConnectionAttempts();
        using var context = CreateContext(mariaDb, connections);

        // Act
        var sql = context
            .Set<LockRow>()
            .TagWith(tag)
            .ToQueryString();

        var executable = ExecutableSql(sql);

        // Assert
        Assert.Contains(tag, sql, StringComparison.Ordinal);
        Assert.DoesNotContain("FOR UPDATE", executable, StringComparison.Ordinal);
        Assert.DoesNotContain("FOR SHARE", executable, StringComparison.Ordinal);
        Assert.DoesNotContain("LOCK IN SHARE MODE", executable, StringComparison.Ordinal);
        Assert.Equal(0, connections.Count);
    }

    /// <summary>Direct joins, reference includes and directed joins keep one final lock clause.</summary>
    /// <param name="mariaDb">Whether the MariaDB dialect is configured.</param>
    /// <param name="shape">The direct relational shape.</param>
    [Theory]
    [MemberData(nameof(DirectShapes))]
    public void Direct_composition_preserves_tables_join_direction_and_lock_scope(
        bool mariaDb,
        string shape
    )
    {
        // Arrange
        var connections = new ConnectionAttempts();
        using var context = CreateContext(mariaDb, connections);
        var rows = context.Set<LockRow>();
        var parents = context.Set<LockParent>();

        // Act
        var sql = shape switch
        {
            "inner" => rows
                .Join(parents,
                    row => row.ParentId,
                    parent => parent.Id,
                    (row, parent) => new LockProjection
                    {
                        Id = row.Id,
                        ParentName = parent.Name,
                    })
                .ForUpdate()
                .ToQueryString(),
            "left" => (from row in rows
                    join parent in parents on row.ParentId equals parent.Id into matches
                    from parent in matches.DefaultIfEmpty()
                    select new LockProjection
                    {
                        Id = row.Id,
                        ParentName = parent.Name,
                    })
                .ForUpdate()
                .ToQueryString(),
            "reference" => rows
                .Include(row => row.Parent)
                .ForUpdate()
                .ToQueryString(),
            "straight" => rows
                .StraightJoin(parents,
                    row => row.ParentId,
                    parent => parent.Id,
                    (row, parent) => new LockProjection
                    {
                        Id = row.Id,
                        ParentName = parent.Name,
                    })
                .ForUpdate()
                .ToQueryString(),
            _ => throw new ArgumentOutOfRangeException(nameof(shape)),
        };

        // Assert
        Assert.Contains("`LockRows`", sql, StringComparison.Ordinal);
        Assert.Contains("`LockParents`", sql, StringComparison.Ordinal);
        Assert.EndsWith("FOR UPDATE",
            sql
                .TrimEnd()
                .TrimEnd(';'),
            StringComparison.Ordinal);

        Assert.Equal(1, CountOccurrences(ExecutableSql(sql), "FOR UPDATE"));

        if (shape == "straight")
        {
            Assert.Contains("STRAIGHT_JOIN", sql, StringComparison.Ordinal);
            Assert.True(sql.IndexOf("`LockRows`", StringComparison.Ordinal)
                < sql.IndexOf("`LockParents`", StringComparison.Ordinal));
        }

        Assert.Equal(0, connections.Count);
    }

    /// <summary>A normal scalar subquery remains a separate unmarked scope inside the locked final block.</summary>
    /// <param name="mariaDb">Whether the MariaDB dialect is configured.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Unmarked_scalar_subquery_does_not_receive_the_outer_lock(
        bool mariaDb
    )
    {
        // Arrange
        var connections = new ConnectionAttempts();
        using var context = CreateContext(mariaDb, connections);
        var query = context
            .Set<LockRow>()
            .Select(row => new LockProjection
            {
                Id = row.Id,
                ParentName = context
                    .Set<LockParent>()
                    .Where(parent => parent.Id >= row.ParentId)
                    .OrderBy(parent => parent.Id)
                    .Select(parent => parent.Name)
                    .FirstOrDefault(),
            })
            .ForUpdate();

        // Act
        var sql = query.ToQueryString();

        // Assert
        Assert.Equal(2, CountOccurrences(sql, "SELECT"));
        Assert.Equal(1, CountOccurrences(sql, "FOR UPDATE"));
        Assert.EndsWith("FOR UPDATE",
            sql
                .TrimEnd()
                .TrimEnd(';'),
            StringComparison.Ordinal);

        Assert.Equal(0, connections.Count);
    }

    /// <summary>Compiling a marked sibling never mutates the reusable ordinary query.</summary>
    /// <param name="mariaDb">Whether the MariaDB dialect is configured.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Marked_sibling_leaves_the_original_query_tree_and_sql_unchanged(
        bool mariaDb
    )
    {
        // Arrange
        var connections = new ConnectionAttempts();
        using var context = CreateContext(mariaDb, connections);
        var query = context
            .Set<LockRow>()
            .Where(row => row.Id > 0);

        var expression = query.Expression;
        var before = query.ToQueryString();

        // Act
        var marked = query
            .ForUpdateNoWait()
            .ToQueryString();

        var after = query.ToQueryString();

        // Assert
        Assert.Same(expression, query.Expression);
        Assert.Equal(before, after);
        Assert.DoesNotContain("FOR UPDATE", after, StringComparison.Ordinal);
        Assert.Contains("FOR UPDATE NOWAIT", marked, StringComparison.Ordinal);
        Assert.Equal(0, connections.Count);
    }

    /// <summary>The ordinary aggregate projection used by the rejection probe is independently translatable.</summary>
    /// <param name="mariaDb">Whether the MariaDB dialect is configured.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Ordinary_group_aggregate_remains_translatable_without_locking(
        bool mariaDb
    )
    {
        // Arrange
        var connections = new ConnectionAttempts();
        using var context = CreateContext(mariaDb, connections);
        var query = context
            .Set<LockRow>()
            .GroupBy(row => row.ParentId)
            .Select(group => new LockProjection
            {
                Id = group.Key,
                Count = group.Count(),
            });

        // Act
        var sql = query.ToQueryString();

        // Assert
        Assert.Contains("GROUP BY", sql, StringComparison.Ordinal);
        Assert.Contains("COUNT(", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("FOR UPDATE", sql, StringComparison.Ordinal);
        Assert.Equal(0, connections.Count);
    }

    /// <summary>The temporal rejection fixture is valid and independently translates without a locking operator.</summary>
    /// <param name="mariaDb">Whether the MariaDB native temporal dialect is configured.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Ordinary_temporal_source_remains_translatable_without_locking(
        bool mariaDb
    )
    {
        // Arrange
        var connections = new ConnectionAttempts();
        using var context = CreateContext(mariaDb, connections, temporal: true);
        var query = context
            .Set<LockRow>()
            .TemporalAsOf(new DateTime(2026,
                1,
                1,
                0,
                0,
                0,
                DateTimeKind.Utc));

        // Act
        var sql = query.ToQueryString();

        // Assert
        Assert.Contains("`LockRows`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("FOR UPDATE", sql, StringComparison.Ordinal);

        if (mariaDb)
        {
            Assert.Contains("FOR SYSTEM_TIME AS OF", sql, StringComparison.Ordinal);
        }
        else
        {
            Assert.Contains("`LockRowsHistory`", sql, StringComparison.Ordinal);
        }

        Assert.Equal(0, connections.Count);
    }

    /// <summary>Parallel compilation keeps dialect and mode in each context rather than shared mutable query state.</summary>
    [Fact]
    public void Parallel_context_compilations_keep_independent_dialects_and_modes()
    {
        // Arrange
        var cases = GetDialectModeCases()
            .ToArray();

        var results = new string[cases.Length];

        // Act
        Parallel.For(0,
            cases.Length,
            index =>
            {
                var data = cases[index];
                using var context = CreateContext(data.MariaDb, new ConnectionAttempts());
                results[index] = Mark(context.Set<LockRow>(), data.Mode)
                    .ToQueryString();
            });

        // Assert
        Assert.All(Enumerable.Range(0, cases.Length),
            index => Assert.EndsWith(
                Clause(cases[index].MariaDb, cases[index].Mode),
                results[index]
                    .TrimEnd()
                    .TrimEnd(';'),
                StringComparison.Ordinal));
    }
}
