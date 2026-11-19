namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

public sealed partial class MySqlCteTranslationTests
{
    /// <summary>Ordinary CTE writes target the real mapped table and retain the bound source predicate.</summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Ordinary_cte_mutation_generates_the_qualified_physical_target_before_database_io(
        bool mariaDb,
        bool update
    )
    {
        // Arrange
        var capture = new CteCommandCapture();
        var opening = new CteConnectionOpeningCounter();
        var version = mariaDb
            ? MySqlServerVersion.MariaDb(new Version(12, 3, 3))
            : MySqlServerVersion.MySql(new Version(8, 4, 0));

        using var context = CreateMutationContext(version, capture, opening);
        var query = context
            .Rows
            .Where(row => row.Id > 2)
            .Select(row => row.TargetId)
            .WithCte(ids => context
                .Set<CteTarget>()
                .Where(target => ids.Contains(target.Id)));

        // Act
        var exception = Record.Exception(() => ExecuteCteMutation(query, update));

        // Assert
        Assert.IsType<CteCommandCapturedException>(exception);
        AssertOrdinary(capture.Sql!, 1);
        Assert.Contains(update
                ? "UPDATE `CteTargets`"
                : "FROM `CteTargets`",
            capture.Sql!,
            StringComparison.Ordinal);

        Assert.Contains("FROM `CteRows`", capture.Sql!, StringComparison.Ordinal);
        Assert.Contains(" IN (", capture.Sql!, StringComparison.Ordinal);
        Assert.Equal(0, opening.Attempts);
    }

    /// <summary>Earlier MariaDB grammar rejects CTE UPDATE and DELETE before command creation or connection opening.</summary>
    [Theory]
    [InlineData("10.11.19", false)]
    [InlineData("10.11.19", true)]
    [InlineData("11.4.9", false)]
    [InlineData("11.4.9", true)]
    [InlineData("11.8.9", false)]
    [InlineData("11.8.9", true)]
    [InlineData("12.3.0", false)]
    [InlineData("12.3.0", true)]
    public void Unsupported_cte_mutation_dialect_is_rejected_before_database_io(
        string version,
        bool update
    )
    {
        // Arrange
        var capture = new CteCommandCapture();
        var opening = new CteConnectionOpeningCounter();
        using var context = CreateMutationContext(MySqlServerVersion.MariaDb(Version.Parse(version)), capture, opening);
        var query = context
            .Rows
            .Select(row => row.TargetId)
            .WithCte(ids => context
                .Set<CteTarget>()
                .Where(target => ids.Contains(target.Id)));

        // Act
        var exception = Record.Exception(() => ExecuteCteMutation(query, update));

        // Assert
        Assert.Contains("CTE UPDATE or DELETE",
            Assert.IsType<InvalidOperationException>(exception)
                .Message,
            StringComparison.Ordinal);

        Assert.Null(capture.Sql);
        Assert.Equal(0, opening.Attempts);
    }

    /// <summary>Embedding the existing materialized fence inside an ordinary CTE cannot make it writable.</summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Materialized_source_nested_in_ordinary_cte_retains_the_mutation_guard(
        bool mariaDb,
        bool update
    )
    {
        // Arrange
        var capture = new CteCommandCapture();
        var opening = new CteConnectionOpeningCounter();
        var version = mariaDb
            ? MySqlServerVersion.MariaDb(new Version(12, 3, 3))
            : MySqlServerVersion.MySql(new Version(8, 4, 0));

        using var context = CreateMutationContext(version, capture, opening);
        var query = context
            .Rows
            .Select(row => row.TargetId)
            .WithMaterializedSource(shared => shared.WithCte(ids =>
                context
                    .Set<CteTarget>()
                    .Where(target => ids.Contains(target.Id))));

        // Act
        var exception = Record.Exception(() => ExecuteCteMutation(query, update));

        // Assert
        Assert.Contains("read queries only",
            Assert.IsType<InvalidOperationException>(exception)
                .Message,
            StringComparison.Ordinal);

        Assert.Null(capture.Sql);
        Assert.Equal(0, opening.Attempts);
    }

    /// <summary>Creates a real provider translation pipeline whose command initialization is intercepted before I/O.</summary>
    private static CteContext CreateMutationContext(
        MySqlServerVersion version,
        CteCommandCapture capture,
        CteConnectionOpeningCounter opening
    )
    {
        var options = MySqlFunctionalTestOptions
            .CreateTransientBuilder<CteContext>()
            .UseMySql("Server=localhost;Database=cte_mutation;User ID=unused;", version)
            .AddInterceptors(capture, opening)
            .Options;

        return new CteContext(options);
    }

    /// <summary>Exercises the synchronous command-construction boundary deliberately, without acquiring a connection.</summary>
    private static int ExecuteCteMutation(
        IQueryable<CteTarget> query,
        bool update
    )
    {
        if (update)
        {
            // WHY: This probe stops at synchronous CommandInitialized before any physical connection acquisition.
            // ReSharper disable once MethodHasAsyncOverload
            return query.ExecuteUpdate(setters => setters.SetProperty(row => row.IsVisible, false));
        }

        // WHY: This probe stops at synchronous CommandInitialized before any physical connection acquisition.
        // ReSharper disable once MethodHasAsyncOverload
        return query.ExecuteDelete();
    }

    /// <summary>Captures the actual generated command text, then prevents physical database execution.</summary>
    private sealed class CteCommandCapture : DbCommandInterceptor
    {
        /// <summary>Gets the exact command generated by the provider, if the query passed its guards.</summary>
        public string? Sql { get; private set; }

        /// <inheritdoc />
        public override DbCommand CommandInitialized(
            CommandEndEventData eventData,
            DbCommand result
        )
        {
            Sql = result.CommandText;

            throw new CteCommandCapturedException();
        }
    }

    /// <summary>Detects an unintended attempt to open a connection while testing translation-only commands.</summary>
    private sealed class CteConnectionOpeningCounter : DbConnectionInterceptor
    {
        private readonly bool _suppressOpen;

        /// <summary>Initializes the test helper with its supplied dependencies and values.</summary>
        public CteConnectionOpeningCounter(
            bool suppressOpen = false
        )
        {
            _suppressOpen = suppressOpen;
        }

        /// <summary>Gets the number of logical EF connection-opening events.</summary>
        public int Attempts { get; private set; }

        /// <summary>Gets how many logical opening events were suppressed before the native driver could open.</summary>
        public int SuppressedOpenings { get; private set; }

        /// <inheritdoc />
        public override InterceptionResult ConnectionOpening(
            DbConnection connection,
            ConnectionEventData eventData,
            InterceptionResult result
        )
        {
            Attempts++;

            if (_suppressOpen)
            {
                // WHY: EF's reader path opens before CommandInitialized, unlike its nonquery path.
                // Suppress only this fixture's native open so the actual scalar terminal stays offline.
                SuppressedOpenings++;

                return InterceptionResult.Suppress();
            }

            return result;
        }
    }

    /// <summary>Marks deliberate interception before the relational command opens its connection.</summary>
    private sealed class CteCommandCapturedException : Exception { }
}
