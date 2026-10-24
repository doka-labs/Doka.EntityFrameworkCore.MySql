namespace Doka.EntityFrameworkCore.MySql.Tests;

public sealed partial class AdrRepositoryValidatorTests
{
    /// <summary>
    /// Accepts the documented queue choices at workflow and job scope.
    /// </summary>
    [Theory]
    [InlineData(false, "max", "false")]
    [InlineData(true, "max", "false")]
    [InlineData(false, "single", "true")]
    [InlineData(true, "single", "${{ github.event_name == 'pull_request' }}")]
    [InlineData(false, "'max'", null)]
    [InlineData(true, "\"single\"", "false")]
    public void Workflow_concurrency_contract_accepts_documented_queue_shapes(
        bool jobLevel,
        string queue,
        string? cancellation
    )
    {
        // Arrange
        var workflow = QueuedWorkflow(jobLevel, queue, cancellation);

        // Act
        var errors = EngineeringSurfaceContract.ValidateWorkflowConcurrency(workflow, "sample.yml");

        // Assert
        Assert.Empty(errors);
    }

    /// <summary>
    /// Rejects unknown values and nonstring queue types before the compatibility filter runs.
    /// </summary>
    [Theory]
    [InlineData("all")]
    [InlineData("true")]
    [InlineData("123")]
    [InlineData("null")]
    [InlineData("[max]")]
    [InlineData("{ mode: max }")]
    [InlineData("''")]
    [InlineData("${{ inputs.queue }}")]
    public void Workflow_concurrency_contract_rejects_invalid_queue_values(
        string queue
    )
    {
        // Arrange
        var workflow = QueuedWorkflow(true, queue, "false");

        // Act
        var errors = EngineeringSurfaceContract.ValidateWorkflowConcurrency(workflow, "sample.yml");

        // Assert
        Assert.Contains("queue must be the string 'single' or 'max'", Assert.Single(errors), StringComparison.Ordinal);
    }

    /// <summary>
    /// Keeps a maximum publication queue incompatible with cancellation and ambiguous scalar types.
    /// </summary>
    [Theory]
    [InlineData(false, "true")]
    [InlineData(true, "true")]
    [InlineData(false, "'true'")]
    [InlineData(true, "\"false\"")]
    [InlineData(true, "${{ inputs.cancel }}")]
    public void Workflow_concurrency_contract_rejects_cancelling_or_ambiguous_max_queue(
        bool jobLevel,
        string cancellation
    )
    {
        // Arrange
        var workflow = QueuedWorkflow(jobLevel, "max", cancellation);

        // Act
        var errors = EngineeringSurfaceContract.ValidateWorkflowConcurrency(workflow, "sample.yml");

        // Assert
        Assert.NotEmpty(errors);
        Assert.All(errors, error => Assert.Contains("sample.yml", error, StringComparison.Ordinal));
    }

    /// <summary>
    /// A quoted bool remains a string even when using the default single-pending queue.
    /// </summary>
    [Theory]
    [InlineData("'true'")]
    [InlineData("\"false\"")]
    [InlineData("1")]
    [InlineData("null")]
    public void Workflow_concurrency_contract_rejects_nonboolean_cancellation(
        string cancellation
    )
    {
        // Arrange
        var workflow = QueuedWorkflow(false, "single", cancellation);

        // Act
        var errors = EngineeringSurfaceContract.ValidateWorkflowConcurrency(workflow, "sample.yml");

        // Assert
        Assert.Contains("cancel-in-progress", Assert.Single(errors), StringComparison.Ordinal);
    }

    /// <summary>
    /// Ambiguous mapping forms must not hide queue semantics from the focused validator.
    /// </summary>
    [Theory]
    [InlineData("concurrency: { group: publication, queue: wrong }\n")]
    [InlineData("concurrency: &publication\n  group: publication\n  queue: max\n")]
    [InlineData("concurrency: *publication\n")]
    [InlineData("concurrency:\n  group: publication\n  queue: max\n  queue: single\n")]
    [InlineData("!!str concurrency:\n  group: publication\n  queue: invalid\n")]
    [InlineData("? concurrency\n:\n  group: publication\n  queue: invalid\n")]
    [InlineData("concurrency:\n  group: publication\n  !!str queue: invalid\n")]
    [InlineData("concurrency:\n  group: publication\n  \"que\\u0075e\": invalid\n")]
    [InlineData("\"concu\\u0072ency\":\n  group: publication\n  queue: invalid\n")]
    [InlineData("concurrency: # Comment\n  group: publication\n  queue: invalid\n")]
    public void Workflow_concurrency_contract_rejects_ambiguous_queued_mappings(
        string concurrency
    )
    {
        // Arrange
        var workflow = "name: Sample\non: workflow_dispatch\n" + concurrency
            + "jobs:\n  sample:\n    runs-on: ubuntu-24.04\n    steps:\n      - run: true\n";

        // Act
        var errors = EngineeringSurfaceContract.ValidateWorkflowConcurrency(workflow, "sample.yml");

        // Assert
        Assert.NotEmpty(errors);
    }

    /// <summary>
    /// A header comment must not turn a valid explicit mapping into an opaque scalar.
    /// </summary>
    [Fact]
    public void Workflow_concurrency_contract_preserves_header_comments()
    {
        // Arrange
        var workflow = QueuedWorkflow(false, "max # Keep every waiting publisher", "false")
            .Replace("concurrency:\n", "concurrency: # Publication boundary\n", StringComparison.Ordinal);

        // Act
        var errors = EngineeringSurfaceContract.ValidateWorkflowConcurrency(workflow, "sample.yml");

        // Assert
        Assert.Empty(errors);
    }

    /// <summary>
    /// Literal run-block text is not workflow configuration and must remain untouched.
    /// </summary>
    [Fact]
    public void Workflow_concurrency_contract_ignores_block_scalar_contents()
    {
        // Arrange
        const string workflow = "name: Sample\non: workflow_dispatch\njobs:\n  sample:\n"
            + "    runs-on: ubuntu-24.04\n    steps:\n      - run: |\n"
            + "          concurrency:\n            queue: invalid\n";

        // Act
        var errors = EngineeringSurfaceContract.ValidateWorkflowConcurrency(workflow, "sample.yml");

        // Assert
        Assert.Empty(errors);
    }

    /// <summary>
    /// The lint entrypoint validates just concurrency, without requiring unrelated repository documents.
    /// </summary>
    [Theory]
    [InlineData("max", "false", 0)]
    [InlineData("single", "true", 0)]
    [InlineData("max", "true", 1)]
    [InlineData("invalid", "false", 1)]
    public void Workflow_concurrency_cli_reports_the_focused_contract(
        string queue,
        string cancellation,
        int expectedExitCode
    )
    {
        // Arrange
        using var directory = TestRepository.Create();
        var workflows = Path.Combine(directory.Root, ".github", "workflows");
        Directory.CreateDirectory(workflows);
        File.WriteAllText(Path.Combine(workflows, "sample.yml"), QueuedWorkflow(true, queue, cancellation));

        // Act
        var exitCode = Doka.EntityFrameworkCore.MySql.RepositoryContract.Program.Main(
            ["--workflow-concurrency", "--root", directory.Root]);

        // Assert
        Assert.Equal(expectedExitCode, exitCode);
    }

    /// <summary>
    /// Mirrors reusable workflows and nested actions without changing source bytes.
    /// </summary>
    [Fact]
    public void Actionlint_snapshot_normalizes_self_references_and_retains_callees()
    {
        // Arrange
        using var repository = TestRepository.Create();
        using var snapshot = TestRepository.Create();
        var destination = Path.Combine(snapshot.Root, "lint");
        WriteSelfRepositoryFixture(repository.Root);
        var callerPath = Path.Combine(repository.Root, ".github/workflows/caller.yml");
        var original = File.ReadAllText(callerPath);

        // Act
        var errors = EngineeringSurfaceContract.PrepareActionlintSnapshot(repository.Root, destination);

        // Assert
        Assert.Empty(errors);
        Assert.Equal(original, File.ReadAllText(callerPath));
        Assert.Equal(original.Replace("uses: $/", "uses: ./", StringComparison.Ordinal),
            File.ReadAllText(Path.Combine(destination, ".github/workflows/caller.yml")));
        Assert.Contains("known:", File.ReadAllText(Path.Combine(destination, ".github/workflows/child.yml")),
            StringComparison.Ordinal);
        Assert.Contains("uses: ./.github/actions/child", File.ReadAllText(
            Path.Combine(destination, ".github/actions/parent/action.yml")), StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(destination, ".github/actions/child/action.yml")));
        Assert.Equal("self-hosted-runner:\n  labels: [local]\n", File.ReadAllText(
            Path.Combine(destination, ".github/actionlint.yaml")));
        var composites = Directory.GetFiles(Path.Combine(destination, ".github/workflows"), "__actionlint_composite_*");

        Assert.Equal(2, composites.Length);
        Assert.Contains(composites, path => File.ReadAllText(path)
            .Contains("      marker:\n        type: string\n", StringComparison.Ordinal));
        Assert.All(composites, path => Assert.Contains(
            "    steps:\n", File.ReadAllText(path), StringComparison.Ordinal));
    }

    /// <summary>
    /// Invalid self references must fail before creating a linter view.
    /// </summary>
    [Theory]
    [InlineData("$/.github/workflows/missing.yml")]
    [InlineData("$/.github/actions/missing")]
    [InlineData("$/.github/workflows/../workflows/child.yml")]
    [InlineData("$/.github/workflows/child.yml@main")]
    [InlineData("$/.github/workflows/${{ inputs.workflow }}.yml")]
    [InlineData("$/other/actions/child")]
    [InlineData("$/.github/Actions/child")]
    public void Actionlint_snapshot_rejects_noncanonical_or_missing_self_references(
        string reference
    )
    {
        // Arrange
        using var repository = TestRepository.Create();
        using var snapshot = TestRepository.Create();
        var destination = Path.Combine(snapshot.Root, "lint");
        WriteSelfRepositoryFixture(repository.Root);
        File.WriteAllText(Path.Combine(repository.Root, ".github/workflows/caller.yml"),
            $"name: Caller\non: workflow_dispatch\njobs:\n  child:\n    uses: {reference}\n");

        // Act
        var errors = EngineeringSurfaceContract.PrepareActionlintSnapshot(repository.Root, destination);

        // Assert
        Assert.NotEmpty(errors);
        Assert.False(Directory.Exists(destination));
    }

    /// <summary>
    /// A symlink must not change the files represented by exact self-reference metadata.
    /// </summary>
    [Fact]
    public void Actionlint_snapshot_rejects_symbolic_link_targets()
    {
        // Arrange
        using var repository = TestRepository.Create();
        using var snapshot = TestRepository.Create();
        WriteSelfRepositoryFixture(repository.Root);
        var target = Path.Combine(repository.Root, ".github/workflows/link.yml");
        File.CreateSymbolicLink(target, Path.Combine(repository.Root, ".github/workflows/child.yml"));

        // Act
        var errors = EngineeringSurfaceContract.PrepareActionlintSnapshot(
            repository.Root, Path.Combine(snapshot.Root, "lint"));

        // Assert
        Assert.Contains(errors, error => error.Contains("symbolic", StringComparison.Ordinal));
    }

    /// <summary>
    /// Snapshot creation must never overwrite repository files or an existing output.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Actionlint_snapshot_rejects_unsafe_destinations(
        bool insideRepository
    )
    {
        // Arrange
        using var repository = TestRepository.Create();
        using var snapshot = TestRepository.Create();
        WriteSelfRepositoryFixture(repository.Root);
        var destination = insideRepository ? repository.Root : snapshot.Root;

        // Act
        var errors = EngineeringSurfaceContract.PrepareActionlintSnapshot(repository.Root, destination);

        // Assert
        Assert.NotEmpty(errors);
        Assert.True(File.Exists(Path.Combine(repository.Root, ".github/workflows/caller.yml")));
    }

    /// <summary>
    /// A linter snapshot must retain literal script examples, even when they resemble action metadata.
    /// </summary>
    [Fact]
    public void Actionlint_snapshot_does_not_rewrite_script_literals()
    {
        // Arrange
        using var repository = TestRepository.Create();
        using var snapshot = TestRepository.Create();
        WriteSelfRepositoryFixture(repository.Root);
        const string workflow = "name: Literal\non: workflow_dispatch\njobs:\n  sample:\n"
            + "    runs-on: ubuntu-24.04\n    steps:\n      - run: |\n"
            + "          uses: $/.github/actions/not-a-reference\n";

        File.WriteAllText(Path.Combine(repository.Root, ".github/workflows/literal.yml"), workflow);
        var destination = Path.Combine(snapshot.Root, "lint");

        // Act
        var errors = EngineeringSurfaceContract.PrepareActionlintSnapshot(repository.Root, destination);

        // Assert
        Assert.Empty(errors);
        Assert.Equal(workflow, File.ReadAllText(Path.Combine(destination, ".github/workflows/literal.yml")));
    }

    /// <summary>
    /// Unsupported action metadata shapes must not silently skip composite shell analysis.
    /// </summary>
    [Theory]
    [InlineData("runs: { using: composite, steps: [] }\n")]
    [InlineData("'runs':\n  using: composite\n  steps: []\n")]
    [InlineData("runs:\n  using: composite\n  steps: []\n")]
    public void Actionlint_snapshot_rejects_opaque_composite_step_shapes(
        string runs
    )
    {
        // Arrange
        using var repository = TestRepository.Create();
        using var snapshot = TestRepository.Create();
        WriteSelfRepositoryFixture(repository.Root);
        File.WriteAllText(Path.Combine(repository.Root, ".github/actions/child/action.yml"),
            "name: Child\ndescription: Opaque step shape\n" + runs);

        // Act
        var errors = EngineeringSurfaceContract.PrepareActionlintSnapshot(
            repository.Root, Path.Combine(snapshot.Root, "lint"));

        // Assert
        Assert.NotEmpty(errors);
    }

    /// <summary>
    /// The adapter CLI emits a complete view only when every referenced target is valid.
    /// </summary>
    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 1)]
    public void Actionlint_snapshot_cli_validates_before_emitting_metadata(
        bool invalid,
        int expectedExitCode
    )
    {
        // Arrange
        using var repository = TestRepository.Create();
        using var snapshot = TestRepository.Create();
        WriteSelfRepositoryFixture(repository.Root);
        if (invalid)
        {
            File.Delete(Path.Combine(repository.Root, ".github/workflows/child.yml"));
        }

        var destination = Path.Combine(snapshot.Root, "lint");

        // Act
        var exitCode = Doka.EntityFrameworkCore.MySql.RepositoryContract.Program.Main(
            ["--actionlint-snapshot", destination, "--root", repository.Root]);

        // Assert
        Assert.Equal(expectedExitCode, exitCode);
        Assert.Equal(!invalid, Directory.Exists(Path.Combine(destination, ".git")));
    }

    /// <summary>
    /// Creates the complete same-repository metadata graph the linter must still inspect.
    /// </summary>
    private static void WriteSelfRepositoryFixture(
        string root
    )
    {
        var workflows = Path.Combine(root, ".github/workflows");
        var parent = Path.Combine(root, ".github/actions/parent");
        var child = Path.Combine(root, ".github/actions/child");
        Directory.CreateDirectory(workflows);
        Directory.CreateDirectory(parent);
        Directory.CreateDirectory(child);
        File.WriteAllText(Path.Combine(workflows, "caller.yml"),
            "name: Caller\non: workflow_dispatch\njobs:\n  child:\n"
            + "    uses: $/.github/workflows/child.yml\n    with:\n      known: true\n");

        File.WriteAllText(Path.Combine(workflows, "child.yml"),
            "name: Child\non:\n  workflow_call:\n    inputs:\n      known:\n        type: boolean\n"
            + "jobs:\n  check:\n    runs-on: ubuntu-24.04\n    steps:\n"
            + "      - uses: $/.github/actions/parent\n");

        File.WriteAllText(Path.Combine(parent, "action.yml"),
            "name: Parent\ndescription: Nested composite\nruns:\n  using: composite\n  steps:\n"
            + "    - uses: $/.github/actions/child\n");

        File.WriteAllText(Path.Combine(child, "action.yml"),
            "name: Child\ndescription: Terminal composite\ninputs:\n  marker:\n    description: Marker input\n"
            + "runs:\n  using: composite\n  steps:\n"
            + "    - shell: bash\n      run: echo ok\n");

        File.WriteAllText(Path.Combine(root, ".github/actionlint.yaml"),
            "self-hosted-runner:\n  labels: [local]\n");
    }

    /// <summary>
    /// Creates a minimal block-style workflow at the same two scopes GitHub accepts.
    /// </summary>
    private static string QueuedWorkflow(
        bool jobLevel,
        string queue,
        string? cancellation
    )
    {
        var indent = jobLevel ? "    " : string.Empty;
        var concurrency = $"{indent}concurrency:\n{indent}  group: publication\n{indent}  queue: {queue}\n";
        if (cancellation is not null)
        {
            concurrency += $"{indent}  cancel-in-progress: {cancellation}\n";
        }

        var header = "name: Sample\non: workflow_dispatch\n";
        var job = "jobs:\n  sample:\n    runs-on: ubuntu-24.04\n";
        var steps = "    steps:\n      - run: true\n";

        return jobLevel ? header + job + concurrency + steps : header + concurrency + job + steps;
    }
}
