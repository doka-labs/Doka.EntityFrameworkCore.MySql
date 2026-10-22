namespace Doka.EntityFrameworkCore.MySql.Tests;

/// <summary>
/// Tests the structured diagnostic payloads and retry events emitted by
/// <c>MySqlLoggerMessages</c>.
/// </summary>
public sealed class MySqlLoggerMessagesTests
{
    // -- ServerVersionResolved disabled-logger early-return --

    /// <summary>ServerVersionResolved skips emission when the logger is disabled at Information.</summary>
    [Fact]
    public void ServerVersionResolved_with_disabled_logger_does_not_emit()
    {
        // Arrange
        var logger = new CapturingLogger { MinLevel = LogLevel.Warning };

        // Act
        MySqlLoggerMessages.ServerVersionResolved(logger, MySqlServerVersion.MySql(new Version(8, 4, 0)));

        // Assert
        Assert.Empty(logger.Entries);
    }

    /// <summary>ServerVersionResolved emits a structured Information entry when enabled.</summary>
    [Fact]
    public void ServerVersionResolved_with_enabled_logger_emits_information_entry()
    {
        // Arrange
        var logger = new CapturingLogger();

        // Act
        MySqlLoggerMessages.ServerVersionResolved(logger, MySqlServerVersion.MySql(new Version(8, 4, 0)));

        // Assert
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Equal(MySqlEventId.ServerVersionResolved, entry.EventId);
        Assert.Contains("MySQL", entry.RenderedMessage);
    }

    /// <summary>ServerVersionResolved renders MariaDB engine label when given a MariaDB version.</summary>
    [Fact]
    public void ServerVersionResolved_for_mariadb_renders_mariadb_label()
    {
        // Arrange
        var logger = new CapturingLogger();

        // Act
        MySqlLoggerMessages.ServerVersionResolved(logger, MySqlServerVersion.MariaDb(new Version(11, 8, 0)));
        var actual = logger.Entries.Single().RenderedMessage;

        // Assert
        Assert.Contains("MariaDB", actual);
    }

    // -- ServerVersionResolvedLogValues iteration (drives indexer + GetEnumerator) --

    /// <summary>The LogValues collection captured by the logger walks engine, version, and every capability entry.</summary>
    [Fact]
    public void ServerVersionResolved_log_values_iterate_engine_version_capabilities()
    {
        // Arrange
        var logger = new CapturingLogger();
        MySqlLoggerMessages.ServerVersionResolved(logger, MySqlServerVersion.MySql(new Version(8, 4, 0)));

        // Act
        var state = GetStructuredState(logger.Entries.Single());
        var iterated = state.ToList();

        // Assert
        Assert.Equal(4 + Enum.GetValues<ProviderCapability>().Length, state.Count);
        Assert.Equal("DatabaseEngine", state[0].Key);
        Assert.Equal("MySQL", state[0].Value);
        Assert.Equal("ServerVersion", state[1].Key);
        Assert.Equal("SupportStatus", state[2].Key);
        Assert.Equal(MySqlServerVersionSupportStatus.Supported.ToString(), state[2].Value);
        Assert.Equal("CompatibilityMode", state[3].Key);
        Assert.Equal(
            Enum.GetNames<ProviderCapability>(),
            state.Skip(4).Select(entry => entry.Key));
        Assert.Equal(state.Count, iterated.Count);
    }

    /// <summary>Verifies structured log values reject an index beyond their bounded fields.</summary>
    [Fact]
    public void ServerVersionResolved_log_values_reject_out_of_range_index()
    {
        // Arrange
        var logger = new CapturingLogger();
        MySqlLoggerMessages.ServerVersionResolved(logger, MySqlServerVersion.MySql(new Version(8, 4, 0)));
        var state = GetStructuredState(logger.Entries.Single());

        // Act
        var recordedException = Record.Exception(() => state[state.Count]);

        // Assert
        Assert.IsType<ArgumentOutOfRangeException>(recordedException);
    }

    /// <summary>
    /// Verifies that an explicitly allowed legacy version emits a structured
    /// warning with its support classification.
    /// </summary>
    [Fact]
    public void UnsupportedServerVersion_emits_structured_warning()
    {
        // Arrange
        var logger = new CapturingLogger();
        var serverVersion = MySqlServerVersion.MySql(
            new Version(8, 0, 44),
            MySqlServerVersionCompatibilityMode.AllowUnsupported);

        // Act
        MySqlLoggerMessages.UnsupportedServerVersion(logger, serverVersion);

        // Assert
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Equal(MySqlEventId.UnsupportedServerVersion, entry.EventId);
        Assert.Contains(MySqlServerVersionSupportStatus.Legacy.ToString(), entry.RenderedMessage);
        Assert.Contains(ServerVersionSupportPolicy.SupportedMatrix, entry.RenderedMessage);
    }

    // -- RetryAttempt null-delay path --

    /// <summary>RetryAttempt with null delay reports zero milliseconds without throwing.</summary>
    [Fact]
    public void RetryAttempt_with_null_delay_reports_zero_milliseconds()
    {
        // Arrange
        var logger = new CapturingLogger();

        // Act
        MySqlLoggerMessages.RetryAttempt(
            logger,
            attempt: 1,
            maxRetryCount: 3,
            delay: null,
            exception: new InvalidOperationException("test"));

        // Assert
        var entry = Assert.Single(logger.Entries);
        var state = Assert.IsAssignableFrom<IReadOnlyList<KeyValuePair<string, object?>>>(entry.State);

        Assert.Equal(MySqlEventId.RetryAttempt, entry.EventId);
        Assert.Equal(0d, Assert.IsType<double>(Assert.Single(state, field => field.Key == "DelayMs").Value));
    }

    /// <summary>RetryAttempt with explicit delay reports the millisecond value.</summary>
    [Fact]
    public void RetryAttempt_with_explicit_delay_emits_entry()
    {
        // Arrange
        var logger = new CapturingLogger();

        // Act
        MySqlLoggerMessages.RetryAttempt(
            logger,
            attempt: 2,
            maxRetryCount: 3,
            delay: TimeSpan.FromMilliseconds(250),
            exception: new InvalidOperationException("retry"));

        // Assert
        var entry = Assert.Single(logger.Entries);
        var state = Assert.IsAssignableFrom<IReadOnlyList<KeyValuePair<string, object?>>>(entry.State);

        Assert.Equal(MySqlEventId.RetryAttempt, entry.EventId);
        Assert.Equal(250d, Assert.IsType<double>(Assert.Single(state, field => field.Key == "DelayMs").Value));
    }

    /// <summary>
    /// Failure logs retain structured exception types without forwarding
    /// exception objects, messages, SQL, or raw migration lock names.
    /// </summary>
    /// <param name="emitter">The independently exercised failure emitter.</param>
    [Theory]
    [InlineData("retry_attempt")]
    [InlineData("retry_exhausted")]
    [InlineData("command_timeout")]
    [InlineData("commit_unknown")]
    [InlineData("lock_timeout")]
    [InlineData("lock_acquire")]
    [InlineData("lock_release")]
    public void Failure_emitters_do_not_serialize_exception_payloads(
        string emitter
    )
    {
        // Arrange
        var logger = new CapturingLogger();
        var exception = new InvalidOperationException("password=secret;SELECT private_data");
        Action emit = emitter switch
        {
            "retry_attempt" => () => MySqlLoggerMessages.RetryAttempt(logger, 1, 3, TimeSpan.Zero, exception),
            "retry_exhausted" => () => MySqlLoggerMessages.RetryLimitExceeded(logger, 4, 3, exception),
            "command_timeout" => () => MySqlLoggerMessages.CommandTimeoutExhausted(
                logger, "Async", 30, "Broken", exception),
            "commit_unknown" => () => MySqlLoggerMessages.CommitUnknown(logger, Guid.NewGuid(), "Broken", exception),
            "lock_timeout" => () => MySqlLoggerMessages.MigrationLockTimeout(
                logger, "safe-scope-id", TimeSpan.FromSeconds(1), exception),
            "lock_acquire" => () => MySqlLoggerMessages.MigrationLockAcquireFailed(
                logger, "safe-scope-id", TimeSpan.FromSeconds(1), exception),
            "lock_release" => () => MySqlLoggerMessages.LockReleaseFailed(logger, "safe-scope-id", exception),
            _ => throw new ArgumentOutOfRangeException(nameof(emitter)),
        };

        // Act
        emit();

        // Assert
        var entry = Assert.Single(logger.Entries);

        Assert.Null(entry.Exception);
        Assert.DoesNotContain("password", entry.RenderedMessage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("private_data", entry.RenderedMessage, StringComparison.Ordinal);
        Assert.Contains("InvalidOperationException", entry.RenderedMessage, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies migration handler failure log does not serialize plugin exception payloads.
    /// </summary>
    [Fact]
    public void Migration_handler_failure_log_does_not_serialize_plugin_exception_payloads()
    {
        // Arrange
        var logger = new CapturingLogger();
        var exception = new InvalidOperationException("password=secret;SELECT private_data");
        exception.Data["private-context"] = "tenant=private_tenant";

        // Act
        MySqlLoggerMessages.MigrationOperationHandlerFailed(
            logger,
            "tests.handler",
            "Tests.CustomOperation",
            "default",
            0,
            MySqlMigrationHandlerFailureCode.HandlerFailed,
            exception);

        // Assert
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(MySqlEventId.MigrationOperationHandlerFailed, entry.EventId);
        Assert.Null(entry.Exception);
        Assert.Contains("InvalidOperationException", entry.RenderedMessage, StringComparison.Ordinal);
        Assert.Contains("HandlerFailed", entry.RenderedMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("password", entry.RenderedMessage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("private_data", entry.RenderedMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("private-context", entry.RenderedMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("private_tenant", entry.RenderedMessage, StringComparison.Ordinal);
        var state = Assert.IsType<IReadOnlyList<KeyValuePair<string, object?>>>(entry.State, exactMatch: false);
        Assert.DoesNotContain(
            state,
            field => field.Value?.ToString()?.Contains("private_tenant", StringComparison.Ordinal) == true);
    }

    /// <summary>
    /// Verifies migration handler contract violation log preserves its reachable failure code.
    /// </summary>
    /// <param name="failureCode">The reachable handler contract failure.</param>
    [Theory]
    [InlineData(MySqlMigrationHandlerFailureCode.InvalidHandlerResult)]
    [InlineData(MySqlMigrationHandlerFailureCode.UnknownOperationType)]
    [InlineData(MySqlMigrationHandlerFailureCode.RecursiveProviderRendering)]
    public void Migration_handler_contract_violation_log_preserves_its_reachable_failure_code(
        MySqlMigrationHandlerFailureCode failureCode
    )
    {
        // Arrange
        var logger = new CapturingLogger();

        // Act
        MySqlLoggerMessages.MigrationOperationHandlerContractViolation(
            logger,
            "tests.handler",
            "Tests.CustomOperation",
            "default",
            0,
            failureCode);

        // Assert
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(MySqlEventId.MigrationOperationHandlerContractViolation, entry.EventId);
        Assert.Contains("invocation contract", entry.RenderedMessage, StringComparison.Ordinal);
        Assert.Contains(failureCode.ToString(), entry.RenderedMessage, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that every object-bearing provider diagnostic replaces raw
    /// metadata with an opaque, structured scope identifier.
    /// </summary>
    /// <param name="emitter">The independently exercised metadata diagnostic.</param>
    [Theory]
    [InlineData("key_length")]
    [InlineData("decimal_precision")]
    [InlineData("spatial_package")]
    [InlineData("spatial_index")]
    [InlineData("foreign_key")]
    public void Object_diagnostics_do_not_serialize_raw_metadata_names(
        string emitter
    )
    {
        // Arrange
        const string secret = "DOKA_SECRET";
        var logger = new CapturingLogger();
        Action emit = emitter switch
        {
            "key_length" => () => MySqlLoggerMessages.KeyOrIndexMaxLengthRequired(
                logger, $"{secret}_ENTITY", $"{secret}_PROPERTY", "text"),
            "decimal_precision" => () => MySqlLoggerMessages.ImplicitDecimalPrecisionDefaulted(
                logger, $"{secret}_ENTITY", $"{secret}_DECIMAL", 18, 2),
            "spatial_package" => () => MySqlLoggerMessages.MissingSpatialPackageDuringScaffolding(
                logger, $"{secret}_TABLE", $"{secret}_COLUMN"),
            "spatial_index" => () => MySqlLoggerMessages.InvalidSpatialIndexConfiguration(
                logger, $"{secret}_INDEX", "must target exactly one property"),
            "foreign_key" => () => MySqlLoggerMessages.ForeignKeyPrincipalTableNotScaffolded(
                logger, $"{secret}_FOREIGN_KEY", $"{secret}_TABLE", $"{secret}_PRINCIPAL_TABLE"),
            _ => throw new ArgumentOutOfRangeException(nameof(emitter)),
        };

        // Act
        emit();

        // Assert
        var entry = Assert.Single(logger.Entries);

        Assert.DoesNotContain(secret, entry.RenderedMessage, StringComparison.Ordinal);

        var state = Assert.IsAssignableFrom<IReadOnlyList<KeyValuePair<string, object?>>>(entry.State);
        var scopeId = Assert.IsType<string>(
            Assert.Single(state, item => item.Key == "ObjectScopeId").Value);

        Assert.Matches("^[0-9a-f]{16}$", scopeId);
        Assert.DoesNotContain(
            state,
            item => item.Value?.ToString()?.Contains(secret, StringComparison.Ordinal) == true);
    }

    /// <summary>
    /// Verifies that invalid-configuration events expose a bounded reason code
    /// rather than caller-provided diagnostic prose.
    /// </summary>
    [Fact]
    public void Invalid_configuration_diagnostic_uses_bounded_reason_code()
    {
        // Arrange
        var logger = new CapturingLogger();

        // Act
        MySqlLoggerMessages.InvalidConfiguration(
            logger,
            MySqlConfigurationFailureReason.IndexNameTooLong,
            "ModelValidation");

        // Assert
        var entry = Assert.Single(logger.Entries);
        var state = Assert.IsType<IReadOnlyList<KeyValuePair<string, object?>>>(entry.State, exactMatch: false);
        Assert.Equal(
            MySqlConfigurationFailureReason.IndexNameTooLong,
            Assert.Single(state, item => item.Key == "Reason").Value);
        Assert.DoesNotContain(state, item => item.Key == "Message");
        Assert.DoesNotContain(state, item => item.Key == "RedactedConnectionString");
    }

    /// <summary>
    /// Verifies that length-prefix framing prevents ambiguous component tuples
    /// from receiving the same diagnostic scope identifier.
    /// </summary>
    [Fact]
    public void Diagnostic_scope_ids_preserve_component_boundaries()
    {
        // Arrange
        var first = MySqlDiagnosticScopeId.Create("ab", "c");

        // Act
        var second = MySqlDiagnosticScopeId.Create("a", "bc");

        // Assert
        Assert.NotEqual(first, second);
    }

    /// <summary>Verifies the same ordered component tuple produces the same scope identifier.</summary>
    [Fact]
    public void Diagnostic_scope_ids_are_stable_for_identical_components()
    {
        // Arrange
        var first = MySqlDiagnosticScopeId.Create("ab", "c");

        // Act
        var repeated = MySqlDiagnosticScopeId.Create("ab", "c");

        // Assert
        Assert.Equal(first, repeated);
    }

    /// <summary>
    /// Verifies that every scope-id overload rejects a missing logical
    /// component instead of hashing an ambiguous sentinel.
    /// </summary>
    /// <param name="arity">The number of components in the scope-id overload.</param>
    /// <param name="nullPosition">The zero-based position of the missing component.</param>
    [Theory]
    [InlineData(1, 0)]
    [InlineData(2, 0)]
    [InlineData(2, 1)]
    [InlineData(3, 0)]
    [InlineData(3, 1)]
    [InlineData(3, 2)]
    public void Diagnostic_scope_ids_reject_null_components(
        int arity,
        int nullPosition
    )
    {
        // Arrange
        Action action = (arity, nullPosition) switch
        {
            (1, 0) => () => MySqlDiagnosticScopeId.Create(null!),
            (2, 0) => () => MySqlDiagnosticScopeId.Create(null!, "b"),
            (2, 1) => () => MySqlDiagnosticScopeId.Create("a", null!),
            (3, 0) => () => MySqlDiagnosticScopeId.Create(null!, "b", "c"),
            (3, 1) => () => MySqlDiagnosticScopeId.Create("a", null!, "c"),
            (3, 2) => () => MySqlDiagnosticScopeId.Create("a", "b", null!),
            _ => throw new ArgumentOutOfRangeException(nameof(nullPosition)),
        };

        // Act
        var recordedException = Record.Exception(action);

        // Assert
        Assert.IsType<ArgumentNullException>(recordedException);
    }

    /// <summary>Reads the structured fixture state while preserving a non-null interface type.</summary>
    /// <param name="entry">The captured log entry prepared for collection tests.</param>
    /// <returns>The structured log fields.</returns>
    private static IReadOnlyList<KeyValuePair<string, object?>> GetStructuredState(
        LogEntry entry
    ) => entry.State as IReadOnlyList<KeyValuePair<string, object?>>
        ?? throw new InvalidOperationException("The log fixture did not capture structured fields.");

    private sealed record LogEntry(
        LogLevel Level,
        EventId EventId,
        string RenderedMessage,
        object? State,
        Exception? Exception
    );

    private sealed class CapturingLogger : ILogger
    {
        public LogLevel MinLevel { get; init; } = LogLevel.Trace;

        public List<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= MinLevel;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Entries.Add(new LogEntry(logLevel, eventId, formatter(state, exception), state, exception));
    }
}
