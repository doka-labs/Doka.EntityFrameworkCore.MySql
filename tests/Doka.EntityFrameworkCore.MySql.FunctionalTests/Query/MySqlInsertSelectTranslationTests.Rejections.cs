namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

public sealed partial class MySqlInsertSelectTranslationTests
{
    /// <summary>Reports each unsupported source independently for both selected dialects.</summary>
    public static TheoryData<bool, string> InvalidSourceCases
    {
        get
        {
            var cases = new TheoryData<bool, string>();

            foreach (var mariaDb in new[] { false, true })
            {
                foreach (var shape in new[]
                    {
                        "collection",
                        "collection-string",
                        "split",
                        "materialized",
                        "recursive-materialized",
                        "locking",
                    })
                {
                    cases.Add(mariaDb, shape);
                }
            }

            return cases;
        }
    }

    /// <summary>Reports independent malformed assignment contracts for both dialects.</summary>
    public static TheoryData<bool, string> InvalidAssignmentCases
    {
        get
        {
            var cases = new TheoryData<bool, string>();

            foreach (var mariaDb in new[] { false, true })
            {
                foreach (var assignment in new[]
                    {
                        "empty",
                        "duplicate",
                        "unknown",
                        "computed",
                        "missing",
                        "converter",
                        "changed-type",
                    })
                {
                    cases.Add(mariaDb, assignment);
                }
            }

            return cases;
        }
    }

    /// <summary>Reports callback/constructor cases separately rather than executing them as test preparation.</summary>
    public static TheoryData<string> ClientAssignmentCases => new()
    {
        "source-dependent",
        "source-independent",
        "constructor",
        "getter",
        "concat-object",
    };

    /// <summary>Projection cannot erase an incompatible loading or read-only source request.</summary>
    /// <param name="mariaDb">Whether translation uses the MariaDB dialect.</param>
    /// <param name="shape">The independently rejected source shape.</param>
    [Theory]
    [MemberData(nameof(InvalidSourceCases))]
    public async Task Unsupported_source_rejects_before_any_command_or_connection(
        bool mariaDb,
        string shape
    )
    {
        // Arrange
        var observation = new InsertObservation();
        await using var context = CreateContext(mariaDb, observation);
        var source = shape switch
        {
            "collection" => context
                .Sources
                .Include(row => row.Lookup)
                .ThenInclude(row => row.Rows),
            "collection-string" => context.Sources.Include("Lookup.Rows"),
            "split" => context.Sources.AsSplitQuery(),
            "materialized" => context.Sources.WithMaterializedSource(rows => rows.Where(row => row.Id > 0)),
            "locking" => context.Sources.ForUpdate(),
            "recursive-materialized" => context.Sources.WithRecursiveCte(frontier => frontier,
                rows => rows.WithMaterializedSource(shared => shared),
                3),
            _ => throw new ArgumentOutOfRangeException(nameof(shape)),
        };

        var cancellationToken = CancellationToken.None;
        var reason = shape switch
        {
            "collection" or "collection-string" => "collection Include",
            "split" => "split-query",
            "materialized" or "recursive-materialized" => "read-only materialized",
            "locking" => "locking-read",
            _ => throw new ArgumentOutOfRangeException(nameof(shape)),
        };

        // Act
        var actual = await Record.ExceptionAsync(() => source.ExecuteInsertFromQueryAsync(context.Targets,
            setters => setters
                .SetProperty(row => row.Name, row => row.Name)
                .SetProperty(row => row.Value, row => row.Value),
            cancellationToken));

        // Assert
        Assert.Contains(reason,
            Assert.IsType<InvalidOperationException>(actual)
                .Message,
            StringComparison.Ordinal);

        Assert.NotSame(observation.OpenFailure, actual);
        Assert.Equal(0, observation.OpenAttempts);
        Assert.Empty(observation.Commands);
    }

    /// <summary>Unknown, duplicate, generated and incompatible assignments never become a partial native write.</summary>
    /// <param name="mariaDb">Whether translation uses the MariaDB dialect.</param>
    /// <param name="assignment">The independently malformed assignment contract.</param>
    [Theory]
    [MemberData(nameof(InvalidAssignmentCases))]
    public async Task Invalid_assignments_reject_before_database_io(
        bool mariaDb,
        string assignment
    )
    {
        // Arrange
        var observation = new InsertObservation();
        await using var context = CreateContext(mariaDb, observation);
        Action<MySqlInsertSettersBuilder<InsertSource, InsertTarget>> setters = assignment switch
        {
            "empty" => _ => { },
            "duplicate" => builder => builder
                .SetProperty(row => row.Name, row => row.Name)
                .SetProperty(row => row.Name, _ => "other"),
            "unknown" => builder
                => builder.SetProperty(row => EF.Property<string>(row, "MissingColumn"), row => row.Name),
            "computed" => builder => builder.SetProperty(row => row.ComputedLength, row => row.Value),
            "missing" => builder => builder.SetProperty(row => row.Value, row => row.Value),
            "converter" => builder => builder
                .SetProperty(row => row.Name, row => row.Name)
                .SetProperty(row => row.Value, row => row.Value)
                .SetProperty(row => row.OtherStatus, row => row.Status),
            "changed-type" => builder => builder.SetProperty<object>(row => row.Name, row => row.Value),
            _ => throw new ArgumentOutOfRangeException(nameof(assignment)),
        };

        var cancellationToken = CancellationToken.None;
        var reason = assignment switch
        {
            "empty" => "at least one target assignment",
            "duplicate" => "more than once",
            "unknown" => "not a mapped scalar property",
            "computed" => "generated or read-only",
            "missing" => "requires a value for physical column",
            "converter" => "different CLR converter representations",
            "changed-type" => "cannot change the mapped property type",
            _ => throw new ArgumentOutOfRangeException(nameof(assignment)),
        };

        // Act
        var actual = await Record.ExceptionAsync(()
            => context.Sources.ExecuteInsertFromQueryAsync(context.Targets, setters, cancellationToken));

        // Assert
        Assert.Contains(reason,
            Assert.IsType<InvalidOperationException>(actual)
                .Message,
            StringComparison.Ordinal);

        Assert.NotSame(observation.OpenFailure, actual);
        Assert.Equal(0, observation.OpenAttempts);
        Assert.Empty(observation.Commands);
    }

    /// <summary>User code cannot select another TPH discriminator, even when other columns are valid.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Explicit_discriminator_assignment_is_rejected(
        bool mariaDb
    )
    {
        // Arrange
        var observation = new InsertObservation();
        await using var context = CreateContext(mariaDb, observation);
        var cancellationToken = CancellationToken.None;

        // Act
        var actual = await Record.ExceptionAsync(() => context.Sources.ExecuteInsertFromQueryAsync(
            context.DerivedTargets,
            setters => setters
                .SetProperty(row => row.Name, row => row.Name)
                .SetProperty(row => row.Detail, row => row.Value)
                .SetProperty(row => EF.Property<string>(row, "Kind"), _ => "base"),
            cancellationToken));

        // Assert
        var invalid = Assert.IsType<InvalidOperationException>(actual);
        Assert.Contains("discriminator", invalid.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, observation.OpenAttempts);
    }

    /// <summary>A different context remains invalid even when both contexts expose the same CLR/model shapes.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Foreign_context_target_is_rejected_before_io(
        bool mariaDb
    )
    {
        // Arrange
        var sourceObservation = new InsertObservation();
        var targetObservation = new InsertObservation();
        await using var sourceContext = CreateContext(mariaDb, sourceObservation);
        await using var targetContext = CreateContext(mariaDb, targetObservation);
        var cancellationToken = CancellationToken.None;

        // Act
        var actual = await Record.ExceptionAsync(() => sourceContext.Sources.ExecuteInsertFromQueryAsync(
            targetContext.Targets,
            setters => setters
                .SetProperty(row => row.Name, row => row.Name)
                .SetProperty(row => row.Value, row => row.Value),
            cancellationToken));

        // Assert
        Assert.IsType<InvalidOperationException>(actual);
        Assert.Equal(0, sourceObservation.OpenAttempts);
        Assert.Equal(0, targetObservation.OpenAttempts);
        Assert.Empty(sourceObservation.Commands);
        Assert.Empty(targetObservation.Commands);
    }

    /// <summary>An arbitrary query provider cannot turn the explicit write into an ordinary client query.</summary>
    [Fact]
    public async Task Foreign_query_provider_is_rejected_locally()
    {
        // Arrange
        var observation = new InsertObservation();
        await using var context = CreateContext(false, observation);
        var source = Array
            .Empty<InsertSource>()
            .AsQueryable();

        var cancellationToken = CancellationToken.None;

        // Act
        var actual = await Record.ExceptionAsync(() => source.ExecuteInsertFromQueryAsync(context.Targets,
            setters => setters
                .SetProperty(row => row.Name, row => row.Name)
                .SetProperty(row => row.Value, row => row.Value),
            cancellationToken));

        // Assert
        Assert.IsType<InvalidOperationException>(actual);
        Assert.Equal(0, observation.OpenAttempts);
        Assert.Empty(observation.Commands);
    }

    /// <summary>Cancellation precedes callback evaluation, model writes and connection acquisition.</summary>
    [Fact]
    public async Task Early_cancellation_does_not_invoke_the_assignment_callback()
    {
        // Arrange
        var observation = new InsertObservation();
        await using var context = CreateContext(false, observation);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var invoked = false;

        // Act
        var actual = await Record.ExceptionAsync(()
            => context.Sources.ExecuteInsertFromQueryAsync(context.Targets, _ => invoked = true, cancellation.Token));

        // Assert
        var canceled = Assert.IsAssignableFrom<OperationCanceledException>(actual);
        Assert.Equal(cancellation.Token, canceled.CancellationToken);
        Assert.False(invoked);
        Assert.Equal(0, observation.OpenAttempts);
        Assert.Empty(observation.Commands);
    }

    /// <summary>Neither SQL translation nor parameter extraction may invoke unsupported assignment callbacks.</summary>
    /// <param name="kind">The independently rejected callback shape.</param>
    [Theory]
    [MemberData(nameof(ClientAssignmentCases))]
    public async Task Unsupported_client_assignments_never_run_user_code(
        string kind
    )
    {
        // Arrange
        var observation = new InsertObservation();
        await using var context = CreateContext(false, observation);
        var callback = new ClientAssignmentCanary();
        Action<MySqlInsertSettersBuilder<InsertSource, InsertTarget>> setters = kind switch
        {
            "source-dependent" => builder => builder.SetProperty(row => row.Name, row => callback.Format(row.Name)),
            "source-independent" => builder => builder.SetProperty(row => row.Name, _ => callback.Format("constant")),
            "constructor" => builder => builder.SetProperty(row => row.Name, _ => new ClientConstructorCanary().Name),
            "getter" => builder => builder.SetProperty(row => row.Name, _ => callback.Name),
            "concat-object" => builder => builder.SetProperty(row => row.Name, _ => string.Concat(callback)),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

        var cancellationToken = CancellationToken.None;

        // Act
        var actual = await Record.ExceptionAsync(()
            => context.Sources.ExecuteInsertFromQueryAsync(context.Targets, setters, cancellationToken));

        // Assert
        Assert.IsType<InvalidOperationException>(actual);
        Assert.NotSame(observation.OpenFailure, actual);
        Assert.Equal(0, callback.Invocations);
        Assert.Equal(0, observation.OpenAttempts);
        Assert.Empty(observation.Commands);
    }

    private sealed class ClientAssignmentCanary
    {
        public int Invocations { get; private set; }
        public string Name => Format("getter");

        public string Format(
            string value
        )
        {
            Invocations++;

            throw new InvalidOperationException("Client callback was executed: " + value);
        }

        public override string ToString() => Format("ToString");
    }

    private sealed class ClientConstructorCanary
    {
        private readonly string _name = string.Empty;

        public ClientConstructorCanary()
        {
            throw new NotSupportedException("Client constructor was executed.");
        }

        public string Name => _name;
    }
}
