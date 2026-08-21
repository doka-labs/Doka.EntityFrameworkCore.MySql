namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Describes one command boundary returned by a custom migration-operation
/// handler.
/// </summary>
public sealed class MySqlMigrationCommandSpec
{
    private const int MaximumScopedCommandCount = 128;
    private const int MaximumScopedCommandTextLength = 1_048_576;
    private static readonly IReadOnlyList<MySqlMigrationCommandFragment> s_opaqueFragments =
        Array.Empty<MySqlMigrationCommandFragment>();

    private MySqlMigrationCommandSpec(
        string commandText,
        bool transactionSuppressed
    ) : this(
        commandText,
        transactionSuppressed,
        s_opaqueFragments,
        providerLayout: null) { }

    private MySqlMigrationCommandSpec(
        string commandText,
        bool transactionSuppressed,
        IReadOnlyList<MySqlMigrationCommandFragment> fragments,
        MySqlMigrationCommandLayout? providerLayout
    )
    {
        CommandText = commandText;
        TransactionSuppressed = transactionSuppressed;
        Fragments = fragments;
        ProviderLayout = providerLayout;
    }

    /// <summary>
    /// Gets the SQL text belonging to this command boundary.
    /// </summary>
    public string CommandText { get; }

    /// <summary>
    /// Gets a value indicating whether EF Core must execute this command
    /// outside its migration transaction.
    /// </summary>
    public bool TransactionSuppressed { get; }

    /// <summary>
    /// Gets the immutable provider-validated structure of this command.
    /// </summary>
    /// <remarks>
    /// Commands returned by <see cref="MySqlMigrationOperationContext.RenderStandardOperation"/>
    /// contain setup, body, and cleanup semantics. Commands created by an
    /// external handler through <see cref="Create"/> are intentionally opaque
    /// and return an empty collection. Commands created through
    /// <see cref="CreateScoped"/> expose their validated execution roles.
    /// </remarks>
    public IReadOnlyList<MySqlMigrationCommandFragment> Fragments { get; }

    internal MySqlMigrationCommandLayout? ProviderLayout { get; }

    /// <summary>
    /// Creates one immutable migration command specification.
    /// </summary>
    /// <param name="commandText">The non-empty SQL command text.</param>
    /// <param name="transactionSuppressed">
    /// Whether EF Core must suppress its migration transaction for this command.
    /// </param>
    /// <returns>The validated command specification.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="commandText"/> is empty or contains only whitespace.
    /// </exception>
    public static MySqlMigrationCommandSpec Create(
        string commandText,
        bool transactionSuppressed = false
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandText);

        return new MySqlMigrationCommandSpec(commandText, transactionSuppressed);
    }

    /// <summary>
    /// Creates one provider-executed command scope whose cleanup is attempted
    /// after success, failure, or cancellation.
    /// </summary>
    /// <param name="setupCommands">
    /// Non-empty SQL commands executed in enumeration order before the body.
    /// </param>
    /// <param name="bodyCommand">The non-empty operation body.</param>
    /// <param name="cleanupCommands">
    /// Non-empty idempotent SQL commands. The provider executes them in reverse
    /// enumeration order so callers can describe resource acquisition order.
    /// </param>
    /// <param name="transactionSuppressed">
    /// Whether EF Core must suppress its migration transaction for the whole
    /// scope.
    /// </param>
    /// <returns>An immutable, validated command specification.</returns>
    /// <exception cref="ArgumentNullException">
    /// A command collection or <paramref name="bodyCommand"/> is
    /// <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// A collection is empty, a command contains only whitespace, or the
    /// bounded scope contract is exceeded.
    /// </exception>
    /// <remarks>
    /// The provider snapshots every input before returning. Cleanup runs with
    /// an independent cancellation token and must therefore be idempotent.
    /// Setup, body, and cleanup form one EF migration command boundary and
    /// share one <paramref name="transactionSuppressed"/> value. One scope is
    /// limited to 128 fragments and 1,048,576 characters of SQL text so an
    /// external handler cannot create an unbounded provider-executed
    /// object.
    /// </remarks>
    public static MySqlMigrationCommandSpec CreateScoped(
        IEnumerable<string> setupCommands,
        string bodyCommand,
        IEnumerable<string> cleanupCommands,
        bool transactionSuppressed = false
    )
    {
        ArgumentNullException.ThrowIfNull(setupCommands);
        ArgumentException.ThrowIfNullOrWhiteSpace(bodyCommand);
        ArgumentNullException.ThrowIfNull(cleanupCommands);

        var setupSnapshot = SnapshotCommands(setupCommands, nameof(setupCommands), MaximumScopedCommandCount - 2);
        var cleanupSnapshot = SnapshotCommands(
            cleanupCommands,
            nameof(cleanupCommands),
            MaximumScopedCommandCount - setupSnapshot.Length - 1);

        ValidateScopedCommandTextLength(setupSnapshot, bodyCommand, cleanupSnapshot);

        // Materialize the cleanup stack at the public trust boundary so the
        // runtime executor, fragments, and generated scripts all expose the
        // same actual execution order.
        Array.Reverse(cleanupSnapshot);

        var layout = MySqlMigrationCommandLayout.CreateHandlerScoped(setupSnapshot, bodyCommand, cleanupSnapshot);

        return new MySqlMigrationCommandSpec(layout.CommandText, transactionSuppressed, layout.Fragments, layout);
    }

    internal static MySqlMigrationCommandSpec CreateProviderRendered(
        string commandText,
        bool transactionSuppressed,
        MySqlMigrationCommandLayout providerLayout
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandText);
        ArgumentNullException.ThrowIfNull(providerLayout);

        if (!string.Equals(commandText, providerLayout.CommandText, StringComparison.Ordinal)
            || providerLayout.Fragments.Count == 0)
        {
            throw new ArgumentException(
                "A provider-rendered migration command must contain at least one fragment.",
                nameof(providerLayout));
        }

        return new MySqlMigrationCommandSpec(
            commandText,
            transactionSuppressed,
            providerLayout.Fragments,
            providerLayout);
    }

    private static string[] SnapshotCommands(
        IEnumerable<string> commands,
        string parameterName,
        int maximumCount
    )
    {
        string[] snapshot;

        try
        {
            // Take one sentinel item beyond the bound so an infinite or
            // attacker-controlled enumerable cannot grow provider memory
            // without limit before validation runs.
            snapshot = commands
                .Take(maximumCount + 1)
                .ToArray();
        }
        catch (Exception exception)
        {
            throw new ArgumentException("The command collection could not be enumerated.", parameterName, exception);
        }

        if (snapshot.Length == 0)
        {
            throw new ArgumentException(
                "A scoped migration command requires non-empty SQL command collections.",
                parameterName);
        }

        if (snapshot.Length > maximumCount)
        {
            throw new ArgumentException(
                $"A scoped migration command cannot exceed {MaximumScopedCommandCount} total fragments.",
                parameterName);
        }

        if (snapshot.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException(
                "A scoped migration command cannot contain an empty SQL command.",
                parameterName);
        }

        return snapshot;
    }

    private static void ValidateScopedCommandTextLength(
        IReadOnlyList<string> setupCommands,
        string bodyCommand,
        IReadOnlyList<string> cleanupCommands
    )
    {
        var totalLength = (long)bodyCommand.Length;

        foreach (var command in setupCommands)
        {
            totalLength += command.Length;
        }

        foreach (var command in cleanupCommands)
        {
            totalLength += command.Length;
        }

        if (totalLength > MaximumScopedCommandTextLength)
        {
            throw new ArgumentException(
                $"A scoped migration command cannot exceed {MaximumScopedCommandTextLength} characters.",
                nameof(bodyCommand));
        }
    }
}
