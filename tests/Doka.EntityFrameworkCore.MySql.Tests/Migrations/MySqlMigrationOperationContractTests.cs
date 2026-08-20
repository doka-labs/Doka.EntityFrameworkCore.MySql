namespace Doka.EntityFrameworkCore.MySql.Tests;

/// <summary>
/// Verifies immutability and reentry guarantees on the public handler value
/// objects.
/// </summary>
public sealed class MySqlMigrationOperationContractTests
{
    [Fact]
    public void Generated_result_snapshots_the_caller_owned_command_collection()
    {
        var commands = new List<MySqlMigrationCommandSpec>
        {
            MySqlMigrationCommandSpec.Create("SELECT 1;", transactionSuppressed: true),
        };

        var result = MySqlMigrationOperationResult.Generated(commands, "generated");
        commands.Add(MySqlMigrationCommandSpec.Create("SELECT 2;"));

        var command = Assert.Single(result.Commands);
        Assert.Equal("SELECT 1;", command.CommandText);
        Assert.True(command.TransactionSuppressed);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Generated")]
    [InlineData("generated-value")]
    [InlineData("1_generated")]
    [InlineData("generated value")]
    public void Invalid_outcome_codes_are_rejected(
        string outcomeCode
    )
    {
        Assert.Throws<ArgumentException>(() => MySqlMigrationOperationResult.Generated(
            [MySqlMigrationCommandSpec.Create("SELECT 1;")],
            outcomeCode));
    }

    [Fact]
    public void Empty_results_are_rejected() =>
        Assert.Throws<ArgumentException>(() => MySqlMigrationOperationResult.Generated([], "generated"));

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Null_command_elements_are_rejected_at_every_sequence_position(
        int nullIndex
    )
    {
        var commands = new[]
        {
            MySqlMigrationCommandSpec.Create("SELECT 1;"),
            MySqlMigrationCommandSpec.Create("SELECT 2;"),
            MySqlMigrationCommandSpec.Create("SELECT 3;"),
        };

        commands[nullIndex] = null!;

        Assert.Throws<ArgumentException>(() => MySqlMigrationOperationResult.Generated(commands, "generated"));
    }

    [Fact]
    public void Outcome_codes_longer_than_64_ascii_characters_are_rejected()
    {
        Assert.Throws<ArgumentException>(() => MySqlMigrationOperationResult.Generated(
            [MySqlMigrationCommandSpec.Create("SELECT 1;")],
            $"g{new string('a', 64)}"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_command_text_is_rejected(
        string commandText
    ) => Assert.Throws<ArgumentException>(() => MySqlMigrationCommandSpec.Create(commandText));

    [Fact]
    public void Handler_created_command_is_opaque()
    {
        var command = MySqlMigrationCommandSpec.Create(
            "SELECT 1;",
            transactionSuppressed: true);

        Assert.Empty(command.Fragments);
        Assert.Equal("SELECT 1;", command.CommandText);
        Assert.True(command.TransactionSuppressed);
    }

    [Fact]
    public void Opaque_commands_share_the_empty_fragment_snapshot()
    {
        var first = MySqlMigrationCommandSpec.Create("SELECT 1;");
        var second = MySqlMigrationCommandSpec.Create("SELECT 2;");

        Assert.Same(first.Fragments, second.Fragments);
    }

    [Fact]
    public void Default_fragment_is_explicitly_unclassified()
    {
        var fragment = default(MySqlMigrationCommandFragment);

        Assert.Equal(MySqlMigrationCommandFragmentKind.Unspecified, fragment.Kind);
        Assert.True(fragment.CommandText.IsEmpty);
    }

    [Fact]
    public void Provider_layout_preserves_exact_order_and_complete_coverage()
    {
        var setup = new[] { "SET @scope = 1;\n" };
        var cleanup = new[] { "SET @scope = NULL;\n" };
        var commandText = setup[0] + "ALTER TABLE `Entries` COMMENT 'safe';\n" + cleanup[0];

        var layout = MySqlMigrationCommandLayout.CreateScoped(commandText, setup, cleanup);

        Assert.Collection(
            layout.Fragments,
            fragment => Assert.Equal(MySqlMigrationCommandFragmentKind.Setup, fragment.Kind),
            fragment => Assert.Equal(MySqlMigrationCommandFragmentKind.Body, fragment.Kind),
            fragment => Assert.Equal(MySqlMigrationCommandFragmentKind.Cleanup, fragment.Kind));
        Assert.Equal(
            commandText,
            string.Concat(layout.Fragments.Select(static fragment => fragment.CommandText.ToString())));
        Assert.Equal("ALTER TABLE `Entries` COMMENT 'safe';\n", layout.Body.ToString());

        foreach (var fragment in layout.Fragments)
        {
            Assert.True(
                MemoryMarshal.TryGetString(
                    fragment.CommandText,
                    out var backingString,
                    out _,
                    out _));
            Assert.Same(commandText, backingString);
        }
    }

    [Fact]
    public void Provider_layout_rejects_a_fragment_that_does_not_match_the_command()
    {
        Assert.Throws<InvalidOperationException>(() =>
            MySqlMigrationCommandLayout.CreateScoped(
                "SET @scope = 1;\nSELECT 1;\nSET @scope = NULL;\n",
                ["SET @scope = 2;\n"],
                ["SET @scope = NULL;\n"]));
    }

    [Fact]
    public void Provider_layout_rejects_an_empty_body()
    {
        Assert.Throws<InvalidOperationException>(() =>
            MySqlMigrationCommandLayout.CreateScoped(
                "SET @scope = 1;\n   SET @scope = NULL;\n",
                ["SET @scope = 1;\n"],
                ["SET @scope = NULL;\n"]));
    }

    [Fact]
    public void Provider_layout_requires_setup_and_cleanup()
    {
        Assert.Throws<ArgumentException>(() =>
            MySqlMigrationCommandLayout.CreateScoped("SELECT 1;", [], ["SET @scope = NULL;"]));
        Assert.Throws<ArgumentException>(() =>
            MySqlMigrationCommandLayout.CreateScoped("SELECT 1;", ["SET @scope = 1;"], []));
    }

    [Fact]
    public void Recursive_standard_rendering_is_rejected()
    {
        var serverVersion = MySqlServerVersion.MySql(new Version(8, 4, 11));
        MySqlMigrationOperationContext context = null!;
        context = new MySqlMigrationOperationContext(
            new CustomOperation(),
            model: null,
            MigrationsSqlGenerationOptions.Default,
            serverVersion,
            new MySqlMigrationFeatureSet(serverVersion.Profile),
            operationOrdinal: 0,
            "tests.recursive",
            operation => context.RenderStandardOperation(operation));

        var exception = Assert.Throws<MySqlMigrationOperationHandlerException>(() =>
            context.RenderStandardOperation(new SqlOperation { Sql = "SELECT 1;" }));

        Assert.Equal(MySqlMigrationHandlerFailureCode.RecursiveProviderRendering, exception.FailureCode);
    }

    [Fact]
    public async Task Concurrent_standard_rendering_is_rejected()
    {
        var rendererEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var releaseRenderer = new ManualResetEventSlim();
        var serverVersion = MySqlServerVersion.MySql(new Version(8, 4, 11));
        var context = new MySqlMigrationOperationContext(
            new CustomOperation(),
            model: null,
            MigrationsSqlGenerationOptions.Default,
            serverVersion,
            new MySqlMigrationFeatureSet(serverVersion.Profile),
            operationOrdinal: 0,
            "tests.concurrent",
            _ =>
            {
                rendererEntered.TrySetResult();

                // Rendering is a synchronous provider contract. Hold this worker
                // deliberately while the async test probes the concurrent caller.
                return releaseRenderer.Wait(TimeSpan.FromSeconds(5))
                    ? [MySqlMigrationCommandSpec.Create("SELECT 1;")]
                    : throw new TimeoutException("The concurrent-render test did not release its first renderer.");
            });

        var activeRender = Task.Run(
            () => context.RenderStandardOperation(new SqlOperation { Sql = "SELECT 1;" }),
            CancellationToken.None);

        await rendererEntered.Task.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None);

        var exception = Assert.Throws<MySqlMigrationOperationHandlerException>(() =>
            context.RenderStandardOperation(new SqlOperation { Sql = "SELECT 2;" }));

        Assert.Equal(MySqlMigrationHandlerFailureCode.RecursiveProviderRendering, exception.FailureCode);

        releaseRenderer.Set();
        _ = await activeRender;
    }

    [Fact]
    public async Task Deactivation_waits_for_the_active_render_lease_and_blocks_new_rendering()
    {
        var rendererEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var releaseRenderer = new ManualResetEventSlim();
        var deactivationStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var serverVersion = MySqlServerVersion.MySql(new Version(8, 4, 11));
        var context = new MySqlMigrationOperationContext(
            new CustomOperation(),
            model: null,
            MigrationsSqlGenerationOptions.Default,
            serverVersion,
            new MySqlMigrationFeatureSet(serverVersion.Profile),
            operationOrdinal: 0,
            "tests.deactivation",
            _ =>
            {
                rendererEntered.TrySetResult();

                // Rendering is a synchronous provider contract. Hold this worker
                // deliberately until the async test observes the expiring lease.
                if (!releaseRenderer.Wait(TimeSpan.FromSeconds(5)))
                {
                    throw new TimeoutException("The deactivation test did not release its renderer.");
                }

                return [MySqlMigrationCommandSpec.Create("SELECT 1;")];
            });

        var activeRender = Task.Run(
            () => context.RenderStandardOperation(new SqlOperation { Sql = "SELECT 1;" }),
            CancellationToken.None);

        await rendererEntered.Task.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None);

        var deactivation = Task.Run(() =>
        {
            deactivationStarted.TrySetResult();
            context.Deactivate();
        }, CancellationToken.None);

        await deactivationStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
        var prematureCompletion = await Task.WhenAny(
            deactivation,
            Task.Delay(TimeSpan.FromMilliseconds(100), CancellationToken.None));

        Assert.NotSame(deactivation, prematureCompletion);

        MySqlMigrationOperationHandlerException? expiredException = null;

        Assert.True(
            SpinWait.SpinUntil(
                () =>
                {
                    try
                    {
                        _ = context.RenderStandardOperation(new SqlOperation { Sql = "SELECT 2;" });
                        return false;
                    }
                    catch (MySqlMigrationOperationHandlerException exception) when (exception.FailureCode
                     == MySqlMigrationHandlerFailureCode.RecursiveProviderRendering)
                    {
                        return false;
                    }
                    catch (MySqlMigrationOperationHandlerException exception) when (exception.FailureCode
                     == MySqlMigrationHandlerFailureCode.ContextExpired)
                    {
                        expiredException = exception;
                        return true;
                    }
                },
                TimeSpan.FromSeconds(1)));
        Assert.Equal(MySqlMigrationHandlerFailureCode.ContextExpired, expiredException!.FailureCode);

        releaseRenderer.Set();
        _ = await activeRender;
        await deactivation;
    }

    private sealed class CustomOperation : MigrationOperation;
}
