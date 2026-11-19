namespace Doka.EntityFrameworkCore.MySql.Tests;

public sealed partial class MySqlRecursiveDataReaderTests
{
    /// <summary>Command reuse copies the immutable policy rather than retaining values from another query.</summary>
    [Fact]
    public void PopulateFrom_copies_the_recursive_policy()
    {
        // Arrange
        using var scope = new CommandScope();
        var policy = Policy(["depth"]);
        var template = scope.Command(policy);
        var rented = scope.Command(null);

        // Act
        rented.PopulateFrom(template);

        // Assert
        Assert.Same(policy, rented.RecursiveQueryPolicy);
    }

    /// <summary>Both provider and ordinary templates clear recursive state on command-pool reuse.</summary>
    /// <param name="provider">Whether the incoming ordinary template is provider-owned.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PopulateFrom_clears_recursive_policy_for_ordinary_templates(
        bool provider
    )
    {
        // Arrange
        using var scope = new CommandScope();
        var command = scope.Command(Policy(["depth"]));
        var template = provider
            ? scope.Command(null)
            : new RelationalCommand(scope.Context.GetService<RelationalCommandBuilderDependencies>(),
                "SELECT ordinary",
                "SELECT ordinary",
                []);

        // Act
        command.PopulateFrom(template);

        // Assert
        Assert.Null(command.RecursiveQueryPolicy);
    }

    /// <summary>Command reuse copies privately generated recursive write-depth names with the direct-write policy.</summary>
    [Fact]
    public void PopulateFrom_copies_recursive_write_depth_parameters()
    {
        // Arrange
        using var scope = new CommandScope();
        var template = scope.Command(null);
        template.RecursiveWriteDepthParameters = new[] { "depth" };
        var rented = scope.Command(null);

        // Act
        rented.PopulateFrom(template);

        // Assert
        Assert.Equal("depth", Assert.Single(rented.RecursiveWriteDepthParameters.ToArray()));
        Assert.Equal(template.RecursiveWriteDepthParameters, rented.RecursiveWriteDepthParameters);
    }

    /// <summary>Ordinary provider and framework templates cannot inherit a previous recursive write-depth guard.</summary>
    /// <param name="provider">Whether the incoming ordinary template is provider-owned.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PopulateFrom_clears_recursive_write_depth_parameters_for_ordinary_templates(
        bool provider
    )
    {
        // Arrange
        using var scope = new CommandScope();
        var command = scope.Command(null);
        command.RecursiveWriteDepthParameters = new[] { "depth" };
        var template = provider
            ? scope.Command(null)
            : new RelationalCommand(scope.Context.GetService<RelationalCommandBuilderDependencies>(),
                "SELECT ordinary",
                "SELECT ordinary",
                []);

        // Act
        command.PopulateFrom(template);

        // Assert
        Assert.True(command.RecursiveWriteDepthParameters.IsEmpty);
    }

    /// <summary>A negative cached runtime value fails before opening or creating a native command.</summary>
    /// <param name="asynchronous">Whether to execute the asynchronous path.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cached_negative_depth_is_rejected_before_io(
        bool asynchronous
    )
    {
        // Arrange
        using var scope = new CommandScope();
        var command = scope.Command(Policy(["depth"]));
        var values = new Dictionary<string, object?> { ["depth"] = 1 };
        using (command.ExecuteReader(scope.Parameters(values, null))) { }

        values["depth"] = -1;
        var opened = scope.Database.OpenCalls;
        var created = scope.Database.CommandsCreated;
        var executed = scope.Database.ReaderExecutions;
        var parameters = scope.Parameters(values, null);
        using var cancellationSource = new CancellationTokenSource();
        var cancellationToken = cancellationSource.Token;

        // Act
        var exception = asynchronous
            ? await Record.ExceptionAsync(() => command.ExecuteReaderAsync(parameters, cancellationToken))
            : Record.Exception(() => command.ExecuteReader(parameters));

        // Assert
        Assert.IsType<ArgumentOutOfRangeException>(exception);
        Assert.Equal(opened, scope.Database.OpenCalls);
        Assert.Equal(created, scope.Database.CommandsCreated);
        Assert.Equal(executed, scope.Database.ReaderExecutions);
    }

    /// <summary>Streaming initializes only one guarded row and retains the real reader in native interception.</summary>
    /// <param name="asynchronous">Whether to execute asynchronously.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Streaming_primes_one_row_and_preserves_native_interception(
        bool asynchronous
    )
    {
        // Arrange
        var observer = new ReaderObserver();
        using var scope = new CommandScope(observer);
        var command = scope.Command(Policy([]));
        var parameters = scope.Parameters(null, null, CommandSource.Migrations);
        using var cancellationSource = new CancellationTokenSource();
        var cancellationToken = cancellationSource.Token;

        // Act
        await using var reader = asynchronous
            ? await command.ExecuteReaderAsync(parameters, cancellationToken)
            : command.ExecuteReader(parameters);

        // Assert
        Assert.NotNull(scope.Database.LastReader);
        Assert.Same(scope.Database.LastReader, observer.ExecutedReader);
        Assert.Equal(CommandSource.Migrations, observer.Source);
        Assert.IsType<MySqlRecursiveDataReader>(reader.DbDataReader);
        Assert.Equal(1, scope.Database.LastReader.ReadCalls);
        Assert.Equal(0, scope.Database.LastReader.PayloadReads);
    }

    /// <summary>Existing EF buffering remains active and consumes only visible payload columns.</summary>
    /// <param name="asynchronous">Whether to execute asynchronously.</param>
    /// <param name="empty">Whether the payload is represented by the synthetic empty row.</param>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Existing_buffering_runs_after_guard_validation(
        bool asynchronous,
        bool empty
    )
    {
        // Arrange
        using var scope = new CommandScope();
        if (empty)
        {
            scope.Database.ReaderFactory = static () => GuardRow(typeof(int), DBNull.Value, DBNull.Value, 0L);
        }

        var command = scope.Command(Policy([]));
        var columns = IntColumns();

        var parameters = scope.Parameters(null, columns);
        using var cancellationSource = new CancellationTokenSource();
        var cancellationToken = cancellationSource.Token;

        // Act
        await using var reader = asynchronous
            ? await command.ExecuteReaderAsync(parameters, cancellationToken)
            : command.ExecuteReader(parameters);

        // Assert
        Assert.NotNull(scope.Database.LastReader);
        Assert.True(scope.Database.LastReader.IsClosed);
        Assert.Equal(1, reader.DbDataReader.FieldCount);
        Assert.IsNotType<MySqlRecursiveDataReader>(reader.DbDataReader);
        Assert.Equal(!empty, reader.Read());
        Assert.Equal(empty
                ? 0
                : 2,
            scope.Database.LastReader.PayloadReads);
    }

    /// <summary>A guard failure releases the command and its successful open reference.</summary>
    /// <param name="asynchronous">Whether cleanup is asynchronous.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Invalid_budget_releases_its_reader_command_and_connection(
        bool asynchronous
    )
    {
        // Arrange
        using var scope = new CommandScope();
        scope.Database.ReaderFactory = static () => GuardRow(typeof(int), DBNull.Value, DBNull.Value, 1L);
        var command = scope.Command(Policy([]));
        var parameters = scope.Parameters(null, null);
        using var cancellationSource = new CancellationTokenSource();
        var cancellationToken = cancellationSource.Token;

        // Act
        var exception = asynchronous
            ? await Record.ExceptionAsync(() => command.ExecuteReaderAsync(parameters, cancellationToken))
            : Record.Exception(() => command.ExecuteReader(parameters));

        // Assert
        Assert.IsType<InvalidOperationException>(exception);
        Assert.NotNull(scope.Database.LastReader);
        Assert.True(scope.Database.LastReader.IsClosed);
        Assert.Equal(1, scope.Database.CommandsDisposed);
        Assert.Equal(1, scope.Database.CloseCalls);
        Assert.Equal(ConnectionState.Closed, scope.Database.State);
    }

    /// <summary>A failed raw-reader dispose cannot prevent attempts to dispose command and release the owned reference.</summary>
    /// <param name="asynchronous">Whether cleanup uses the asynchronous path.</param>
    /// <param name="buffered">Whether EF retry buffering owns the guarded input.</param>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Guard_failure_and_reader_disposal_failure_preserve_errors_and_attempt_all_cleanup(
        bool asynchronous,
        bool buffered
    )
    {
        // Arrange
        using var scope = new CommandScope();
        var cleanupFailure = new IOException("reader-dispose-canary");
        scope.Database.ReaderFactory = () =>
        {
            var raw = GuardRow(typeof(int), DBNull.Value, DBNull.Value, 1L);
            raw.DisposeFailure = cleanupFailure;
            raw.FailDisposeOnce = true;

            return raw;
        };

        var command = scope.Command(Policy([]));
        var parameters = scope.Parameters(null,
            buffered
                ? IntColumns()
                : null);

        using var cancellationSource = new CancellationTokenSource();
        var cancellationToken = cancellationSource.Token;

        // Act
        var exception = asynchronous
            ? await Record.ExceptionAsync(() => command.ExecuteReaderAsync(parameters, cancellationToken))
            : Record.Exception(() => command.ExecuteReader(parameters));

        // Assert
        var failures = Assert
            .IsType<AggregateException>(exception)
            .Flatten()
            .InnerExceptions;

        Assert.Contains(failures, failure => failure is InvalidOperationException);
        Assert.Contains(cleanupFailure, failures);
        Assert.Equal(1, scope.Database.CommandsDisposed);
        Assert.Equal(1, scope.Database.CloseCalls);
        Assert.Equal(ConnectionState.Closed, scope.Database.State);
    }

    // WHY: EF Core 10 marks the actual retry-buffer column constructor experimental;
    // exercising that same framework buffer is the regression contract, not an application API dependency.
#pragma warning disable EF9100
    private static ReaderColumn?[] IntColumns() =>
    [
        new ReaderColumn<int>(nullable: false, name: null, property: null, (reader, map) => reader.GetInt32(0)),
    ];
#pragma warning restore EF9100
}
