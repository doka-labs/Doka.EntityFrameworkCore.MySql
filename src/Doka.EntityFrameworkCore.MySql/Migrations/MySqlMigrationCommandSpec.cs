namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Describes one command boundary returned by a custom migration-operation
/// handler.
/// </summary>
public sealed class MySqlMigrationCommandSpec
{
    private MySqlMigrationCommandSpec(
        string commandText,
        bool transactionSuppressed
    )
    {
        CommandText = commandText;
        TransactionSuppressed = transactionSuppressed;
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
}
