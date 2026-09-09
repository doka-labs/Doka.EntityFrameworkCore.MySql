namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Describes the completed processing of one custom migration operation.
/// </summary>
public sealed class MySqlMigrationOperationResult
{
    private static readonly IReadOnlyList<MySqlMigrationCommandSpec> s_emptyCommands =
        Array.Empty<MySqlMigrationCommandSpec>();
    private static readonly Regex s_outcomeCodePattern = new(
        "^[a-z][a-z0-9_]{0,63}$",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    private MySqlMigrationOperationResult(
        IReadOnlyList<MySqlMigrationCommandSpec> commands,
        string outcomeCode,
        MySqlMigrationOperationResultKind kind
    )
    {
        Commands = commands;
        OutcomeCode = outcomeCode;
        Kind = kind;
    }

    /// <summary>
    /// Gets the immutable, ordered command sequence. The sequence is empty
    /// only when the operation was consumed without generating SQL.
    /// </summary>
    public IReadOnlyList<MySqlMigrationCommandSpec> Commands { get; }

    /// <summary>
    /// Gets the stable, low-cardinality outcome code emitted by provider
    /// diagnostics.
    /// </summary>
    public string OutcomeCode { get; }

    internal MySqlMigrationOperationResultKind Kind { get; }

    /// <summary>
    /// Creates a generated result and snapshots its command sequence.
    /// </summary>
    /// <param name="commands">One or more validated commands.</param>
    /// <param name="outcomeCode">
    /// A stable code matching <c>^[a-z][a-z0-9_]{0,63}$</c>. Do not include SQL,
    /// object names, identifiers, or other unbounded values.
    /// </param>
    /// <returns>The immutable generated result.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="commands"/> or <paramref name="outcomeCode"/> is
    /// <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="outcomeCode"/> does not satisfy the bounded contract, or
    /// <paramref name="commands"/> is empty or contains a
    /// <see langword="null"/> element.
    /// </exception>
    public static MySqlMigrationOperationResult Generated(
        IEnumerable<MySqlMigrationCommandSpec> commands,
        string outcomeCode
    )
    {
        ArgumentNullException.ThrowIfNull(commands);
        ValidateOutcomeCode(outcomeCode);

        var snapshot = commands.ToArray();

        if (snapshot.Length == 0 || snapshot.Any(command => command is null))
        {
            throw new ArgumentException(
                "A generated result must contain at least one non-null command.",
                nameof(commands));
        }

        return new MySqlMigrationOperationResult(
            Array.AsReadOnly(snapshot),
            outcomeCode,
            MySqlMigrationOperationResultKind.Generated);
    }

    /// <summary>
    /// Creates a successful result for an operation that has no database
    /// effect and therefore generates no SQL command.
    /// </summary>
    /// <param name="outcomeCode">
    /// A stable code matching <c>^[a-z][a-z0-9_]{0,63}$</c>. Do not include SQL,
    /// object names, identifiers, or other unbounded values.
    /// </param>
    /// <returns>The commandless consumed result.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="outcomeCode"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="outcomeCode"/> does not satisfy the bounded contract.
    /// </exception>
    public static MySqlMigrationOperationResult Consumed(
        string outcomeCode
    )
    {
        ValidateOutcomeCode(outcomeCode);

        return new MySqlMigrationOperationResult(
            s_emptyCommands,
            outcomeCode,
            MySqlMigrationOperationResultKind.Consumed);
    }

    internal static bool IsValidOutcomeCode(string outcomeCode)
        => s_outcomeCodePattern.IsMatch(outcomeCode);

    private static void ValidateOutcomeCode(
        string outcomeCode
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outcomeCode);

        if (!IsValidOutcomeCode(outcomeCode))
        {
            throw new ArgumentException(
                "The outcome code must match ^[a-z][a-z0-9_]{0,63}$.",
                nameof(outcomeCode));
        }
    }
}

internal enum MySqlMigrationOperationResultKind : byte
{
    Generated = 1,
    Consumed = 2,
}
