namespace Doka.EntityFrameworkCore.MySql.TestUtilities;

/// <summary>
/// Executes SQL scripts that use the MySQL command-line client's
/// <c>DELIMITER</c> directive.
/// </summary>
/// <remarks>
/// <c>DELIMITER</c> is client syntax and cannot be sent through
/// <see cref="MySqlCommand"/>. This executor removes only those directives and
/// splits statements at the active delimiter while preserving delimiters in
/// quoted values, quoted identifiers, and comments.
/// </remarks>
public static class MySqlClientScriptExecutor
{
    /// <summary>
    /// Executes every parsed statement sequentially on an open connection.
    /// </summary>
    public static async Task ExecuteAsync(
        MySqlConnection connection,
        string script,
        int commandTimeout,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(script);

        foreach (var statement in ParseStatements(script))
        {
            await using var command = connection.CreateCommand();
            command.CommandText = statement;
            command.CommandTimeout = commandTimeout;
            await command
                .ExecuteNonQueryAsync(cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Parses a MySQL client script into the statements sent to the server.
    /// </summary>
    public static IReadOnlyList<string> ParseStatements(
        string script
    )
    {
        ArgumentNullException.ThrowIfNull(script);

        var statements = new List<string>();
        var statement = new StringBuilder();
        var delimiter = ";";
        var state = ParserState.Normal;
        var atLineStart = true;

        for (var index = 0; index < script.Length;)
        {
            if (state == ParserState.Normal
                && atLineStart
                && TryReadDelimiterDirective(script, index, out var nextIndex, out var nextDelimiter))
            {
                delimiter = nextDelimiter;
                index = nextIndex;
                atLineStart = true;
                continue;
            }

            var current = script[index];
            atLineStart = false;

            switch (state)
            {
                case ParserState.Normal:
                    if (script
                        .AsSpan(index)
                        .StartsWith(delimiter, StringComparison.Ordinal))
                    {
                        AddStatement(statements, statement);
                        index += delimiter.Length;
                        continue;
                    }

                    if (current is '\'' or '"' or '`')
                    {
                        state = current switch
                        {
                            '\'' => ParserState.SingleQuoted,
                            '"' => ParserState.DoubleQuoted,
                            _ => ParserState.QuotedIdentifier,
                        };
                    }
                    else if (current == '#'
                             || (current == '-'
                             && index + 1 < script.Length
                             && script[index + 1] == '-'
                             && (index + 2 == script.Length || char.IsWhiteSpace(script[index + 2]))))
                    {
                        state = ParserState.LineComment;
                    }
                    else if (current == '/'
                             && index + 1 < script.Length
                             && script[index + 1] == '*')
                    {
                        statement.Append(current);
                        statement.Append(script[index + 1]);
                        state = ParserState.BlockComment;
                        index += 2;
                        continue;
                    }

                    break;

                case ParserState.SingleQuoted:
                case ParserState.DoubleQuoted:
                case ParserState.QuotedIdentifier:
                    var quote = state switch
                    {
                        ParserState.SingleQuoted => '\'', ParserState.DoubleQuoted => '"', _ => '`',
                    };

                    if (current == '\\'
                        && index + 1 < script.Length)
                    {
                        statement.Append(current);
                        statement.Append(script[index + 1]);
                        index += 2;
                        continue;
                    }

                    if (current == quote)
                    {
                        if (index + 1 < script.Length
                            && script[index + 1] == quote)
                        {
                            statement.Append(current);
                            statement.Append(script[index + 1]);
                            index += 2;
                            continue;
                        }

                        state = ParserState.Normal;
                    }

                    break;

                case ParserState.LineComment:
                    if (current is '\r' or '\n')
                    {
                        state = ParserState.Normal;
                    }

                    break;

                case ParserState.BlockComment:
                    if (current == '*'
                        && index + 1 < script.Length
                        && script[index + 1] == '/')
                    {
                        statement.Append(current);
                        statement.Append(script[index + 1]);
                        state = ParserState.Normal;
                        index += 2;
                        continue;
                    }

                    break;

                default:
                    throw new UnreachableException();
            }

            statement.Append(current);
            index++;

            if (current == '\r')
            {
                if (index < script.Length
                    && script[index] == '\n')
                {
                    statement.Append(script[index]);
                    index++;
                }

                atLineStart = true;
            }
            else if (current == '\n')
            {
                atLineStart = true;
            }
        }

        if (state is ParserState.SingleQuoted
            or ParserState.DoubleQuoted
            or ParserState.QuotedIdentifier
            or ParserState.BlockComment)
        {
            throw new InvalidOperationException(
                "The MySQL client script ends inside a quoted value, identifier, or block comment.");
        }

        AddStatement(statements, statement);
        return statements.AsReadOnly();
    }

    private static bool TryReadDelimiterDirective(
        string script,
        int lineStart,
        out int nextIndex,
        out string delimiter
    )
    {
        const string directive = "DELIMITER";
        var lineEnd = lineStart;

        while (lineEnd < script.Length
               && script[lineEnd] is not '\r' and not '\n')
        {
            lineEnd++;
        }

        var line = script
            .AsSpan(lineStart, lineEnd - lineStart)
            .Trim();
        if (!line.StartsWith(directive, StringComparison.OrdinalIgnoreCase))
        {
            nextIndex = lineStart;
            delimiter = string.Empty;
            return false;
        }

        if (line.Length == directive.Length)
        {
            throw new InvalidOperationException("A MySQL DELIMITER directive must specify one non-whitespace token.");
        }

        if (!char.IsWhiteSpace(line[directive.Length]))
        {
            nextIndex = lineStart;
            delimiter = string.Empty;
            return false;
        }

        var delimiterSpan = line[directive.Length..]
            .Trim();
        if (delimiterSpan.IsEmpty
            || delimiterSpan.IndexOfAny(" \t\r\n") >= 0)
        {
            throw new InvalidOperationException("A MySQL DELIMITER directive must specify one non-whitespace token.");
        }

        delimiter = delimiterSpan.ToString();
        nextIndex = lineEnd;
        if (nextIndex < script.Length
            && script[nextIndex] == '\r')
        {
            nextIndex++;
        }

        if (nextIndex < script.Length
            && script[nextIndex] == '\n')
        {
            nextIndex++;
        }

        return true;
    }

    private static void AddStatement(
        List<string> statements,
        StringBuilder statement
    )
    {
        var commandText = statement
            .ToString()
            .Trim();
        statement.Clear();

        if (commandText.Length != 0)
        {
            statements.Add(commandText);
        }
    }

    private enum ParserState
    {
        Normal,
        SingleQuoted,
        DoubleQuoted,
        QuotedIdentifier,
        LineComment,
        BlockComment,
    }
}
