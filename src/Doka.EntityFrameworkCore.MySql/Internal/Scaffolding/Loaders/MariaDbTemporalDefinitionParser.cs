namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Reads application-time metadata from MariaDB's canonical
/// <c>SHOW CREATE TABLE</c> output.
/// </summary>
/// <remarks>
/// MariaDB supports application-time periods before 11.4, but exposes the
/// PERIODS and KEY_PERIOD_USAGE catalogs only from 11.4. The parser is limited
/// to server-rendered table clauses and never infers periods from user column
/// names. Quoted literals are discarded before grammar tokens are inspected so
/// comments and defaults cannot masquerade as temporal clauses.
/// </remarks>
internal static class MariaDbTemporalDefinitionParser
{
    public static MariaDbApplicationTimeDefinition? ParseApplicationTime(
        string createTableSql
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(createTableSql);

        var clauses = SplitTableClauses(Tokenize(createTableSql));
        MariaDbApplicationTimeDefinition? definition = null;
        var withoutOverlapsConstraints = new List<(string ConstraintName, string PeriodName)>();

        foreach (var clause in clauses)
        {
            if (IsKeyword(clause, 0, "PERIOD")
                && IsKeyword(clause, 1, "FOR"))
            {
                var period = ParsePeriodClause(clause);

                if (period is null)
                {
                    continue;
                }

                if (definition is not null)
                {
                    throw new InvalidOperationException(
                        "SHOW CREATE TABLE exposes more than one application-time period.");
                }

                definition = period;
                continue;
            }

            if (ContainsKeywordPair(clause, "WITHOUT", "OVERLAPS"))
            {
                withoutOverlapsConstraints.Add(ParseWithoutOverlapsClause(clause));
            }
        }

        if (definition is null)
        {
            if (withoutOverlapsConstraints.Count > 0)
            {
                throw new InvalidOperationException(
                    "SHOW CREATE TABLE exposes WITHOUT OVERLAPS without an application-time period.");
            }

            return null;
        }

        foreach (var (constraintName, periodName) in withoutOverlapsConstraints)
        {
            if (!string.Equals(periodName, definition.PeriodName, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"WITHOUT OVERLAPS constraint '{constraintName}' references unknown period " + $"'{periodName}'.");
            }

            definition.WithoutOverlapsConstraints.Add(constraintName);
        }

        return definition;
    }

    private static MariaDbApplicationTimeDefinition? ParsePeriodClause(
        IReadOnlyList<Token> clause
    )
    {
        if (clause.Count < 8
            || !TryReadIdentifier(clause, 2, out var periodName))
        {
            throw new InvalidOperationException("SHOW CREATE TABLE exposes a malformed PERIOD FOR clause.");
        }

        if (string.Equals(periodName, "SYSTEM_TIME", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (!IsSymbol(clause, 3, "(")
            || !TryReadIdentifier(clause, 4, out var startColumnName)
            || !IsSymbol(clause, 5, ",")
            || !TryReadIdentifier(clause, 6, out var endColumnName)
            || !IsSymbol(clause, 7, ")"))
        {
            throw new InvalidOperationException(
                $"SHOW CREATE TABLE exposes malformed boundaries for period '{periodName}'.");
        }

        return new MariaDbApplicationTimeDefinition(periodName, startColumnName, endColumnName);
    }

    private static (string ConstraintName, string PeriodName) ParseWithoutOverlapsClause(
        IReadOnlyList<Token> clause
    )
    {
        string constraintName;

        if (IsKeyword(clause, 0, "PRIMARY")
            && IsKeyword(clause, 1, "KEY"))
        {
            constraintName = "PRIMARY";
        }
        else if (IsKeyword(clause, 0, "UNIQUE")
                 && (IsKeyword(clause, 1, "KEY") || IsKeyword(clause, 1, "INDEX"))
                 && TryReadIdentifier(clause, 2, out var uniqueName))
        {
            constraintName = uniqueName;
        }
        else
        {
            throw new InvalidOperationException(
                "SHOW CREATE TABLE exposes WITHOUT OVERLAPS on an unsupported key clause.");
        }

        for (var index = 1; index < clause.Count; index++)
        {
            if (!IsKeyword(clause, index, "WITHOUT")
                || !IsKeyword(clause, index + 1, "OVERLAPS")
                || !TryReadIdentifier(clause, index - 1, out var periodName))
            {
                continue;
            }

            return (constraintName, periodName);
        }

        throw new InvalidOperationException(
            $"SHOW CREATE TABLE exposes malformed WITHOUT OVERLAPS metadata for '{constraintName}'.");
    }

    private static List<IReadOnlyList<Token>> SplitTableClauses(
        IReadOnlyList<Token> tokens
    )
    {
        var openingParenthesis = -1;

        for (var index = 0; index < tokens.Count; index++)
        {
            if (IsSymbol(tokens, index, "("))
            {
                openingParenthesis = index;
                break;
            }
        }

        if (openingParenthesis < 0)
        {
            throw new InvalidOperationException("SHOW CREATE TABLE does not contain a table definition.");
        }

        var clauses = new List<IReadOnlyList<Token>>();
        var clause = new List<Token>();
        var depth = 1;

        for (var index = openingParenthesis + 1; index < tokens.Count; index++)
        {
            var token = tokens[index];

            if (token.IsSymbol("("))
            {
                depth++;
                clause.Add(token);
                continue;
            }

            if (token.IsSymbol(")"))
            {
                depth--;

                if (depth == 0)
                {
                    AddClause(clauses, clause);
                    return clauses;
                }

                clause.Add(token);
                continue;
            }

            if (depth == 1
                && token.IsSymbol(","))
            {
                AddClause(clauses, clause);
                clause = [];
                continue;
            }

            clause.Add(token);
        }

        throw new InvalidOperationException("SHOW CREATE TABLE contains an unterminated table definition.");
    }

    private static void AddClause(
        ICollection<IReadOnlyList<Token>> clauses,
        List<Token> clause
    )
    {
        if (clause.Count > 0)
        {
            clauses.Add(clause.ToArray());
        }
    }

    private static List<Token> Tokenize(
        string sql
    )
    {
        var tokens = new List<Token>();

        for (var index = 0; index < sql.Length;)
        {
            var current = sql[index];

            if (char.IsWhiteSpace(current))
            {
                index++;
                continue;
            }

            if (current is '\'' or '"')
            {
                index = SkipQuotedLiteral(sql, index, current);
                continue;
            }

            if (current == '`')
            {
                var identifier = ReadQuotedIdentifier(sql, ref index);
                tokens.Add(new Token(identifier, TokenKind.QuotedIdentifier));
                continue;
            }

            if (current is '(' or ')' or ',')
            {
                tokens.Add(new Token(current.ToString(), TokenKind.Symbol));
                index++;
                continue;
            }

            var start = index;
            while (index < sql.Length
                   && !char.IsWhiteSpace(sql[index])
                   && sql[index] is not ('(' or ')' or ',' or '`' or '\'' or '"'))
            {
                index++;
            }

            tokens.Add(new Token(sql[start..index], TokenKind.Word));
        }

        return tokens;
    }

    private static int SkipQuotedLiteral(
        string sql,
        int openingQuote,
        char quote
    )
    {
        for (var index = openingQuote + 1; index < sql.Length; index++)
        {
            if (sql[index] == '\\')
            {
                index++;
                continue;
            }

            if (sql[index] != quote)
            {
                continue;
            }

            if (index + 1 < sql.Length
                && sql[index + 1] == quote)
            {
                index++;
                continue;
            }

            return index + 1;
        }

        throw new InvalidOperationException("SHOW CREATE TABLE contains an unterminated quoted literal.");
    }

    private static string ReadQuotedIdentifier(
        string sql,
        ref int index
    )
    {
        var identifier = new StringBuilder();
        index++;

        while (index < sql.Length)
        {
            if (sql[index] != '`')
            {
                identifier.Append(sql[index]);
                index++;
                continue;
            }

            if (index + 1 < sql.Length
                && sql[index + 1] == '`')
            {
                identifier.Append('`');
                index += 2;
                continue;
            }

            index++;
            return identifier.ToString();
        }

        throw new InvalidOperationException("SHOW CREATE TABLE contains an unterminated quoted identifier.");
    }

    private static bool ContainsKeywordPair(
        IReadOnlyList<Token> tokens,
        string first,
        string second
    )
    {
        for (var index = 0; index < tokens.Count - 1; index++)
        {
            if (IsKeyword(tokens, index, first)
                && IsKeyword(tokens, index + 1, second))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryReadIdentifier(
        IReadOnlyList<Token> tokens,
        int index,
        out string identifier
    )
    {
        if (index >= 0
            && index < tokens.Count
            && tokens[index].Kind is TokenKind.Word or TokenKind.QuotedIdentifier)
        {
            identifier = tokens[index].Value;
            return !string.IsNullOrWhiteSpace(identifier);
        }

        identifier = string.Empty;
        return false;
    }

    private static bool IsKeyword(
        IReadOnlyList<Token> tokens,
        int index,
        string keyword
    ) => index >= 0
        && index < tokens.Count
        && tokens[index].Kind == TokenKind.Word
        && string.Equals(tokens[index].Value, keyword, StringComparison.OrdinalIgnoreCase);

    private static bool IsSymbol(
        IReadOnlyList<Token> tokens,
        int index,
        string symbol
    ) => index >= 0
        && index < tokens.Count
        && tokens[index]
            .IsSymbol(symbol);

    private readonly struct Token
    {
        public Token(
            string value,
            TokenKind kind
        )
        {
            Value = value;
            Kind = kind;
        }

        public string Value { get; }

        public TokenKind Kind { get; }

        public bool IsSymbol(
            string symbol
        ) => Kind == TokenKind.Symbol
            && string.Equals(Value, symbol, StringComparison.Ordinal);
    }

    private enum TokenKind
    {
        Word,
        QuotedIdentifier,
        Symbol,
    }
}

/// <summary>
/// Application-time definition reconstructed from one MariaDB table.
/// </summary>
internal sealed class MariaDbApplicationTimeDefinition
{
    public MariaDbApplicationTimeDefinition(
        string periodName,
        string startColumnName,
        string endColumnName
    )
    {
        PeriodName = periodName;
        StartColumnName = startColumnName;
        EndColumnName = endColumnName;
    }

    public string PeriodName { get; }

    public string StartColumnName { get; }

    public string EndColumnName { get; }

    public List<string> WithoutOverlapsConstraints { get; } = [];
}
