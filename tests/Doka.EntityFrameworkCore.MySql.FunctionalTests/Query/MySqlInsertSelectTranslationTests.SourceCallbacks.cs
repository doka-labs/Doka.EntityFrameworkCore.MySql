namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

public sealed partial class MySqlInsertSelectTranslationTests
{
    /// <summary>The exact C#14 array view conversion preserves normal nonempty collection SQL.</summary>
    [Fact]
    public async Task Captured_array_contains_remains_valid_source_sql()
    {
        // Arrange
        var observation = new InsertObservation();
        await using var context = CreateContext(mariaDb: true, observation);
        var identifiers = new[] { 1, 2, 3 };
        var cancellationToken = TestContext.Current.CancellationToken;

        // Act
        var actual = await Record.ExceptionAsync(() => context
            .Sources
            .Where(row => identifiers.Contains(row.Id))
            .ExecuteInsertFromQueryAsync(context.Targets,
                setters => setters
                    .SetProperty(row => row.Name, value => value.Name)
                    .SetProperty(row => row.Value, value => value.Value),
                cancellationToken));

        // Assert
        Assert.Same(observation.OpenFailure, actual);
        var command = Assert.Single(observation.Commands);
        Assert.Contains("WHERE", command.Sql, StringComparison.Ordinal);
        Assert.Equal(1, observation.OpenAttempts);
    }

    /// <summary>Known empty-input framework factories remain ordinary SQL predicates without application callbacks.</summary>
    /// <param name="mariaDb">Whether SQL uses the MariaDB dialect.</param>
    /// <param name="enumerable">Whether the collection uses Enumerable.Empty instead of Array.Empty.</param>
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Empty_framework_collection_factories_remain_valid_source_inputs(
        bool mariaDb,
        bool enumerable
    )
    {
        // Arrange
        var observation = new InsertObservation();
        await using var context = CreateContext(mariaDb, observation);
        var cancellationToken = TestContext.Current.CancellationToken;
        var source = enumerable
            ? context.Sources.Where(row => Enumerable
                .Empty<int>()
                .Contains(row.Id))
            : context.Sources.Where(row => Array
                .Empty<int>()
                .Contains(row.Id));

        // Act
        var actual = await Record.ExceptionAsync(() => source.ExecuteInsertFromQueryAsync(context.Targets,
            setters => setters
                .SetProperty(row => row.Name, value => value.Name)
                .SetProperty(row => row.Value, value => value.Value),
            cancellationToken));

        // Assert
        Assert.Same(observation.OpenFailure, actual);
        var command = Assert.Single(observation.Commands);
        Assert.StartsWith("INSERT INTO", command.Sql, StringComparison.Ordinal);
        Assert.Contains("WHERE", command.Sql, StringComparison.Ordinal);
        Assert.Equal(1, observation.OpenAttempts);
    }

    /// <summary>Independent source projection and predicate callbacks must never run during compilation.</summary>
    /// <param name="kind">The independently forbidden application callback.</param>
    [Theory]
    [InlineData("constructor")]
    [InlineData("projection-method")]
    [InlineData("projection-getter")]
    [InlineData("predicate-method")]
    [InlineData("predicate-getter")]
    [InlineData("predicate-delegate")]
    [InlineData("array-method")]
    [InlineData("array-getter")]
    [InlineData("array-delegate")]
    public async Task Source_client_callbacks_are_rejected_without_invocation_or_io(
        string kind
    )
    {
        // Arrange
        var observation = new InsertObservation();
        await using var context = CreateContext(mariaDb: true, observation);
        var callback = new SourceCallbackCanary();
        var predicate = callback.Accept;
        var identifiers = callback.Identifiers;
        var cancellationToken = TestContext.Current.CancellationToken;
        Func<Task<int>> operation = kind switch
        {
            "constructor" => () => context
                .Sources
                .Select(_ => new SourceProjectionCanary(callback))
                .ExecuteInsertFromQueryAsync(context.Targets,
                    setters => setters
                        .SetProperty(row => row.Name, value => value.Name)
                        .SetProperty(row => row.Value, value => value.Value),
                    cancellationToken),
            "projection-method" => () => context
                .Sources
                .Select(_ => callback.Projection())
                .ExecuteInsertFromQueryAsync(context.Targets,
                    setters => setters
                        .SetProperty(row => row.Name, value => value.Name)
                        .SetProperty(row => row.Value, value => value.Value),
                    cancellationToken),
            "projection-getter" => () => context
                .Sources
                .Select(_ => callback.Projected)
                .ExecuteInsertFromQueryAsync(context.Targets,
                    setters => setters
                        .SetProperty(row => row.Name, value => value.Name)
                        .SetProperty(row => row.Value, value => value.Value),
                    cancellationToken),
            "predicate-method" => () => context
                .Sources
                .Where(_ => callback.Accept())
                .ExecuteInsertFromQueryAsync(context.Targets,
                    setters => setters
                        .SetProperty(row => row.Name, value => value.Name)
                        .SetProperty(row => row.Value, value => value.Value),
                    cancellationToken),
            "predicate-getter" => () => context
                .Sources
                .Where(_ => callback.Accepted)
                .ExecuteInsertFromQueryAsync(context.Targets,
                    setters => setters
                        .SetProperty(row => row.Name, value => value.Name)
                        .SetProperty(row => row.Value, value => value.Value),
                    cancellationToken),
            "predicate-delegate" => () => context
                .Sources
                .Where(_ => predicate())
                .ExecuteInsertFromQueryAsync(context.Targets,
                    setters => setters
                        .SetProperty(row => row.Name, value => value.Name)
                        .SetProperty(row => row.Value, value => value.Value),
                    cancellationToken),
            "array-method" => () => context
                .Sources
                .Where(row => callback
                    .Identifiers()
                    .Contains(row.Id))
                .ExecuteInsertFromQueryAsync(context.Targets,
                    setters => setters
                        .SetProperty(row => row.Name, value => value.Name)
                        .SetProperty(row => row.Value, value => value.Value),
                    cancellationToken),
            "array-getter" => () => context
                .Sources
                .Where(row => callback.IdValues.Contains(row.Id))
                .ExecuteInsertFromQueryAsync(context.Targets,
                    setters => setters
                        .SetProperty(row => row.Name, value => value.Name)
                        .SetProperty(row => row.Value, value => value.Value),
                    cancellationToken),
            "array-delegate" => () => context
                .Sources
                .Where(row => identifiers()
                    .Contains(row.Id))
                .ExecuteInsertFromQueryAsync(context.Targets,
                    setters => setters
                        .SetProperty(row => row.Name, value => value.Name)
                        .SetProperty(row => row.Value, value => value.Value),
                    cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

        // Act
        var actual = await Record.ExceptionAsync(operation);

        // Assert
        Assert.Contains("source lambdas cannot",
            Assert.IsType<InvalidOperationException>(actual)
                .Message,
            StringComparison.Ordinal);

        Assert.Equal(0, callback.Invocations);
        Assert.Equal(0, observation.OpenAttempts);
        Assert.Empty(observation.Commands);
    }

    /// <summary>Row-dependent DTO initialization remains a SQL projection and never invokes its application constructor.</summary>
    [Fact]
    public async Task Row_dependent_dto_and_captured_scalars_remain_server_projections()
    {
        // Arrange
        var observation = new InsertObservation();
        await using var context = CreateContext(mariaDb: true, observation);
        var minimum = 11;
        var increment = 2;
        var cancellationToken = TestContext.Current.CancellationToken;
        var source = context
            .Sources
            .Where(row => row.Id > minimum)
            .Select(row => new SqlSourceDto
            {
                Name = row.Name,
                Value = row.Value + increment,
            });

        // Act
        var actual = await Record.ExceptionAsync(() => source.ExecuteInsertFromQueryAsync(context.Targets,
            setters => setters
                .SetProperty(row => row.Name, value => value.Name)
                .SetProperty(row => row.Value, value => value.Value),
            cancellationToken));

        // Assert
        Assert.Same(observation.OpenFailure, actual);
        var command = Assert.Single(observation.Commands);
        Assert.Contains(11, command.Parameters.Values);
        Assert.Contains(2, command.Parameters.Values);
        Assert.Contains("INSERT INTO", command.Sql, StringComparison.Ordinal);
        Assert.Equal(1, observation.OpenAttempts);
    }

    /// <summary>Anonymous row projections and SQL-only fluent factories retain their registered server semantics.</summary>
    [Fact]
    public async Task Source_anonymous_projection_keeps_registered_window_markers()
    {
        // Arrange
        var observation = new InsertObservation();
        await using var context = CreateContext(mariaDb: true, observation);
        var cancellationToken = TestContext.Current.CancellationToken;
        var source = context.Sources.Select(row => new
        {
            row.Name,
            Value = (int)EF
                .Functions
                .Window()
                .OrderBy(row.Id)
                .RowNumber(),
        });

        // Act
        var actual = await Record.ExceptionAsync(() => source.ExecuteInsertFromQueryAsync(context.Targets,
            setters => setters
                .SetProperty(row => row.Name, value => value.Name)
                .SetProperty(row => row.Value, value => value.Value),
            cancellationToken));

        // Assert
        Assert.Same(observation.OpenFailure, actual);
        Assert.Contains("ROW_NUMBER() OVER",
            Assert.Single(observation.Commands)
                .Sql,
            StringComparison.Ordinal);

        Assert.Equal(1, observation.OpenAttempts);
    }

    private sealed class SourceCallbackCanary
    {
        internal int Invocations { get; private set; }
        public SourceProjectionCanary Projected => Projection();
        public bool Accepted => Accept();
        public int[] IdValues => Identifiers();

        public SourceProjectionCanary Projection() => new(this);

        public int[] Identifiers()
        {
            Accept();

            return [];
        }

        public bool Accept()
        {
            Invocations++;

            throw new NotSupportedException("A source application callback was invoked.");
        }
    }

    private sealed class SourceProjectionCanary
    {
        private readonly string _name = string.Empty;
        private readonly int _value;

        public SourceProjectionCanary(
            SourceCallbackCanary callback
        )
        {
            _value = callback.Accept()
                ? 1
                : 0;
        }

        public string Name => _name;
        public int Value => _value;
    }

    private sealed class SqlSourceDto
    {
        public SqlSourceDto()
        {
            throw new NotSupportedException("A SQL DTO projection must not invoke its constructor.");
        }

        public string Name { get; set; } = string.Empty;
        public int Value { get; set; }
    }
}
