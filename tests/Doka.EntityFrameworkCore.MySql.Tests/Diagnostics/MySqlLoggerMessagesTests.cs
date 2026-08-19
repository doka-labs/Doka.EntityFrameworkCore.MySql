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
        var logger = new CapturingLogger { MinLevel = LogLevel.Warning };
        MySqlLoggerMessages.ServerVersionResolved(logger, MySqlServerVersion.MySql(new Version(8, 4, 0)));

        Assert.Empty(logger.Entries);
    }

    /// <summary>ServerVersionResolved emits a structured Information entry when enabled.</summary>
    [Fact]
    public void ServerVersionResolved_with_enabled_logger_emits_information_entry()
    {
        var logger = new CapturingLogger();
        MySqlLoggerMessages.ServerVersionResolved(logger, MySqlServerVersion.MySql(new Version(8, 4, 0)));

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Equal(MySqlEventId.ServerVersionResolved, entry.EventId);
        Assert.Contains("MySQL", entry.RenderedMessage);
    }

    /// <summary>ServerVersionResolved renders MariaDB engine label when given a MariaDB version.</summary>
    [Fact]
    public void ServerVersionResolved_for_mariadb_renders_mariadb_label()
    {
        var logger = new CapturingLogger();
        MySqlLoggerMessages.ServerVersionResolved(logger, MySqlServerVersion.MariaDb(new Version(11, 8, 0)));

        Assert.Contains("MariaDB", logger.Entries.Single().RenderedMessage);
    }

    // -- ServerVersionResolvedLogValues iteration (drives indexer + GetEnumerator) --

    /// <summary>The LogValues collection captured by the logger walks engine, version, and every capability entry.</summary>
    [Fact]
    public void ServerVersionResolved_log_values_iterate_engine_version_capabilities()
    {
        var logger = new CapturingLogger();
        MySqlLoggerMessages.ServerVersionResolved(logger, MySqlServerVersion.MySql(new Version(8, 4, 0)));

        var state = Assert.IsAssignableFrom<IReadOnlyList<KeyValuePair<string, object?>>>(logger.Entries.Single().State);

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

        var iterated = state.ToList();

        Assert.Equal(state.Count, iterated.Count);

        Assert.Throws<ArgumentOutOfRangeException>(() => state[state.Count]);
    }

    /// <summary>
    /// Verifies that an explicitly allowed legacy version emits a structured
    /// warning with its support classification.
    /// </summary>
    [Fact]
    public void UnsupportedServerVersion_emits_structured_warning()
    {
        var logger = new CapturingLogger();
        var serverVersion = MySqlServerVersion.MySql(
            new Version(8, 0, 44),
            MySqlServerVersionCompatibilityMode.AllowUnsupported);

        MySqlLoggerMessages.UnsupportedServerVersion(logger, serverVersion);

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
        var logger = new CapturingLogger();
        MySqlLoggerMessages.RetryAttempt(logger, attempt: 1, maxRetryCount: 3, delay: null, exception: new InvalidOperationException("test"));

        Assert.Single(logger.Entries);
    }

    /// <summary>RetryAttempt with explicit delay reports the millisecond value.</summary>
    [Fact]
    public void RetryAttempt_with_explicit_delay_emits_entry()
    {
        var logger = new CapturingLogger();
        MySqlLoggerMessages.RetryAttempt(
            logger,
            attempt: 2,
            maxRetryCount: 3,
            delay: TimeSpan.FromMilliseconds(250),
            exception: new InvalidOperationException("retry"));

        Assert.Single(logger.Entries);
    }

    /// <summary>
    /// Failure logs retain structured exception types without forwarding
    /// exception objects, messages, SQL, or raw migration lock names.
    /// </summary>
    [Fact]
    public void Failure_emitters_do_not_serialize_exception_payloads()
    {
        var logger = new CapturingLogger();
        var exception = new InvalidOperationException("password=secret;SELECT private_data");

        MySqlLoggerMessages.RetryAttempt(logger, 1, 3, TimeSpan.Zero, exception);
        MySqlLoggerMessages.RetryLimitExceeded(logger, 4, 3, exception);
        MySqlLoggerMessages.CommandTimeoutExhausted(logger, "Async", 30, "Broken", exception);
        MySqlLoggerMessages.CommitUnknown(logger, Guid.NewGuid(), "Broken", exception);
        MySqlLoggerMessages.MigrationLockTimeout(logger, "safe-scope-id", TimeSpan.FromSeconds(1), exception);
        MySqlLoggerMessages.MigrationLockAcquireFailed(
            logger,
            "safe-scope-id",
            TimeSpan.FromSeconds(1),
            exception);
        MySqlLoggerMessages.LockReleaseFailed(logger, "safe-scope-id", exception);

        Assert.All(logger.Entries, entry =>
        {
            Assert.Null(entry.Exception);
            Assert.DoesNotContain("password", entry.RenderedMessage, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("private_data", entry.RenderedMessage, StringComparison.Ordinal);
        });
        Assert.All(
            logger.Entries,
            entry => Assert.Contains("InvalidOperationException", entry.RenderedMessage, StringComparison.Ordinal));
    }

    [Fact]
    public void Migration_handler_failure_log_does_not_serialize_plugin_exception_payloads()
    {
        var logger = new CapturingLogger();
        var exception = new InvalidOperationException("password=secret;SELECT private_data");
        exception.Data["private-context"] = "tenant=private_tenant";

        MySqlLoggerMessages.MigrationOperationHandlerFailed(
            logger,
            "tests.handler",
            "Tests.CustomOperation",
            "default",
            0,
            MySqlMigrationHandlerFailureCode.HandlerFailed,
            exception);

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

    [Theory]
    [InlineData(MySqlMigrationHandlerFailureCode.InvalidHandlerResult)]
    [InlineData(MySqlMigrationHandlerFailureCode.UnknownOperationType)]
    [InlineData(MySqlMigrationHandlerFailureCode.RecursiveProviderRendering)]
    public void Migration_handler_contract_violation_log_preserves_its_reachable_failure_code(
        MySqlMigrationHandlerFailureCode failureCode
    )
    {
        var logger = new CapturingLogger();

        MySqlLoggerMessages.MigrationOperationHandlerContractViolation(
            logger,
            "tests.handler",
            "Tests.CustomOperation",
            "default",
            0,
            failureCode);

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(MySqlEventId.MigrationOperationHandlerContractViolation, entry.EventId);
        Assert.Contains("invocation contract", entry.RenderedMessage, StringComparison.Ordinal);
        Assert.Contains(failureCode.ToString(), entry.RenderedMessage, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that every object-bearing provider diagnostic replaces raw
    /// metadata with an opaque, structured scope identifier.
    /// </summary>
    [Fact]
    public void Object_diagnostics_do_not_serialize_raw_metadata_names()
    {
        const string secret = "DOKA_SECRET";

        var logger = new CapturingLogger();

        MySqlLoggerMessages.KeyOrIndexMaxLengthRequired(
            logger,
            $"{secret}_ENTITY",
            $"{secret}_PROPERTY",
            "text");
        MySqlLoggerMessages.ImplicitDecimalPrecisionDefaulted(
            logger,
            $"{secret}_ENTITY",
            $"{secret}_DECIMAL",
            18,
            2);
        MySqlLoggerMessages.MissingSpatialPackageDuringScaffolding(
            logger,
            $"{secret}_TABLE",
            $"{secret}_COLUMN");
        MySqlLoggerMessages.InvalidSpatialIndexConfiguration(
            logger,
            $"{secret}_INDEX",
            "must target exactly one property");
        MySqlLoggerMessages.ForeignKeyPrincipalTableNotScaffolded(
            logger,
            $"{secret}_FOREIGN_KEY",
            $"{secret}_TABLE",
            $"{secret}_PRINCIPAL_TABLE");

        Assert.Equal(5, logger.Entries.Count);

        Assert.All(logger.Entries, entry =>
        {
            Assert.DoesNotContain(secret, entry.RenderedMessage, StringComparison.Ordinal);

            var state = Assert.IsAssignableFrom<IReadOnlyList<KeyValuePair<string, object?>>>(entry.State);
            var scopeId = Assert.IsType<string>(
                Assert.Single(state, item => item.Key == "ObjectScopeId").Value);

            Assert.Matches("^[0-9a-f]{16}$", scopeId);
            Assert.DoesNotContain(
                state,
                item => item.Value?.ToString()?.Contains(secret, StringComparison.Ordinal) == true);
        });
    }

    /// <summary>
    /// Verifies that invalid-configuration events expose a bounded reason code
    /// rather than caller-provided diagnostic prose.
    /// </summary>
    [Fact]
    public void Invalid_configuration_diagnostic_uses_bounded_reason_code()
    {
        var logger = new CapturingLogger();

        MySqlLoggerMessages.InvalidConfiguration(
            logger,
            MySqlConfigurationFailureReason.IndexNameTooLong,
            "ModelValidation");

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
        var first = MySqlDiagnosticScopeId.Create("ab", "c");
        var second = MySqlDiagnosticScopeId.Create("a", "bc");

        Assert.NotEqual(first, second);
        Assert.Equal(first, MySqlDiagnosticScopeId.Create("ab", "c"));
    }

    /// <summary>
    /// Verifies that every scope-id overload rejects a missing logical
    /// component instead of hashing an ambiguous sentinel.
    /// </summary>
    [Fact]
    public void Diagnostic_scope_ids_reject_null_components()
    {
        Assert.Throws<ArgumentNullException>(() => MySqlDiagnosticScopeId.Create(null!));
        Assert.Throws<ArgumentNullException>(() => MySqlDiagnosticScopeId.Create(null!, "b"));
        Assert.Throws<ArgumentNullException>(() => MySqlDiagnosticScopeId.Create("a", null!));
        Assert.Throws<ArgumentNullException>(() => MySqlDiagnosticScopeId.Create(null!, "b", "c"));
        Assert.Throws<ArgumentNullException>(() => MySqlDiagnosticScopeId.Create("a", null!, "c"));
        Assert.Throws<ArgumentNullException>(() => MySqlDiagnosticScopeId.Create("a", "b", null!));
    }

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
