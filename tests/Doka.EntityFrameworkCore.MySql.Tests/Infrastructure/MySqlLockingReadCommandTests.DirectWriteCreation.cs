namespace Doka.EntityFrameworkCore.MySql.Tests;

public sealed partial class MySqlLockingReadCommandTests
{
    /// <summary>Initialization failures dispose the adopted command before acquiring any connection reference.</summary>
    /// <param name="asynchronous">Whether the public execution entry point is asynchronous.</param>
    /// <param name="stage">The initialization stage that fails.</param>
    /// <param name="disposeFails">Whether native command disposal also fails.</param>
    [Theory]
    [InlineData(false, "binding", false)]
    [InlineData(false, "binding", true)]
    [InlineData(true, "binding", false)]
    [InlineData(true, "binding", true)]
    [InlineData(false, "created", false)]
    [InlineData(false, "created", true)]
    [InlineData(true, "created", false)]
    [InlineData(true, "created", true)]
    [InlineData(false, "initialized", false)]
    [InlineData(false, "initialized", true)]
    [InlineData(true, "initialized", false)]
    [InlineData(true, "initialized", true)]
    public async Task Direct_write_creation_failure_retains_command_ownership_and_both_causes(
        bool asynchronous,
        string stage,
        bool disposeFails
    )
    {
        // Arrange
        var interceptor = new CreationStageInterceptor(stage);
        using var scope = new CommandScope(interceptor);
        var disposal = new IOException("creation-disposal-canary");
        scope.Database.CommandDisposeFailure = disposeFails
            ? disposal
            : null;

        var parameter = new CreationStageParameter(interceptor);
        var command = new MySqlRelationalCommand(scope.Dependencies, "INSERT canary", "INSERT <redacted>", [parameter])
        {
            IsDirectWrite = true,
        };

        var parameters = CreationParameters(scope);
        using var cancellationSource = new CancellationTokenSource();
        var cancellationToken = cancellationSource.Token;

        // Act
        var actual = asynchronous
            ? await Record.ExceptionAsync(() => command.ExecuteNonQueryAsync(parameters, cancellationToken))
            : Record.Exception(() => command.ExecuteNonQuery(parameters));

        scope.Database.CommandDisposeFailure = null;

        // Assert
        if (disposeFails)
        {
            var failures = Assert
                .IsType<AggregateException>(actual)
                .Flatten()
                .InnerExceptions;

            Assert.Equal(2, failures.Count);
            Assert.Contains(interceptor.Failure, failures);
            Assert.Contains(disposal, failures);
        }
        else
        {
            Assert.Same(interceptor.Failure, actual);
        }

        Assert.Equal(1, scope.Database.CommandDisposeCount);
        Assert.Equal(0, scope.Database.OpenCount);
        Assert.Equal(0, scope.Database.CloseCount);
        Assert.Equal(0, scope.Database.NonQueryCount);
        Assert.Equal(ConnectionState.Closed, scope.Database.State);
        Assert.DoesNotContain("executing", interceptor.Events);
    }

    /// <summary>Failure before a command exists performs no native cleanup or connection acquisition.</summary>
    /// <param name="asynchronous">Whether the public execution entry point is asynchronous.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Direct_write_creating_callback_failure_has_no_command_to_dispose(
        bool asynchronous
    )
    {
        // Arrange
        var interceptor = new CreationStageInterceptor("creating");
        using var scope = new CommandScope(interceptor);
        var command = scope.Command(MySqlLockingReadMode.None);
        command.IsDirectWrite = true;
        var parameters = CreationParameters(scope);
        using var cancellationSource = new CancellationTokenSource();
        var cancellationToken = cancellationSource.Token;

        // Act
        var actual = asynchronous
            ? await Record.ExceptionAsync(() => command.ExecuteNonQueryAsync(parameters, cancellationToken))
            : Record.Exception(() => command.ExecuteNonQuery(parameters));

        // Assert
        Assert.Same(interceptor.Failure, actual);
        Assert.Equal(["creating"], interceptor.Events);
        Assert.Equal(0, scope.Database.CommandDisposeCount);
        Assert.Equal(0, scope.Database.OpenCount);
        Assert.Equal(0, scope.Database.CloseCount);
        Assert.Equal(0, scope.Database.NonQueryCount);
    }

    /// <summary>Suppression and replacement preserve EF callback order and execute the finally adopted command.</summary>
    /// <param name="asynchronous">Whether the public execution entry point is asynchronous.</param>
    /// <param name="replacementStage">The callback that supplies an alternate command, or none.</param>
    [Theory]
    [InlineData(false, "none")]
    [InlineData(true, "none")]
    [InlineData(false, "creating")]
    [InlineData(true, "creating")]
    [InlineData(false, "created")]
    [InlineData(true, "created")]
    [InlineData(false, "initialized")]
    [InlineData(true, "initialized")]
    public async Task Direct_write_creation_preserves_diagnostic_and_replacement_contracts(
        bool asynchronous,
        string replacementStage
    )
    {
        // Arrange
        var interceptor = new CreationStageInterceptor("none");
        using var scope = new CommandScope(interceptor);
        interceptor.ReplacementStage = replacementStage;
        interceptor.Replacement = replacementStage == "none"
            ? null
            : new RecordingCommand(scope.Database);

        var command = new MySqlRelationalCommand(scope.Dependencies,
            "INSERT canary",
            "INSERT <redacted>",
            [new CreationStageParameter(interceptor)]) { IsDirectWrite = true };

        var parameters = CreationParameters(scope);
        using var cancellationSource = new CancellationTokenSource();
        var cancellationToken = cancellationSource.Token;

        // Act
        var actual = asynchronous
            ? await command.ExecuteNonQueryAsync(parameters, cancellationToken)
            : command.ExecuteNonQuery(parameters);

        // Assert
        Assert.Equal(1, actual);
        Assert.Equal(["creating", "created", "binding", "initialized", "executing"], interceptor.Events);
        Assert.Same(interceptor.Replacement ?? interceptor.Created, interceptor.Executed);
        Assert.Equal("INSERT canary", interceptor.Executed?.CommandText);
        Assert.Equal(replacementStage is "created" or "initialized"
                ? 2
                : 1,
            scope.Database.CommandDisposeCount);

        Assert.Equal(1, scope.Database.NonQueryCount);
        Assert.Equal(1, scope.Database.OpenCount);
        Assert.Equal(1, scope.Database.CloseCount);
    }

    /// <summary>A creation failure cannot release a connection reference already held by the caller.</summary>
    /// <param name="asynchronous">Whether the public execution entry point is asynchronous.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Direct_write_creation_failure_preserves_borrowed_open_scope(
        bool asynchronous
    )
    {
        // Arrange
        var interceptor = new CreationStageInterceptor("initialized");
        using var scope = new CommandScope(interceptor);
        scope.Connection.Open();
        var command = scope.Command(MySqlLockingReadMode.None);
        command.IsDirectWrite = true;
        var parameters = CreationParameters(scope);
        using var cancellationSource = new CancellationTokenSource();
        var cancellationToken = cancellationSource.Token;

        // Act
        var actual = asynchronous
            ? await Record.ExceptionAsync(() => command.ExecuteNonQueryAsync(parameters, cancellationToken))
            : Record.Exception(() => command.ExecuteNonQuery(parameters));

        // Assert
        Assert.Same(interceptor.Failure, actual);
        Assert.Equal(1, scope.Database.OpenCount);
        Assert.Equal(0, scope.Database.CloseCount);
        Assert.Equal(ConnectionState.Open, scope.Database.State);
        Assert.Equal(1, scope.Database.CommandDisposeCount);
        Assert.Equal(0, scope.Database.NonQueryCount);
    }

    /// <summary>The ordinary command path keeps EF's native creation behavior without entering the opt-in helper.</summary>
    /// <param name="asynchronous">Whether the public execution entry point is asynchronous.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Ordinary_write_creation_behavior_remains_the_ef_baseline(
        bool asynchronous
    )
    {
        // Arrange
        var interceptor = new CreationStageInterceptor("created");
        using var scope = new CommandScope(interceptor);
        var command = scope.Command(MySqlLockingReadMode.None);
        var parameters = CreationParameters(scope);
        using var cancellationSource = new CancellationTokenSource();
        var cancellationToken = cancellationSource.Token;

        // Act
        var actual = asynchronous
            ? await Record.ExceptionAsync(() => command.ExecuteNonQueryAsync(parameters, cancellationToken))
            : Record.Exception(() => command.ExecuteNonQuery(parameters));

        // Assert
        // WHY: The new cleanup contract is explicitly opt-in. This pins EF Core 10's
        // existing ordinary-path behavior rather than silently expanding its scope.
        Assert.Same(interceptor.Failure, actual);
        Assert.Equal(0, scope.Database.CommandDisposeCount);
        Assert.Equal(0, scope.Database.OpenCount);
        Assert.Equal(0, scope.Database.CloseCount);
        interceptor.Created?.Dispose();
    }

    private static RelationalCommandParameterObject CreationParameters(
        CommandScope scope
    ) => new(scope.Connection,
        null,
        null,
        scope.Context,
        scope.Context.GetService<IRelationalCommandDiagnosticsLogger>());

    private sealed class CreationStageParameter : IRelationalParameter
    {
        private readonly CreationStageInterceptor _interceptor;

        /// <summary>Initializes the test helper with its supplied dependencies and values.</summary>
        public CreationStageParameter(
            CreationStageInterceptor interceptor
        )
        {
            _interceptor = interceptor;
        }

        public string InvariantName => "creation-canary";

        public void AddDbParameter(
            DbCommand command,
            object? value
        ) => Bind();

        public void AddDbParameter(
            DbCommand command,
            IReadOnlyDictionary<string, object?>? parameterValues
        ) => Bind();

        private void Bind()
        {
            _interceptor.Events.Add("binding");
            _interceptor.ThrowAt("binding");
        }
    }

    private sealed class CreationStageInterceptor : DbCommandInterceptor
    {
        private readonly string _failureStage;

        /// <summary>Initializes the interceptor with the behavior observed by this test.</summary>
        public CreationStageInterceptor(
            string failureStage
        )
        {
            _failureStage = failureStage;
        }

        internal IOException Failure { get; } = new("creation-stage-canary");
        internal List<string> Events { get; } = [];
        internal string ReplacementStage { get; set; } = "none";
        internal DbCommand? Replacement { get; set; }
        internal DbCommand? Created { get; private set; }
        internal DbCommand? Executed { get; private set; }

        internal void ThrowAt(
            string stage
        )
        {
            if (_failureStage == stage)
            {
                throw Failure;
            }
        }

        public override InterceptionResult<DbCommand> CommandCreating(
            CommandCorrelatedEventData eventData,
            InterceptionResult<DbCommand> result
        )
        {
            Events.Add("creating");
            ThrowAt("creating");

            return ReplacementStage == "creating" && Replacement is { } replacement
                ? InterceptionResult<DbCommand>.SuppressWithResult(replacement)
                : result;
        }

        public override DbCommand CommandCreated(
            CommandEndEventData eventData,
            DbCommand result
        )
        {
            Events.Add("created");
            Created = result;
            ThrowAt("created");

            return ReplaceAt("created", result);
        }

        public override DbCommand CommandInitialized(
            CommandEndEventData eventData,
            DbCommand result
        )
        {
            Events.Add("initialized");
            ThrowAt("initialized");

            return ReplaceAt("initialized", result);
        }

        public override InterceptionResult<int> NonQueryExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result
        )
        {
            Events.Add("executing");
            Executed = command;

            return result;
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default
        )
        {
            cancellationToken.ThrowIfCancellationRequested();

            // WHY: The callback only records in-memory state for both EF entry points.
            // ReSharper disable once MethodHasAsyncOverload
            return ValueTask.FromResult(NonQueryExecuting(command, eventData, result));
        }

        private DbCommand ReplaceAt(
            string stage,
            DbCommand command
        )
        {
            if (ReplacementStage != stage
                || Replacement is not { } replacement)
            {
                return command;
            }

            replacement.CommandText = command.CommandText;

            // WHY: As with EF's original callbacks, an interceptor that replaces a
            // successfully adopted command owns the superseded command's disposal.
            command.Dispose();

            return replacement;
        }
    }
}
