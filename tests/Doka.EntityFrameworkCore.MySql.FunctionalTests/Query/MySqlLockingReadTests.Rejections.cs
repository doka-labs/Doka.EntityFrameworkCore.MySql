namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

public sealed partial class MySqlLockingReadTests
{
    /// <summary>Reports terminal simplifications that must not erase an earlier locking request.</summary>
    public static TheoryData<bool, string> RejectedTerminals
    {
        get
        {
            var cases = new TheoryData<bool, string>();

            foreach (var mariaDb in s_dialects)
            {
                foreach (var terminal in new[]
                    {
                        "count",
                        "long-count",
                        "any",
                        "all",
                        "sum",
                        "average",
                        "min",
                        "max",
                        "update",
                        "delete",
                    })
                {
                    cases.Add(mariaDb, terminal);
                }
            }

            return cases;
        }
    }

    /// <summary>Each public API rejects a null source without evaluating another query.</summary>
    /// <param name="mode">The public locking mode.</param>
    [Theory]
    [MemberData(nameof(Modes))]
    public void Null_sources_fail_locally(
        string mode
    )
    {
        // Arrange
        IQueryable<LockRow> source = null!;

        // Act
        var exception = Record.Exception(() => Mark(source, mode));

        // Assert
        Assert.Equal("source",
            Assert.IsType<ArgumentNullException>(exception)
                .ParamName);
    }

    /// <summary>A foreign LINQ provider cannot turn the documented lock request into a client-side no-op.</summary>
    /// <param name="mode">The public locking mode.</param>
    [Theory]
    [MemberData(nameof(Modes))]
    public void Foreign_query_provider_is_rejected_at_construction(
        string mode
    )
    {
        // Arrange
        var source = Array
            .Empty<LockRow>()
            .AsQueryable();

        // Act
        var exception = Record.Exception(() => Mark(source, mode));

        // Assert
        Assert.Contains("provider",
            Assert.IsType<InvalidOperationException>(exception)
                .Message,
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>An arbitrary async provider cannot discard the locking operator before Doka rejects the source.</summary>
    /// <param name="mode">The public locking mode.</param>
    [Theory]
    [MemberData(nameof(Modes))]
    public void Arbitrary_async_provider_is_rejected_before_it_can_discard_the_lock(
        string mode
    )
    {
        // Arrange
        var source = new ForeignAsyncSource();

        // WHY: The interface alone does not guarantee EF query translation; this provider
        // deliberately returns an ordinary unlocked query if CreateQuery is called.

        // Act
        var exception = Record.Exception(() => Mark(source, mode));

        // Assert
        Assert.Contains("provider",
            Assert.IsType<InvalidOperationException>(exception)
                .Message,
            StringComparison.OrdinalIgnoreCase);

        Assert.False(source.CreateQueryCalled);
    }

    /// <summary>Reports each forbidden relational scope before it can silently lose a locking marker.</summary>
    public static TheoryData<bool, string> RejectedShapes()
    {
        var cases = new TheoryData<bool, string>();

        string[] shapes =
        [
            "distinct-before", "distinct-after", "union-before", "union-after", "concat", "intersect", "except",
            "group", "derived", "marked-subquery", "collection-include", "split", "split-collection",
            "raw", "view", "keyless", "temporal", "materialized-before", "materialized-after", "materialized-nested",
        ];

        foreach (var mariaDb in s_dialects)
        {
            foreach (var shape in shapes)
            {
                cases.Add(mariaDb, shape);
            }
        }

        return cases;
    }

    /// <summary>Unsupported SQL scopes fail offline rather than producing incomplete or misplaced locking SQL.</summary>
    /// <param name="mariaDb">Whether the MariaDB dialect is configured.</param>
    /// <param name="shape">The rejected query shape.</param>
    [Theory]
    [MemberData(nameof(RejectedShapes))]
    public void Unsupported_query_shapes_are_rejected_before_database_io(
        bool mariaDb,
        string shape
    )
    {
        // Arrange
        var connections = new ConnectionAttempts();
        using var context = CreateContext(mariaDb, connections, temporal: shape == "temporal");
        var rows = context.Set<LockRow>();
        var other = rows.Where(row => row.Value > 0);

        // Act
        var exception = Record.Exception(() =>
        {
            _ = shape switch
            {
                "distinct-before" => rows
                    .Distinct()
                    .ForUpdate()
                    .ToQueryString(),
                "distinct-after" => rows
                    .ForUpdate()
                    .Distinct()
                    .ToQueryString(),
                "union-before" => rows
                    .Union(other)
                    .ForUpdate()
                    .ToQueryString(),
                "union-after" => rows
                    .ForUpdate()
                    .Union(other)
                    .ToQueryString(),
                "concat" => rows
                    .Concat(other)
                    .ForUpdate()
                    .ToQueryString(),
                "intersect" => rows
                    .Intersect(other)
                    .ForUpdate()
                    .ToQueryString(),
                "except" => rows
                    .Except(other)
                    .ForUpdate()
                    .ToQueryString(),
                "group" => rows
                    .ForUpdate()
                    .GroupBy(row => row.ParentId)
                    .Select(group => new LockProjection
                    {
                        Id = group.Key,
                        Count = group.Count(),
                    })
                    .ToQueryString(),
                "derived" => rows
                    .ForUpdate()
                    .OrderBy(row => row.Id)
                    .Take(2)
                    .Where(row => row.Value > 0)
                    .ToQueryString(),
                "marked-subquery" => rows
                    .Where(row => context
                        .Set<LockParent>()
                        .ForUpdate()
                        .Any(parent => parent.Id == row.ParentId))
                    .ToQueryString(),
                "collection-include" => context
                    .Set<LockParent>()
                    .Include(parent => parent.Children)
                    .ForUpdate()
                    .ToQueryString(),
                "split" => rows
                    .AsSplitQuery()
                    .ForUpdate()
                    .ToQueryString(),
                "split-collection" => context
                    .Set<LockParent>()
                    .Include(parent => parent.Children)
                    .AsSplitQuery()
                    .ForUpdate()
                    .ToQueryString(),
                "raw" => rows
                    .FromSqlRaw("SELECT * FROM `LockRows`")
                    .ForUpdate()
                    .ToQueryString(),
                "view" => context
                    .Set<LockView>()
                    .ForUpdate()
                    .ToQueryString(),
                "keyless" => context
                    .Set<KeylessLockRow>()
                    .ForUpdate()
                    .ToQueryString(),
                "temporal" => rows
                    .TemporalAsOf(new DateTime(2026,
                        1,
                        1,
                        0,
                        0,
                        0,
                        DateTimeKind.Utc))
                    .ForUpdate()
                    .ToQueryString(),
                "materialized-before" => rows
                    .WithMaterializedSource(shared => shared)
                    .ForUpdate()
                    .ToQueryString(),
                "materialized-after" => rows
                    .ForUpdate()
                    .WithMaterializedSource(shared => shared)
                    .ToQueryString(),
                "materialized-nested" => rows
                    .WithMaterializedSource(shared => shared.ForUpdate())
                    .ToQueryString(),
                _ => throw new ArgumentOutOfRangeException(nameof(shape)),
            };
        });

        // Assert
        Assert.Contains("lock",
            Assert.IsType<InvalidOperationException>(exception)
                .Message,
            StringComparison.OrdinalIgnoreCase);

        Assert.Equal(0, connections.Count);
    }

    /// <summary>Aggregates and mutation terminals reject during translation before connection acquisition.</summary>
    /// <param name="mariaDb">Whether the MariaDB dialect is configured.</param>
    /// <param name="terminal">The forbidden terminal operation.</param>
    [Theory]
    [MemberData(nameof(RejectedTerminals))]
    public async Task Unsupported_terminal_is_rejected_before_execution_or_transaction_validation(
        bool mariaDb,
        string terminal
    )
    {
        // Arrange
        var connections = new ConnectionAttempts();
        await using var context = CreateContext(mariaDb, connections);
        var query = context
            .Set<LockRow>()
            .ForUpdate();

        Func<Task> execute = terminal switch
        {
            "count" => () => query.CountAsync(CancellationToken.None),
            "long-count" => () => query.LongCountAsync(CancellationToken.None),
            "any" => () => query.AnyAsync(CancellationToken.None),
            "all" => () => query.AllAsync(row => row.Value > 0, CancellationToken.None),
            "sum" => () => query.SumAsync(row => row.Value, CancellationToken.None),
            "average" => () => query.AverageAsync(row => row.Value, CancellationToken.None),
            "min" => () => query.MinAsync(row => row.Value, CancellationToken.None),
            "max" => () => query.MaxAsync(row => row.Value, CancellationToken.None),
            "update" => ()
                => query.ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Value, 1),
                    CancellationToken.None),
            "delete" => () => query.ExecuteDeleteAsync(CancellationToken.None),
            _ => throw new ArgumentOutOfRangeException(nameof(terminal)),
        };

        // Act
        var exception = await Record.ExceptionAsync(execute);

        // Assert
        var rejected = Assert.IsType<InvalidOperationException>(exception);
        Assert.Contains("lock", rejected.Message, StringComparison.OrdinalIgnoreCase);

        // WHY: A missing-transaction error would conceal a lost marker or unsupported-shape check;
        // translation must reject the operation before the runtime transaction guard is relevant.
        Assert.DoesNotContain("transaction", rejected.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, connections.Count);
    }

    /// <summary>A known nontransactional table cannot advertise InnoDB row-lock semantics in any mode.</summary>
    /// <param name="mariaDb">Whether the MariaDB dialect is configured.</param>
    /// <param name="mode">The public locking mode.</param>
    [Theory]
    [MemberData(nameof(DialectModes))]
    public void Known_nontransactional_engine_is_rejected_for_every_mode(
        bool mariaDb,
        string mode
    )
    {
        // Arrange
        var connections = new ConnectionAttempts();
        using var context = CreateContext(mariaDb, connections, storageEngine: "MyISAM");

        // Act
        var exception = Record.Exception(() => Mark(context.Set<LockRow>(), mode)
            .ToQueryString());

        // Assert
        Assert.Contains("InnoDB",
            Assert.IsType<InvalidOperationException>(exception)
                .Message,
            StringComparison.OrdinalIgnoreCase);

        Assert.Equal(0, connections.Count);
    }

    /// <summary>The transactional-engine requirement also applies to another table directly joined into the locked block.</summary>
    /// <param name="mariaDb">Whether the MariaDB dialect is configured.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Joined_nontransactional_table_cannot_hide_behind_a_transactional_root(
        bool mariaDb
    )
    {
        // Arrange
        var connections = new ConnectionAttempts();
        using var context = CreateContext(mariaDb, connections, storageEngine: "MyISAM");
        var query = context
            .Set<LockParent>()
            .Join(context.Set<LockRow>(),
                parent => parent.Id,
                row => row.ParentId,
                (parent, row) => new LockProjection
                {
                    Id = row.Id,
                    ParentName = parent.Name,
                })
            .ForUpdate();

        // Act
        var exception = Record.Exception(() => query.ToQueryString());

        // Assert
        Assert.Contains("InnoDB",
            Assert.IsType<InvalidOperationException>(exception)
                .Message,
            StringComparison.OrdinalIgnoreCase);

        Assert.Equal(0, connections.Count);
    }

    /// <summary>The locking API does not extend the provider's configured supported-server policy.</summary>
    /// <param name="mariaDb">Whether the unsupported MariaDB rather than MySQL profile is used.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Unsupported_server_version_remains_rejected_before_io(
        bool mariaDb
    )
    {
        // Arrange
        var connections = new ConnectionAttempts();
        var version = mariaDb
            ? MySqlServerVersion.MariaDb(new Version(10, 5, 0))
            : MySqlServerVersion.MySql(new Version(8, 0, 0));

        // Act
        var exception = Record.Exception(() =>
        {
            using var context = CreateContext(mariaDb, connections, version: version);
            _ = context
                .Set<LockRow>()
                .ForUpdateSkipLocked()
                .ToQueryString();
        });

        // Assert
        Assert.IsType<NotSupportedException>(exception);
        Assert.Equal(0, connections.Count);
    }
}
