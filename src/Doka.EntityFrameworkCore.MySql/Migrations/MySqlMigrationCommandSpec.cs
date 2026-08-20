namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Describes one command boundary returned by a custom migration-operation
/// handler.
/// </summary>
public sealed class MySqlMigrationCommandSpec
{
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
    /// Gets the immutable provider-owned structure of this command.
    /// </summary>
    /// <remarks>
    /// Commands returned by <see cref="MySqlMigrationOperationContext.RenderStandardOperation"/>
    /// contain setup, body, and cleanup semantics. Commands created by an
    /// external handler through <see cref="Create"/> are intentionally opaque
    /// and return an empty collection.
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
}
