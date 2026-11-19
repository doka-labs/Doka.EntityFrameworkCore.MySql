namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>Qualifies process metadata against real blocked commands with adversarial parameter representations.</summary>
[IntegrationDatabaseTest]
[Trait("VerificationLane", "FullIntegration")]
public sealed class MySqlNativeProcessMetadataIntegrationTests
{
    /// <summary>Identifies actual wire parameter values, rather than simulated process metadata.</summary>
    public enum PayloadKind
    {
        /// <summary>Uses a binary parameter containing only ASCII bytes.</summary>
        AsciiBytes,

        /// <summary>Uses binary bytes that cannot be interpreted as UTF-8 text.</summary>
        InvalidUtf8Bytes,

        /// <summary>Uses a text parameter containing a supplementary Unicode character.</summary>
        NonBmpText,
    }

    /// <summary>Identifies the independently executed native observation shapes.</summary>
    public enum ProbeKind
    {
        /// <summary>Counts only the exact blocked session and marked statement.</summary>
        Count,

        /// <summary>Reads the exact blocked session's byte-preserving normalized statement.</summary>
        Statement,

        /// <summary>Executes the shared active-statement diagnostic command.</summary>
        Diagnostic,
    }

    private readonly ITestOutputHelper _output;

    /// <summary>Retains exact session and native statement observations for each independent case.</summary>
    /// <param name="output">The output owned by the current test case.</param>
    public MySqlNativeProcessMetadataIntegrationTests(
        ITestOutputHelper output
    )
    {
        _output = output;
    }

    /// <summary>Reports all three query shapes and payloads independently on each selected supported engine.</summary>
    public static TheoryData<string, ProbeKind, PayloadKind> PositiveCases
    {
        get
        {
            var cases = new TheoryData<string, ProbeKind, PayloadKind>();

            foreach (var target in SelectedTargets())
            {
                foreach (var kind in Enum.GetValues<ProbeKind>())
                {
                    cases.Add(target, kind, PayloadKind.AsciiBytes);
                    cases.Add(target, kind, PayloadKind.InvalidUtf8Bytes);
                    cases.Add(target, kind, PayloadKind.NonBmpText);
                }
            }

            return cases;
        }
    }

    /// <summary>Reports unrelated-session or unrelated-marker controls for every selected query shape.</summary>
    public static TheoryData<string, ProbeKind> NegativeCases
    {
        get
        {
            var cases = new TheoryData<string, ProbeKind>();

            foreach (var target in SelectedTargets())
            {
                foreach (var kind in Enum.GetValues<ProbeKind>())
                {
                    cases.Add(target, kind);
                }
            }

            return cases;
        }
    }

    /// <summary>Observes the exact native wait even when the active command carries binary or non-BMP data.</summary>
    /// <param name="targetName">The selected live engine.</param>
    /// <param name="kind">The one observation shape executed by this case.</param>
    /// <param name="payloadKind">The real parameter representation carried by the blocked command.</param>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(PositiveCases))]
    public async Task Selected_metadata_query_observes_the_exact_blocked_worker(
        string targetName,
        ProbeKind kind,
        PayloadKind payloadKind
    )
    {
        // Arrange
        using var safety = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        await using var probe = await NativeProbe.CreateAsync(targetName, CreatePayload(payloadKind), safety.Token);
        Exception? primary = null;
        object? actual;

        try
        {
            WriteObservations(targetName, kind, payloadKind, probe);

            // Act
            actual = await ObserveAsync(probe, kind, matchesWorker: true, safety.Token);
        }
        catch (Exception failure)
        {
            primary = failure;

            throw;
        }
        finally
        {
            await probe.CompleteAsync(primary);
        }

        // Assert
        if (kind == ProbeKind.Count)
        {
            Assert.Equal(1L, Convert.ToInt64(actual, CultureInfo.InvariantCulture));
        }
        else
        {
            var statement = Assert.IsType<string>(actual);
            Assert.Contains(probe.Marker, statement, StringComparison.Ordinal);
            Assert.Contains("GET_LOCK(", statement, StringComparison.OrdinalIgnoreCase);

            if (kind == ProbeKind.Diagnostic)
            {
                Assert.StartsWith($"{probe.WorkerId} | User lock |", statement, StringComparison.OrdinalIgnoreCase);
            }
        }

        AssertExecutedPayload(probe);
    }

    /// <summary>Does not mistake an idle session or the observer's own marker parameter for the blocked worker.</summary>
    /// <param name="targetName">The selected live engine.</param>
    /// <param name="kind">The one observation shape executed by this case.</param>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(NegativeCases))]
    public async Task Selected_metadata_query_rejects_an_unrelated_session_or_marker(
        string targetName,
        ProbeKind kind
    )
    {
        // Arrange
        using var safety = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        await using var probe =
            await NativeProbe.CreateAsync(targetName, CreatePayload(PayloadKind.InvalidUtf8Bytes), safety.Token);

        Exception? primary = null;
        object? actual;

        try
        {
            WriteObservations(targetName, kind, PayloadKind.InvalidUtf8Bytes, probe);

            // Act
            actual = await ObserveAsync(probe, kind, matchesWorker: false, safety.Token);
        }
        catch (Exception failure)
        {
            primary = failure;

            throw;
        }
        finally
        {
            await probe.CompleteAsync(primary);
        }

        // Assert
        if (kind == ProbeKind.Count)
        {
            Assert.Equal(0L, Convert.ToInt64(actual, CultureInfo.InvariantCulture));
        }
        else
        {
            Assert.True(actual is null or DBNull or "", "The observation reported unrelated native work.");
        }

        AssertExecutedPayload(probe);
    }

    /// <summary>Enumerates only selected, currently supported native targets.</summary>
    private static IEnumerable<string> SelectedTargets() => IntegrationTestEnvironment
        .GetSupportedTargets()
        .Where(IntegrationTestEnvironment.IsTargetSelected)
        .Select(target => target.ToString());

    /// <summary>Creates real binary and text inputs without putting invalid bytes in source text.</summary>
    private static object CreatePayload(
        PayloadKind kind
    ) => kind switch
    {
        PayloadKind.AsciiBytes => Encoding.ASCII.GetBytes("native_ascii_payload"),
        PayloadKind.InvalidUtf8Bytes => new byte[]
        {
            0x80,
            0x00,
            0xff,
            0x88,
            0x99,
        },
        PayloadKind.NonBmpText => "\U0001F680",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    /// <summary>Executes exactly the query shape selected by the current theory row.</summary>
    private static async Task<object?> ObserveAsync(
        NativeProbe probe,
        ProbeKind kind,
        bool matchesWorker,
        CancellationToken cancellationToken
    )
    {
        var source = MySqlNativeProcessMetadata.GetProcessListSource(probe.Observer);
        const string normalizedInfo = "CONVERT(CAST(INFO AS BINARY) USING latin1)";
        var sql = kind switch
        {
            ProbeKind.Count => $"SELECT COUNT(*) FROM {source} WHERE ID = @worker AND STATE = 'User lock' "
                + $"AND LOCATE(@marker, {normalizedInfo}) > 0;",
            ProbeKind.Statement => $"SELECT {normalizedInfo} FROM {source} WHERE ID = @worker AND STATE = 'User lock' "
                + $"AND LOCATE(@marker, {normalizedInfo}) > 0;",
            ProbeKind.Diagnostic => MySqlNativeProcessMetadata.GetActiveStatementsCommandText(probe.Observer),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

        await using var command = new MySqlCommand(sql, probe.Observer);

        if (kind == ProbeKind.Diagnostic)
        {
            // WHY: The observer serializes this needle into its own SQL text. The shared
            // diagnostic must exclude that session instead of reporting itself as a match.
            command.Parameters.AddWithValue("@tableName",
                matchesWorker
                    ? probe.Marker
                    : "absent_" + probe.Marker);
        }
        else
        {
            command.Parameters.AddWithValue("@worker",
                matchesWorker
                    ? probe.WorkerId
                    : probe.GuardId);

            command.Parameters.AddWithValue("@marker", probe.Marker);
        }

        return await command
            .ExecuteScalarAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Retains only this probe's synthetic statements, never other sessions' command text.</summary>
    private void WriteObservations(
        string targetName,
        ProbeKind kind,
        PayloadKind payloadKind,
        NativeProbe probe
    ) => _output.WriteLine(JsonSerializer.Serialize(new
    {
        targetName,
        kind,
        payloadKind,
        probe.WorkerId,
        probe.GuardId,
        probe.Marker,
        probe.WorkerBefore,
        probe.GuardBefore,
        probe.ActiveWorker,
    }));

    /// <summary>Proves the blocked native command actually transported the selected payload unchanged.</summary>
    private static void AssertExecutedPayload(
        NativeProbe probe
    )
    {
        var result = Assert.IsType<WorkerResult>(probe.Result);
        Assert.Equal(1, result.AcquiredLock);

        if (probe.Payload is byte[] bytes)
        {
            Assert.Equal(bytes, Assert.IsType<byte[]>(result.Payload));
        }
        else
        {
            Assert.Equal(Assert.IsType<string>(probe.Payload), Assert.IsType<string>(result.Payload));
        }
    }

    private sealed record ProcessObservation(
        long Id,
        string? State,
        string? Info
    );

    private sealed record WorkerResult(
        int AcquiredLock,
        object Payload
    );

    /// <summary>Owns three fresh physical sessions and one unique native lock for a single theory row.</summary>
    private sealed class NativeProbe : IAsyncDisposable
    {
        private readonly MySqlConnection _guard;

        private readonly string _lockName = "doka_process_"
            + Guid
                .NewGuid()
                .ToString("N");

        private readonly MySqlConnection _worker;
        private readonly MySqlCommand _workerCommand;
        private Task? _completionTask;
        private bool _guardOwnsLock;
        private Task<WorkerResult>? _pending;

        private NativeProbe(
            string connectionString,
            object payload
        )
        {
            Payload = payload;
            _guard = new MySqlConnection(connectionString);
            _worker = new MySqlConnection(connectionString);
            Observer = new MySqlConnection(connectionString);
            Marker = "doka_native_process_"
                + Guid
                    .NewGuid()
                    .ToString("N");

            // WHY: Ordinary unprepared parameter serialization puts the real byte/text
            // representation on the wire; payloads are never interpolated into SQL by the test.
            _workerCommand = new MySqlCommand($"SELECT /* {Marker} */ GET_LOCK(@name, 60), @payload;", _worker)
            {
                CommandTimeout = 0,
            };

            _workerCommand.Parameters.AddWithValue("@name", _lockName);
            _workerCommand.Parameters.AddWithValue("@payload", payload);
        }

        internal MySqlConnection Observer { get; }
        internal string Marker { get; }
        internal object Payload { get; }

        /// <summary>Gets the captured physical worker ID independently of later connection disposal.</summary>
        internal int WorkerId { get; private set; }

        /// <summary>Gets the captured physical guard ID independently of later connection disposal.</summary>
        internal int GuardId { get; private set; }

        internal ProcessObservation? WorkerBefore { get; private set; }
        internal ProcessObservation? GuardBefore { get; private set; }
        internal ProcessObservation? ActiveWorker { get; private set; }
        internal WorkerResult? Result { get; private set; }

        /// <inheritdoc />
        public ValueTask DisposeAsync() => new(CompleteAsync(null));

        /// <summary>Establishes real baseline rows and a native marker-qualified user-lock wait before observation.</summary>
        internal static async Task<NativeProbe> CreateAsync(
            string targetName,
            object payload,
            CancellationToken cancellationToken
        )
        {
            var target = Enum.Parse<IntegrationDatabaseTarget>(targetName);
            var settings = new MySqlConnectionStringBuilder(IntegrationTestEnvironment.GetBaseConnectionString(target))
            {
                Database = string.Empty,
                Pooling = false,
                AutoEnlist = false,
            };

            var probe = new NativeProbe(settings.ConnectionString, payload);

            try
            {
                await probe
                    ._guard
                    .OpenAsync(cancellationToken)
                    .ConfigureAwait(false);

                await probe
                    ._worker
                    .OpenAsync(cancellationToken)
                    .ConfigureAwait(false);

                await probe
                    .Observer
                    .OpenAsync(cancellationToken)
                    .ConfigureAwait(false);

                probe.WorkerId = probe._worker.ServerThread;
                probe.GuardId = probe._guard.ServerThread;
                var source = MySqlNativeProcessMetadata.GetProcessListSource(probe.Observer);
                var baselineSql =
                    $"SELECT ID, STATE, CONVERT(CAST(INFO AS BINARY) USING latin1) FROM {source} WHERE ID = @id;";

                probe.WorkerBefore = await probe.ReadProcessAsync(baselineSql, probe.WorkerId, cancellationToken);
                probe.GuardBefore = await probe.ReadProcessAsync(baselineSql, probe.GuardId, cancellationToken);

                // WHY: A missing/instrumentation-disabled source must not turn a negative
                // observation into a false success merely because it exposes no sessions.
                if (probe.WorkerBefore is null
                    || probe.GuardBefore is null)
                {
                    throw new InvalidOperationException(
                        "Native process metadata did not expose both fresh sessions before the probe.");
                }

                if (await LockAsync(probe._guard, probe._lockName, acquire: true, cancellationToken)
                        .ConfigureAwait(false)
                    != 1)
                {
                    throw new InvalidOperationException(
                        "The private guard did not acquire its unique native user lock.");
                }

                probe._guardOwnsLock = true;
                probe._pending = probe.ExecuteWorkerAsync(cancellationToken);
                probe.ActiveWorker = await probe
                    .WaitForNativeUserLockAsync(cancellationToken)
                    .ConfigureAwait(false);

                return probe;
            }
            catch (Exception failure)
            {
                await probe
                    .CompleteAsync(failure)
                    .ConfigureAwait(false);

                throw;
            }
        }

        /// <summary>Releases and drains owned work even after request cancellation, preserving the primary failure.</summary>
        internal Task CompleteAsync(
            Exception? primaryFailure
        )
        {
            // WHY: This sequential case-owned fixture has exactly one completion workflow.
            // Explicit primary-preserving cleanup and await using observe the same task, so
            // neither locks nor connections are released twice, even when cleanup faults.
            // Cleanup cannot abandon native work after request cancellation; the existing
            // helper retains secondary failures without replacing the primary exception.
            return _completionTask ??= DatabaseLockProbeCleanup.CompleteLockProbeAsync(
                null,
                ReleaseGuardAsync,
                CaptureWorkerResultAsync(),
                primaryFailure,
                ReleaseWorkerAndDisposeAsync);
        }

        /// <summary>Reads one selected native row with byte-preserving normalization of its synthetic SQL text.</summary>
        private async Task<ProcessObservation?> ReadProcessAsync(
            string sql,
            int id,
            CancellationToken cancellationToken
        )
        {
            // WHY: Callers own the fixed metadata grammar. Casting INFO to binary before
            // latin1 conversion preserves arbitrary original SQL bytes without text decoding.
            await using var command = new MySqlCommand(sql, Observer);

            command.Parameters.AddWithValue("@id", id);
            await using var reader = await command
                .ExecuteReaderAsync(cancellationToken)
                .ConfigureAwait(false);

            if (!await reader
                .ReadAsync(cancellationToken)
                .ConfigureAwait(false))
            {
                return null;
            }

            return new ProcessObservation(reader.GetInt64(0),
                reader.IsDBNull(1)
                    ? null
                    : reader.GetString(1),
                reader.IsDBNull(2)
                    ? null
                    : reader.GetString(2));
        }

        /// <summary>Uses independent native wait/state evidence; elapsed time and task pendingness never prove readiness.</summary>
        private async Task<ProcessObservation> WaitForNativeUserLockAsync(
            CancellationToken cancellationToken
        )
        {
            var mariaDb = Observer.ServerVersion.Contains("MariaDB", StringComparison.OrdinalIgnoreCase);
            var sql = mariaDb
                ? "SELECT ID, STATE, CONVERT(CAST(INFO AS BINARY) USING latin1) FROM information_schema.PROCESSLIST WHERE ID = @id;"
                : "SELECT PROCESSLIST_ID, PROCESSLIST_STATE, CONVERT(CAST(PROCESSLIST_INFO AS BINARY) USING latin1) "
                + "FROM performance_schema.threads WHERE PROCESSLIST_ID = @id;";

            while (_pending is { IsCompleted: false })
            {
                cancellationToken.ThrowIfCancellationRequested();
                var active = await ReadProcessAsync(sql, WorkerId, cancellationToken)
                    .ConfigureAwait(false);

                if (active is { State: not null, Info: not null }
                    && string.Equals(active.State, "User lock", StringComparison.OrdinalIgnoreCase)
                    && active.Info.Contains(Marker, StringComparison.Ordinal))
                {
                    return active;
                }

                // WHY: This only yields to the server/driver; success requires the exact
                // session's native User lock state and command marker, never a delay threshold.
                await Task
                    .Delay(10, cancellationToken)
                    .ConfigureAwait(false);
            }

            if (_pending is not null)
            {
                await _pending.ConfigureAwait(false);
            }

            throw new InvalidOperationException(
                "The worker completed before its marker-qualified native user-lock wait was observed.");
        }

        /// <summary>Includes the first row read so a result-set header cannot be mistaken for completed native work.</summary>
        private async Task<WorkerResult> ExecuteWorkerAsync(
            CancellationToken cancellationToken
        )
        {
            await using var reader = await _workerCommand
                .ExecuteReaderAsync(cancellationToken)
                .ConfigureAwait(false);

            if (!await reader
                .ReadAsync(cancellationToken)
                .ConfigureAwait(false))
            {
                throw new InvalidOperationException("The native worker returned no lock/payload row.");
            }

            return new WorkerResult(reader.GetInt32(0), reader.GetValue(1));
        }

        /// <summary>Captures owned work without an abandonable cleanup token.</summary>
        private async Task CaptureWorkerResultAsync()
        {
            if (_pending is not null)
            {
                Result = await _pending.ConfigureAwait(false);
            }
        }

        /// <summary>Unconditionally releases the guard's exact lock independently of request cancellation.</summary>
        private async Task ReleaseGuardAsync()
        {
            if (!_guardOwnsLock)
            {
                return;
            }

            if (_guard.State == ConnectionState.Open)
            {
                await LockAsync(_guard, _lockName, acquire: false, CancellationToken.None)
                    .ConfigureAwait(false);
            }

            _guardOwnsLock = false;
        }

        /// <summary>Attempts every owned release/disposal after drainage without losing independent cleanup failures.</summary>
        private async Task ReleaseWorkerAndDisposeAsync()
        {
            List<Exception>? failures = null;

            try
            {
                if (_worker.State == ConnectionState.Open)
                {
                    await LockAsync(_worker, _lockName, acquire: false, CancellationToken.None)
                        .ConfigureAwait(false);
                }
            }
            catch (Exception failure)
            {
                (failures ??= []).Add(failure);
            }

            // WHY: A failed close/dispose must not prevent the remaining fresh sessions
            // from releasing their private native locks and socket ownership.
            foreach (var resource in new IAsyncDisposable[] { _workerCommand, _worker, Observer, _guard })
            {
                try
                {
                    await resource
                        .DisposeAsync()
                        .ConfigureAwait(false);
                }
                catch (Exception failure)
                {
                    (failures ??= []).Add(failure);
                }
            }

            if (failures is not null)
            {
                throw new AggregateException("Native process probe resource cleanup failed.", failures);
            }
        }

        /// <summary>Executes only the two fixed native lock operations with a required forward/cleanup token.</summary>
        private static async Task<int> LockAsync(
            MySqlConnection connection,
            string name,
            bool acquire,
            CancellationToken cancellationToken
        )
        {
            await using var command = new MySqlCommand(acquire
                    ? "SELECT GET_LOCK(@name, 0);"
                    : "SELECT RELEASE_LOCK(@name);",
                connection);

            command.Parameters.AddWithValue("@name", name);
            var result = await command
                .ExecuteScalarAsync(cancellationToken)
                .ConfigureAwait(false);

            return result is null or DBNull
                ? -1
                : Convert.ToInt32(result, CultureInfo.InvariantCulture);
        }
    }
}
