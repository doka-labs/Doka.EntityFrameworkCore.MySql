using System.Text.Json;
using DotNet.Testcontainers.Containers;

namespace Doka.EntityFrameworkCore.MySql.TestUtilities;

/// <summary>
/// Owns the database endpoints used by one live test assembly.
/// </summary>
public sealed class TestDatabaseSession : IAsyncDisposable
{
    private const string EvidenceFileEnvironmentVariable = "DOKA_TEST_DATABASE_EVIDENCE_FILE";
    private static readonly TimeSpan s_startupTimeout = TimeSpan.FromMinutes(3);
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly IReadOnlyDictionary<string, TestDatabaseEndpoint> _endpoints;
    private readonly IReadOnlyList<TestDatabaseContainer> _containers;
    private readonly string? _evidenceFile;
    private bool _disposed;

    private TestDatabaseSession(
        IReadOnlyDictionary<string, TestDatabaseEndpoint> endpoints,
        IReadOnlyList<TestDatabaseContainer> containers,
        string? evidenceFile
    )
    {
        _endpoints = endpoints;
        _containers = containers;
        _evidenceFile = evidenceFile;
    }

    /// <summary>
    /// Starts locally provisioned targets and validates externally supplied targets.
    /// </summary>
    /// <param name="requests">The target definitions owned by this session.</param>
    /// <param name="evidenceScope">
    /// An optional single directory name that isolates this session's evidence
    /// from another fixture in the same test process.
    /// </param>
    /// <returns>A session that owns every locally provisioned target.</returns>
    public static async Task<TestDatabaseSession> StartAsync(
        IEnumerable<TestDatabaseRequest> requests,
        string? evidenceScope = null
    )
    {
        ArgumentNullException.ThrowIfNull(requests);

        var requestArray = requests.ToArray();
        ValidateRequests(requestArray);
        var evidenceFile = ResolveEvidenceFile(evidenceScope);

        var endpoints = new Dictionary<string, TestDatabaseEndpoint>(StringComparer.OrdinalIgnoreCase);
        var containers = new List<TestDatabaseContainer>();

        try
        {
            foreach (var request in requestArray)
            {
                // Each image receives the complete startup budget. Sharing one
                // deadline across a matrix makes later targets depend on pull
                // and initialization time consumed by unrelated predecessors.
                using var startupCancellation = new CancellationTokenSource(s_startupTimeout);
                var externalConnectionString =
                    Environment.GetEnvironmentVariable(request.ConnectionStringEnvironmentVariable);

                if (!string.IsNullOrWhiteSpace(externalConnectionString))
                {
                    if (request.SecurityProfile != TestDatabaseSecurityProfile.PlainText)
                    {
                        throw new InvalidOperationException(
                            $"Test-owned TLS target '{request.TargetId}' cannot use external endpoint "
                            + $"'{request.ConnectionStringEnvironmentVariable}'.");
                    }

                    await VerifyExternalEndpointAsync(externalConnectionString, startupCancellation.Token)
                        .ConfigureAwait(false);

                    endpoints.Add(
                        request.TargetId,
                        new TestDatabaseEndpoint(
                            request.TargetId,
                            request.Engine,
                            request.ServerVersionToken,
                            externalConnectionString,
                            "environment",
                            null,
                            null,
                            null));
                    continue;
                }

                if (string.IsNullOrWhiteSpace(request.Image))
                {
                    throw new InvalidOperationException(
                        $"Test database target '{request.TargetId}' requires environment variable "
                        + $"'{request.ConnectionStringEnvironmentVariable}'.");
                }

                TestDatabaseContainer container;

                try
                {
                    container = await TestDatabaseContainer
                        .StartAsync(request, startupCancellation.Token)
                        .ConfigureAwait(false);
                }
                catch (ResourceReaperException exception)
                {
                    throw new InvalidOperationException(
                        $"Test database infrastructure failed before any test body ran for target "
                        + $"'{request.TargetId}': the Testcontainers Resource Reaper did not initialize. "
                        + "Let a cancelled IDE run finish cleanup before retrying. For repeated local runs, "
                        + "use a persistent Compose "
                        + $"endpoint through {request.ConnectionStringEnvironmentVariable} instead of "
                        + "provisioning a new container for every run.",
                        exception);
                }
                catch (OperationCanceledException exception) when (startupCancellation.IsCancellationRequested)
                {
                    throw new TimeoutException(
                        $"Test database infrastructure did not initialize target '{request.TargetId}' "
                        + $"within {s_startupTimeout}. No test body ran.",
                        exception);
                }

                containers.Add(container);

                endpoints.Add(
                    request.TargetId,
                    new TestDatabaseEndpoint(
                        request.TargetId,
                        request.Engine,
                        request.ServerVersionToken,
                        container.ConnectionString,
                        "testcontainers",
                        request.Image,
                        container.ContainerId,
                        container.TlsOptions));
            }

            var session = new TestDatabaseSession(endpoints, containers, evidenceFile);
            await session
                .WriteEvidenceAsync("ready")
                .ConfigureAwait(false);
            return session;
        }
        catch
        {
            await DisposeContainersAsync(containers)
                .ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// Gets one ready endpoint by target id.
    /// </summary>
    public TestDatabaseEndpoint GetEndpoint(
        string targetId
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetId);

        return _endpoints.TryGetValue(targetId, out var endpoint)
            ? endpoint
            : throw new InvalidOperationException(
                $"Test database target '{targetId}' was not selected for this test run.");
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            await DisposeContainersAsync(_containers)
                .ConfigureAwait(false);
        }
        catch
        {
            await WriteEvidenceAsync("cleanup-failed")
                .ConfigureAwait(false);
            throw;
        }

        await WriteEvidenceAsync("cleanup-completed")
            .ConfigureAwait(false);
    }

    private static void ValidateRequests(
        TestDatabaseRequest[] requests
    )
    {
        if (requests.Length == 0)
        {
            throw new ArgumentException("At least one test database target must be requested.", nameof(requests));
        }

        var duplicateTargetIds = requests
            .GroupBy(request => request.TargetId, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .OrderBy(targetId => targetId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (duplicateTargetIds.Length > 0)
        {
            throw new ArgumentException(
                $"Duplicate test database target id(s): {string.Join(", ", duplicateTargetIds)}.",
                nameof(requests));
        }
    }

    private static async Task VerifyExternalEndpointAsync(
        string connectionString,
        CancellationToken cancellationToken
    )
    {
        var builder = new MySqlConnectionStringBuilder(connectionString)
        {
            ConnectionTimeout = 15,
            Pooling = false,
        };

        await using var connection = new MySqlConnection(builder.ConnectionString);
        await connection
            .OpenAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task DisposeContainersAsync(
        IEnumerable<TestDatabaseContainer> containers
    )
    {
        List<Exception>? failures = null;

        foreach (var container in containers.Reverse())
        {
            try
            {
                await container
                    .DisposeAsync()
                    .ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                failures ??= [];
                failures.Add(exception);
            }
        }

        if (failures is not null)
        {
            throw new AggregateException("One or more test database containers could not be disposed.", failures);
        }
    }

    private async Task WriteEvidenceAsync(
        string lifecycleState
    )
    {
        if (string.IsNullOrWhiteSpace(_evidenceFile))
        {
            return;
        }

        var directory = Path.GetDirectoryName(_evidenceFile);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var targets = _endpoints.Values
            .OrderBy(endpoint => endpoint.TargetId, StringComparer.OrdinalIgnoreCase)
            .Select(CreateEvidenceTarget)
            .ToArray();

        var evidence = new TestDatabaseEvidence(
            SchemaVersion: 1,
            GeneratedUtc: DateTimeOffset.UtcNow,
            LifecycleState: lifecycleState,
            Targets: targets);

        var temporaryFile = $"{_evidenceFile}.{Guid.NewGuid():N}.tmp";

        await using (var stream = File.Create(temporaryFile))
        {
            await JsonSerializer
                .SerializeAsync(
                    stream,
                    evidence,
                    s_jsonOptions)
                .ConfigureAwait(false);
            await stream
                .FlushAsync()
                .ConfigureAwait(false);
        }

        File.Move(temporaryFile, _evidenceFile, overwrite: true);
    }

    private static string? ResolveEvidenceFile(
        string? evidenceScope
    )
    {
        var evidenceFile = Environment.GetEnvironmentVariable(EvidenceFileEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(evidenceFile)
            || string.IsNullOrWhiteSpace(evidenceScope))
        {
            return evidenceFile;
        }

        if (evidenceScope is "." or ".."
            || !string.Equals(Path.GetFileName(evidenceScope), evidenceScope, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The evidence scope must be a single directory name.",
                nameof(evidenceScope));
        }

        var directory = Path.GetDirectoryName(evidenceFile);
        var fileName = Path.GetFileName(evidenceFile);

        return string.IsNullOrWhiteSpace(directory)
            ? Path.Combine(evidenceScope, fileName)
            : Path.Combine(directory, evidenceScope, fileName);
    }

    private static TestDatabaseEvidenceTarget CreateEvidenceTarget(
        TestDatabaseEndpoint endpoint
    )
    {
        var builder = new MySqlConnectionStringBuilder(endpoint.ConnectionString);

        return new TestDatabaseEvidenceTarget(
            endpoint.TargetId,
            endpoint.Engine.ToString(),
            endpoint.ServerVersionToken,
            endpoint.Source,
            endpoint.Image,
            endpoint.ContainerId,
            builder.Server,
            builder.Port,
            builder.Database);
    }

    private sealed record TestDatabaseEvidence(
        int SchemaVersion,
        DateTimeOffset GeneratedUtc,
        string LifecycleState,
        IReadOnlyList<TestDatabaseEvidenceTarget> Targets
    );

    private sealed record TestDatabaseEvidenceTarget(
        string TargetId,
        string Engine,
        string ServerVersionToken,
        string Source,
        string? Image,
        string? ContainerId,
        string Server,
        uint Port,
        string Database
    );
}
