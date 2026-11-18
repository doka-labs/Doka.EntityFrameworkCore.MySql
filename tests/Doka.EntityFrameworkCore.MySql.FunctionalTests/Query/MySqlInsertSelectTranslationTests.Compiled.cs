namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

public sealed partial class MySqlInsertSelectTranslationTests
{
    /// <summary>The terminal does not execute an implicit retry strategy around a write with an uncertain outcome.</summary>
    [Fact]
    public async Task Insert_does_not_execute_a_configured_execution_strategy()
    {
        // Arrange
        var observation = new InsertObservation();
        await using var context = CreateContext(mariaDb: true, observation, forbidExecutionStrategy: true);
        var factory = Assert.IsType<ForbiddenExecutionStrategyFactory>(context.GetService<IExecutionStrategyFactory>());
        var cancellationToken = TestContext.Current.CancellationToken;

        // Act
        var actual = await Record.ExceptionAsync(() => context.Sources.ExecuteInsertFromQueryAsync(context.Targets,
            setters => setters
                .SetProperty(row => row.Name, row => row.Name)
                .SetProperty(row => row.Value, row => row.Value),
            cancellationToken));

        // Assert
        Assert.Same(observation.OpenFailure, actual);
        Assert.Equal(0, factory.ExecutionCount);
        Assert.Equal(1, observation.OpenAttempts);
        Assert.Single(observation.Commands);
    }

    /// <summary>One model/provider graph never reuses another target or positional assignment list.</summary>
    [Fact]
    public async Task Same_source_with_a_different_target_has_a_distinct_command_cache_identity()
    {
        // Arrange
        var observation = new InsertObservation();
        await using var context = CreateContext(mariaDb: true, observation);
        var source = context.Sources.Where(row => row.Id > 3);
        var cancellationToken = TestContext.Current.CancellationToken;

        // Act
        var first = await Record.ExceptionAsync(() => source.ExecuteInsertFromQueryAsync(context.Targets,
            setters => setters
                .SetProperty(row => row.Name, row => row.Name)
                .SetProperty(row => row.Value, row => row.Value),
            cancellationToken));

        var second = await Record.ExceptionAsync(() => source.ExecuteInsertFromQueryAsync(context.DerivedTargets,
            setters => setters
                .SetProperty(row => row.Name, row => row.Name)
                .SetProperty(row => row.Detail, row => row.Value),
            cancellationToken));

        // Assert
        Assert.Same(observation.OpenFailure, first);
        Assert.Same(observation.OpenFailure, second);
        Assert.Equal(2, observation.Commands.Count);
        Assert.StartsWith("INSERT INTO `insert``Target` (`Data``Name`, `payload value`)",
            observation.Commands[0].Sql,
            StringComparison.Ordinal);

        Assert.StartsWith("INSERT INTO `InsertHierarchy` (`Name`, `Detail`, `Kind`)",
            observation.Commands[1].Sql,
            StringComparison.Ordinal);

        Assert.NotEqual(observation.Commands[0].Sql, observation.Commands[1].Sql);
        Assert.Contains("'derived'", observation.Commands[1].Sql, StringComparison.Ordinal);
    }

    /// <summary>A cached asynchronous terminal reads the current context and current scalar parameters.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Compiled_insert_does_not_retain_the_first_context_or_parameter(
        bool mariaDb
    )
    {
        // Arrange
        var firstObservation = new InsertObservation();
        var secondObservation = new InsertObservation();
        await using var first = CreateContext(mariaDb, firstObservation);
        await using var second = CreateContext(mariaDb, secondObservation, first.Model);
        second.TenantId = 19;
        var compiled = EF.CompileQuery((InsertContext context, int minimum, CancellationToken token) =>
            context
                .Sources
                .Where(row => row.Id > minimum)
                .ExecuteInsertFromQueryAsync(context.Targets,
                    setters => setters
                        .SetProperty(row => row.Name, row => row.Name)
                        .SetProperty(row => row.Value, row => row.Value),
                    token));

        var cancellationToken = TestContext.Current.CancellationToken;

        // Act
        var firstFailure = await Record.ExceptionAsync(() => compiled(first, 11, cancellationToken));
        var secondFailure = await Record.ExceptionAsync(() => compiled(second, 23, cancellationToken));

        // Assert
        Assert.Same(firstObservation.OpenFailure, firstFailure);
        Assert.Same(secondObservation.OpenFailure, secondFailure);
        var firstCommand = Assert.Single(firstObservation.Commands);
        var secondCommand = Assert.Single(secondObservation.Commands);
        Assert.Equal(firstCommand.Sql, secondCommand.Sql);
        Assert.Contains(11, firstCommand.Parameters.Values);
        Assert.Contains(7, firstCommand.Parameters.Values);
        Assert.Contains(23, secondCommand.Parameters.Values);
        Assert.Contains(19, secondCommand.Parameters.Values);
        Assert.DoesNotContain(11, secondCommand.Parameters.Values);
        Assert.Empty(first.ChangeTracker.Entries());
        Assert.Empty(second.ChangeTracker.Entries());
    }

    /// <summary>A new canceled token is honored even after the compiled terminal has been initialized.</summary>
    [Fact]
    public async Task Compiled_insert_reads_the_current_cancellation_token()
    {
        // Arrange
        var observation = new InsertObservation();
        await using var context = CreateContext(mariaDb: true, observation);
        var compiled = EF.CompileQuery((InsertContext current, CancellationToken token) =>
            current.Sources.ExecuteInsertFromQueryAsync(current.Targets,
                setters => setters
                    .SetProperty(row => row.Name, row => row.Name)
                    .SetProperty(row => row.Value, row => row.Value),
                token));

        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        var cancellationToken = TestContext.Current.CancellationToken;

        // Act
        var initialized = await Record.ExceptionAsync(() => compiled(context, cancellationToken));
        var actual = await Record.ExceptionAsync(() => compiled(context, canceled.Token));

        // Assert
        Assert.Same(observation.OpenFailure, initialized);
        Assert.Equal(canceled.Token,
            Assert.IsAssignableFrom<OperationCanceledException>(actual)
                .CancellationToken);

        Assert.Equal(1, observation.OpenAttempts);
        Assert.Single(observation.Commands);
    }

    /// <summary>A shared model never authorizes inserting through another context's captured target set.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Compiled_insert_rejects_a_captured_foreign_target_before_io(
        bool mariaDb
    )
    {
        // Arrange
        var observation = new InsertObservation();
        var foreignObservation = new InsertObservation();
        await using var context = CreateContext(mariaDb, observation);
        await using var foreign = CreateContext(mariaDb, foreignObservation, context.Model);
        var foreignTarget = foreign.Targets;
        var compiled = EF.CompileQuery((InsertContext current, CancellationToken token) =>
            current.Sources.ExecuteInsertFromQueryAsync(foreignTarget,
                setters => setters
                    .SetProperty(row => row.Name, row => row.Name)
                    .SetProperty(row => row.Value, row => row.Value),
                token));

        var cancellationToken = TestContext.Current.CancellationToken;

        // Act
        var actual = await Record.ExceptionAsync(() => compiled(context, cancellationToken));

        // Assert
        Assert.IsType<InvalidOperationException>(actual);
        Assert.Equal(0, observation.OpenAttempts);
        Assert.Equal(0, foreignObservation.OpenAttempts);
        Assert.Empty(observation.Commands);
        Assert.Empty(foreignObservation.Commands);
    }

    /// <summary>Normal EF provider identity validation still visits a captured foreign source after target resolution.</summary>
    [Fact]
    public async Task Compiled_insert_rejects_a_captured_foreign_source_before_io()
    {
        // Arrange
        var observation = new InsertObservation();
        var foreignObservation = new InsertObservation();
        await using var context = CreateContext(mariaDb: true, observation);
        await using var foreign = CreateContext(mariaDb: true, foreignObservation, context.Model);
        var foreignSource = foreign.Sources;
        var compiled = EF.CompileQuery((InsertContext current, CancellationToken token) =>
            foreignSource.ExecuteInsertFromQueryAsync(current.Targets,
                setters => setters
                    .SetProperty(row => row.Name, row => row.Name)
                    .SetProperty(row => row.Value, row => row.Value),
                token));

        var cancellationToken = TestContext.Current.CancellationToken;

        // Act
        var actual = await Record.ExceptionAsync(() => compiled(context, cancellationToken));

        // Assert
        Assert.IsType<InvalidOperationException>(actual);
        Assert.Equal(0, observation.OpenAttempts);
        Assert.Equal(0, foreignObservation.OpenAttempts);
        Assert.Empty(observation.Commands);
        Assert.Empty(foreignObservation.Commands);
    }

    /// <summary>Normal compiled queries retain EF's existing synchronous scalar compiler behavior.</summary>
    [Fact]
    public void Ordinary_compiled_query_still_uses_the_standard_pipeline()
    {
        // Arrange
        var observation = new InsertObservation();
        using var context = CreateContext(mariaDb: true, observation);
        var compiled = EF.CompileQuery((InsertContext current) => current
            .Sources
            .Where(row => row.Id < 0)
            .Count());

        // Act
        // WHY: This control deliberately exercises EF's synchronous compiled-query entry point.
        // ReSharper disable once MethodHasAsyncOverload
        var actual = Record.Exception(() => compiled(context));

        // Assert
        Assert.Same(observation.OpenFailure, actual);
        Assert.Equal(1, observation.OpenAttempts);

        // WHY: EF's ordinary reader opens before initializing its command, unlike
        // our direct-write path. The controlled opening failure therefore sees no command.
        Assert.Empty(observation.Commands);
    }

    /// <summary>Fails deterministically if the write pipeline silently adds strategy-managed retries.</summary>
    private sealed class ForbiddenExecutionStrategyFactory : IExecutionStrategyFactory
    {
        /// <summary>Allows EF's normal scoped service activation in this test-only replacement.</summary>
        public ForbiddenExecutionStrategyFactory() { }

        internal int ExecutionCount { get; private set; }

        /// <inheritdoc />
        public IExecutionStrategy Create() => new ForbiddenStrategy(this);

        /// <summary>Allows normal EF service activation but rejects any hidden strategy execution.</summary>
        private sealed class ForbiddenStrategy : IExecutionStrategy
        {
            private readonly ForbiddenExecutionStrategyFactory _owner;

            /// <summary>Initializes the test helper with its supplied dependencies and values.</summary>
            public ForbiddenStrategy(
                ForbiddenExecutionStrategyFactory owner
            )
            {
                _owner = owner;
            }

            /// <inheritdoc />
            public bool RetriesOnFailure => true;

            /// <inheritdoc />
            public TResult Execute<TState, TResult>(
                TState state,
                Func<DbContext, TState, TResult> operation,
                Func<DbContext, TState, ExecutionResult<TResult>>? verifySucceeded
            )
            {
                _owner.ExecutionCount++;

                throw new InvalidOperationException("An INSERT SELECT must not execute an implicit retry strategy.");
            }

            /// <inheritdoc />
            public Task<TResult> ExecuteAsync<TState, TResult>(
                TState state,
                Func<DbContext, TState, CancellationToken, Task<TResult>> operation,
                Func<DbContext, TState, CancellationToken, Task<ExecutionResult<TResult>>>? verifySucceeded,
                CancellationToken cancellationToken = default
            )
            {
                _owner.ExecutionCount++;

                throw new InvalidOperationException("An INSERT SELECT must not execute an implicit retry strategy.");
            }
        }
    }
}
