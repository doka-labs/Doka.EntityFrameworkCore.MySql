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
    public void Scoped_command_snapshots_roles_and_caller_owned_collections()
    {
        var setup = new List<string>
        {
            "SET @first = 1;\n",
            "SET @second = 2;\n",
        };
        var cleanup = new List<string>
        {
            "SET @first = NULL;\n",
            "SET @second = NULL;\n",
        };

        var command = MySqlMigrationCommandSpec.CreateScoped(
            setup,
            "SELECT @first + @second;\n",
            cleanup,
            transactionSuppressed: true);

        setup[0] = "SELECT 'mutated';";
        cleanup.Clear();

        Assert.Collection(
            command.Fragments,
            fragment => Assert.Equal(MySqlMigrationCommandFragmentKind.Setup, fragment.Kind),
            fragment => Assert.Equal(MySqlMigrationCommandFragmentKind.Setup, fragment.Kind),
            fragment => Assert.Equal(MySqlMigrationCommandFragmentKind.Body, fragment.Kind),
            fragment => Assert.Equal(MySqlMigrationCommandFragmentKind.Cleanup, fragment.Kind),
            fragment => Assert.Equal(MySqlMigrationCommandFragmentKind.Cleanup, fragment.Kind));
        Assert.Equal(
            "SET @first = 1;\n"
            + "SET @second = 2;\n"
            + "SELECT @first + @second;\n"
            + "SET @second = NULL;\n"
            + "SET @first = NULL;\n",
            command.CommandText);
        Assert.True(command.TransactionSuppressed);

        foreach (var fragment in command.Fragments)
        {
            Assert.True(MemoryMarshal.TryGetString(fragment.CommandText, out var backingString, out _, out _));
            Assert.Same(command.CommandText, backingString);
        }
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public void Scoped_command_rejects_every_empty_role(
        bool emptySetup,
        bool emptyBody,
        bool emptyCleanup
    )
    {
        var setup = emptySetup ? Array.Empty<string>() : ["SET @scope = 1;"];
        var body = emptyBody ? " " : "SELECT @scope;";
        var cleanup = emptyCleanup ? Array.Empty<string>() : ["SET @scope = NULL;"];

        Assert.Throws<ArgumentException>(() => MySqlMigrationCommandSpec.CreateScoped(setup, body, cleanup));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Scoped_command_rejects_whitespace_at_every_collection_boundary(
        bool invalidSetup,
        bool invalidCleanup
    )
    {
        string[] setup = invalidSetup ? [" "] : ["SET @scope = 1;"];
        string[] cleanup = invalidCleanup ? [" "] : ["SET @scope = NULL;"];

        Assert.Throws<ArgumentException>(() =>
            MySqlMigrationCommandSpec.CreateScoped(setup, "SELECT @scope;", cleanup));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Scoped_command_rejects_null_at_every_collection_boundary(
        bool invalidSetup,
        bool invalidCleanup
    )
    {
        string[] setup = invalidSetup ? [null!] : ["SET @scope = 1;"];
        string[] cleanup = invalidCleanup ? [null!] : ["SET @scope = NULL;"];

        Assert.Throws<ArgumentException>(() =>
            MySqlMigrationCommandSpec.CreateScoped(setup, "SELECT @scope;", cleanup));
    }

    [Fact]
    public void Scoped_command_enumerates_each_foreign_collection_once()
    {
        var setupEnumerations = 0;
        var cleanupEnumerations = 0;

        IEnumerable<string> Setup()
        {
            setupEnumerations++;
            yield return "SET @scope = 1;";
        }

        IEnumerable<string> Cleanup()
        {
            cleanupEnumerations++;
            yield return "SET @scope = NULL;";
        }

        var command = MySqlMigrationCommandSpec.CreateScoped(
            Setup(),
            "SELECT @scope;",
            Cleanup());

        Assert.Equal(3, command.Fragments.Count);
        Assert.Equal(1, setupEnumerations);
        Assert.Equal(1, cleanupEnumerations);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Scoped_command_wraps_collection_enumeration_failures_without_copying_the_payload(
        bool failSetup
    )
    {
        const string sensitivePayload = "server=private;password=do-not-log";

        static IEnumerable<string> ThrowingCommands(
            string message
        )
        {
            yield return "SET @scope = 1;";
            throw new InvalidOperationException(message);
        }

        var exception = Assert.Throws<ArgumentException>(() => MySqlMigrationCommandSpec.CreateScoped(
            failSetup ? ThrowingCommands(sensitivePayload) : ["SET @scope = 1;"],
            "SELECT @scope;",
            failSetup ? ["SET @scope = NULL;"] : ThrowingCommands(sensitivePayload)));

        Assert.Equal(failSetup ? "setupCommands" : "cleanupCommands", exception.ParamName);
        Assert.DoesNotContain(sensitivePayload, exception.Message, StringComparison.Ordinal);
        Assert.IsType<InvalidOperationException>(exception.InnerException);
    }

    [Fact]
    public void Scoped_command_accepts_the_registered_fragment_and_payload_limits()
    {
        var setup = Enumerable.Repeat("S", 126);
        var body = new string('B', 1_048_449);

        var command = MySqlMigrationCommandSpec.CreateScoped(setup, body, ["C"]);

        Assert.Equal(128, command.Fragments.Count);
        Assert.Equal(1_048_576, command.CommandText.Length);
    }

    [Fact]
    public void Scoped_command_rejects_more_than_the_registered_fragment_limit()
    {
        var setup = Enumerable.Repeat("S", 127);

        var exception =
            Assert.Throws<ArgumentException>(() => MySqlMigrationCommandSpec.CreateScoped(setup, "B", ["C"]));

        Assert.Equal("setupCommands", exception.ParamName);
        Assert.Contains("128 total fragments", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Scoped_command_rejects_cleanup_beyond_the_remaining_fragment_limit()
    {
        var cleanup = Enumerable.Repeat("C", 127);

        var exception = Assert.Throws<ArgumentException>(() =>
            MySqlMigrationCommandSpec.CreateScoped(["S"], "B", cleanup));

        Assert.Equal("cleanupCommands", exception.ParamName);
        Assert.Contains("128 total fragments", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Scoped_command_rejects_more_than_the_registered_payload_limit()
    {
        var body = new string('B', 1_048_575);

        var exception = Assert.Throws<ArgumentException>(() =>
            MySqlMigrationCommandSpec.CreateScoped(["S"], body, ["C"]));

        Assert.Equal("bodyCommand", exception.ParamName);
        Assert.Contains("1048576 characters", exception.Message, StringComparison.Ordinal);
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
        var body = "ALTER TABLE `Entries` COMMENT 'safe';\n";
        var cleanup = new[] { "SET @scope = NULL;\n" };
        var commandText = setup[0] + body + cleanup[0];

        var layout = MySqlMigrationCommandLayout.CreateProviderScoped(setup, body, cleanup);

        Assert.Collection(
            layout.Fragments,
            fragment => Assert.Equal(MySqlMigrationCommandFragmentKind.Setup, fragment.Kind),
            fragment => Assert.Equal(MySqlMigrationCommandFragmentKind.Body, fragment.Kind),
            fragment => Assert.Equal(MySqlMigrationCommandFragmentKind.Cleanup, fragment.Kind));
        Assert.Equal(
            commandText,
            string.Concat(layout.Fragments.Select(static fragment => fragment.CommandText.ToString())));
        Assert.Equal("ALTER TABLE `Entries` COMMENT 'safe';\n", layout.BodyCommandText);

        foreach (var fragment in layout.Fragments)
        {
            Assert.True(
                MemoryMarshal.TryGetString(
                    fragment.CommandText,
                    out var backingString,
                    out _,
                    out _));
            Assert.Same(layout.CommandText, backingString);
        }
    }

    [Fact]
    public void Handler_layout_retains_validated_execution_strings()
    {
        var setup = new[] { new string('S', 8), new string('T', 8) };
        var body = new string('B', 32);
        var cleanup = new[] { new string('C', 8), new string('D', 8) };

        var spec = MySqlMigrationCommandSpec.CreateScoped(setup, body, cleanup);
        var layout = Assert.IsType<MySqlMigrationCommandLayout>(spec.ProviderLayout);

        Assert.Same(setup[0], layout.SetupCommandTexts[0]);
        Assert.Same(setup[1], layout.SetupCommandTexts[1]);
        Assert.Same(body, layout.BodyCommandText);
        Assert.Same(cleanup[1], layout.CleanupCommandTexts[0]);
        Assert.Same(cleanup[0], layout.CleanupCommandTexts[1]);
    }

    [Fact]
    public void Provider_layout_rejects_an_empty_body()
    {
        Assert.Throws<ArgumentException>(() =>
            MySqlMigrationCommandLayout.CreateProviderScoped(
                ["SET @scope = 1;\n"],
                "   ",
                ["SET @scope = NULL;\n"]));
    }

    [Fact]
    public void Provider_layout_requires_setup_and_cleanup()
    {
        Assert.Throws<ArgumentException>(() =>
            MySqlMigrationCommandLayout.CreateProviderScoped([], "SELECT 1;", ["SET @scope = NULL;"]));
        Assert.Throws<ArgumentException>(() =>
            MySqlMigrationCommandLayout.CreateProviderScoped(["SET @scope = 1;"], "SELECT 1;", []));
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
