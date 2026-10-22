namespace Doka.EntityFrameworkCore.MySql.Tests;

/// <summary>Verifies opaque and scoped commands, immutable fragments, and collection limits.</summary>
public sealed partial class MySqlMigrationOperationContractTests
{
    /// <summary>
    /// Verifies empty command text is rejected.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_command_text_is_rejected(
        string commandText
    )
    {
        // Arrange
        Action act = () => MySqlMigrationCommandSpec.Create(commandText);

        // Act
        var recordedException = Record.Exception(act);

        // Assert
        Assert.IsType<ArgumentException>(recordedException);
    }

    /// <summary>
    /// Verifies handler created command is opaque.
    /// </summary>
    [Fact]
    public void Handler_created_command_is_opaque()
    {
        // Arrange
        const string commandText = "SELECT 1;";

        // Act
        var command = MySqlMigrationCommandSpec.Create(
            commandText,
            transactionSuppressed: true);

        // Assert
        Assert.Empty(command.Fragments);
        Assert.Equal("SELECT 1;", command.CommandText);
        Assert.True(command.TransactionSuppressed);
    }

    /// <summary>
    /// Verifies opaque commands share the empty fragment snapshot.
    /// </summary>
    [Fact]
    public void Opaque_commands_share_the_empty_fragment_snapshot()
    {
        // Arrange
        var first = MySqlMigrationCommandSpec.Create("SELECT 1;");

        // Act
        var second = MySqlMigrationCommandSpec.Create("SELECT 2;");

        // Assert
        Assert.Same(first.Fragments, second.Fragments);
    }

    /// <summary>
    /// Verifies scoped command snapshots roles and caller owned collections.
    /// </summary>
    [Fact]
    public void Scoped_command_snapshots_roles_and_caller_owned_collections()
    {
        // Arrange
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

        // Act
        setup[0] = "SELECT 'mutated';";
        cleanup.Clear();

        // Assert
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

    /// <summary>
    /// Verifies scoped command rejects every empty role.
    /// </summary>
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
        // Arrange
        var setup = emptySetup ? Array.Empty<string>() : ["SET @scope = 1;"];
        var body = emptyBody ? " " : "SELECT @scope;";
        var cleanup = emptyCleanup ? Array.Empty<string>() : ["SET @scope = NULL;"];
        Action act = () => MySqlMigrationCommandSpec.CreateScoped(setup, body, cleanup);

        // Act
        var recordedException = Record.Exception(act);

        // Assert
        Assert.IsType<ArgumentException>(recordedException);
    }

    /// <summary>
    /// Verifies scoped command rejects whitespace at every collection boundary.
    /// </summary>
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Scoped_command_rejects_whitespace_at_every_collection_boundary(
        bool invalidSetup,
        bool invalidCleanup
    )
    {
        // Arrange
        string[] setup = invalidSetup ? [" "] : ["SET @scope = 1;"];
        string[] cleanup = invalidCleanup ? [" "] : ["SET @scope = NULL;"];
        Action act = () =>
            MySqlMigrationCommandSpec.CreateScoped(setup, "SELECT @scope;", cleanup);

        // Act
        var recordedException = Record.Exception(act);

        // Assert
        Assert.IsType<ArgumentException>(recordedException);
    }

    /// <summary>
    /// Verifies scoped command rejects null at every collection boundary.
    /// </summary>
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Scoped_command_rejects_null_at_every_collection_boundary(
        bool invalidSetup,
        bool invalidCleanup
    )
    {
        // Arrange
        string[] setup = invalidSetup ? [null!] : ["SET @scope = 1;"];
        string[] cleanup = invalidCleanup ? [null!] : ["SET @scope = NULL;"];
        Action act = () =>
            MySqlMigrationCommandSpec.CreateScoped(setup, "SELECT @scope;", cleanup);

        // Act
        var recordedException = Record.Exception(act);

        // Assert
        Assert.IsType<ArgumentException>(recordedException);
    }

    /// <summary>
    /// Verifies scoped command enumerates each foreign collection once.
    /// </summary>
    [Fact]
    public void Scoped_command_enumerates_each_foreign_collection_once()
    {
        // Arrange
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

        // Act
        var command = MySqlMigrationCommandSpec.CreateScoped(
            Setup(),
            "SELECT @scope;",
            Cleanup());

        // Assert
        Assert.Equal(3, command.Fragments.Count);
        Assert.Equal(1, setupEnumerations);
        Assert.Equal(1, cleanupEnumerations);
    }

    /// <summary>
    /// Verifies scoped command wraps collection enumeration failures without copying the payload.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Scoped_command_wraps_collection_enumeration_failures_without_copying_the_payload(
        bool failSetup
    )
    {
        // Arrange
        const string sensitivePayload = "server=private;password=do-not-log";
        static IEnumerable<string> ThrowingCommands(
            string message
        )
        {
            yield return "SET @scope = 1;";
            throw new InvalidOperationException(message);
        }
        Action act = () => MySqlMigrationCommandSpec.CreateScoped(
            failSetup ? ThrowingCommands(sensitivePayload) : ["SET @scope = 1;"],
            "SELECT @scope;",
            failSetup ? ["SET @scope = NULL;"] : ThrowingCommands(sensitivePayload));

        // Act
        var recordedException = Record.Exception(act);

        // Assert
        var exception = Assert.IsType<ArgumentException>(recordedException);
        Assert.Equal(failSetup ? "setupCommands" : "cleanupCommands", exception.ParamName);
        Assert.DoesNotContain(sensitivePayload, exception.Message, StringComparison.Ordinal);
        Assert.IsType<InvalidOperationException>(exception.InnerException);
    }

    /// <summary>
    /// Verifies scoped command accepts the registered fragment and payload limits.
    /// </summary>
    [Fact]
    public void Scoped_command_accepts_the_registered_fragment_and_payload_limits()
    {
        // Arrange
        var setup = Enumerable.Repeat("S", 126);
        var body = new string('B', 1_048_449);

        // Act
        var command = MySqlMigrationCommandSpec.CreateScoped(setup, body, ["C"]);

        // Assert
        Assert.Equal(128, command.Fragments.Count);
        Assert.Equal(1_048_576, command.CommandText.Length);
    }

    /// <summary>
    /// Verifies scoped command rejects more than the registered fragment limit.
    /// </summary>
    [Fact]
    public void Scoped_command_rejects_more_than_the_registered_fragment_limit()
    {
        // Arrange
        var setup = Enumerable.Repeat("S", 127);
        Action act = () => MySqlMigrationCommandSpec.CreateScoped(setup, "B", ["C"]);

        // Act
        var recordedException = Record.Exception(act);

        // Assert
        var exception =
            Assert.IsType<ArgumentException>(recordedException);
        Assert.Equal("setupCommands", exception.ParamName);
        Assert.Contains("128 total fragments", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies scoped command rejects cleanup beyond the remaining fragment limit.
    /// </summary>
    [Fact]
    public void Scoped_command_rejects_cleanup_beyond_the_remaining_fragment_limit()
    {
        // Arrange
        var cleanup = Enumerable.Repeat("C", 127);
        Action act = () =>
            MySqlMigrationCommandSpec.CreateScoped(["S"], "B", cleanup);

        // Act
        var recordedException = Record.Exception(act);

        // Assert
        var exception = Assert.IsType<ArgumentException>(recordedException);
        Assert.Equal("cleanupCommands", exception.ParamName);
        Assert.Contains("128 total fragments", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies scoped command rejects more than the registered payload limit.
    /// </summary>
    [Fact]
    public void Scoped_command_rejects_more_than_the_registered_payload_limit()
    {
        // Arrange
        var body = new string('B', 1_048_575);
        Action act = () =>
            MySqlMigrationCommandSpec.CreateScoped(["S"], body, ["C"]);

        // Act
        var recordedException = Record.Exception(act);

        // Assert
        var exception = Assert.IsType<ArgumentException>(recordedException);
        Assert.Equal("bodyCommand", exception.ParamName);
        Assert.Contains("1048576 characters", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies default fragment is explicitly unclassified.
    /// </summary>
    [Fact]
    public void Default_fragment_is_explicitly_unclassified()
    {
        // Arrange
        var fragment = default(MySqlMigrationCommandFragment);

        // Act
        var kind = fragment.Kind;
        var isEmpty = fragment.CommandText.IsEmpty;

        // Assert
        Assert.Equal(MySqlMigrationCommandFragmentKind.Unspecified, kind);
        Assert.True(isEmpty);
    }
}
