namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Static helpers shared by the per-aspect scaffolding loaders. The helpers cover
/// scalar-string execution, value-generated resolution, character-set derivation,
/// canonical JSON_VALID column-name extraction, referential-action mapping, and parametrized
/// TABLE_NAME IN (@t0, @t1, ...) filter binding.
/// </summary>
internal static class ScaffoldingHelpers
{
    /// <summary>
    /// Runs <paramref name="commandText"/> as a scalar query and returns the result
    /// as a string. Returns an empty string when the scalar is null or DBNull.
    /// </summary>
    public static string ExecuteScalarString(
        DbConnection connection,
        string commandText
    )
    {
        using var command = connection.CreateCommand();
        command.CommandText = commandText;

        var result = command.ExecuteScalar();

        return result switch
        {
            null => string.Empty,
            DBNull => string.Empty,
            _ => Convert.ToString(result, CultureInfo.InvariantCulture) ?? string.Empty,
        };
    }

    /// <summary>
    /// Resolves the EF Core ValueGenerated kind from the INFORMATION_SCHEMA.COLUMNS
    /// EXTRA column. Currently, maps auto_increment to OnAdd; everything else is null.
    /// </summary>
    public static ValueGenerated? ResolveValueGenerated(
        string? extra
    )
    {
        if (string.IsNullOrWhiteSpace(extra))
        {
            return null;
        }

        return extra.Contains("auto_increment", StringComparison.OrdinalIgnoreCase)
            ? ValueGenerated.OnAdd
            : null;
    }

    /// <summary>
    /// Resolves the IsStored flag for computed columns from the EXTRA column.
    /// Returns true for STORED GENERATED, false for VIRTUAL GENERATED, null otherwise.
    /// </summary>
    public static bool? ResolveIsStored(
        string? extra
    )
    {
        if (string.IsNullOrWhiteSpace(extra))
        {
            return null;
        }

        if (extra.Contains("STORED GENERATED", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (extra.Contains("VIRTUAL GENERATED", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return null;
    }

    /// <summary>
    /// Maps INFORMATION_SCHEMA.REFERENTIAL_CONSTRAINTS.DELETE_RULE strings to the
    /// EF Core ReferentialAction enum. Unrecognized values return null.
    /// </summary>
    public static ReferentialAction? ResolveReferentialAction(
        string? deleteRule
    ) => deleteRule?.ToUpperInvariant() switch
    {
        "CASCADE" => ReferentialAction.Cascade,
        "SET NULL" => ReferentialAction.SetNull,
        "SET DEFAULT" => ReferentialAction.SetDefault,
        "RESTRICT" => ReferentialAction.Restrict,
        "NO ACTION" => ReferentialAction.NoAction,
        _ => null,
    };

    /// <summary>
    /// Derives the MySQL character-set name from a collation by stripping at the
    /// first underscore (utf8mb4_0900_ai_ci -> utf8mb4). Returns null when the
    /// collation has no underscore separator or is null/whitespace.
    /// </summary>
    public static string? DeriveCharSetFromCollation(
        string? collation
    )
    {
        if (string.IsNullOrWhiteSpace(collation))
        {
            return null;
        }

        var separatorIndex = collation.IndexOf('_');

        return separatorIndex > 0 ? collation[..separatorIndex] : null;
    }

    /// <summary>
    /// Extracts the column name from a canonical MariaDB JSON-alias CHECK_CLAUSE like
    /// <c>json_valid(`payload`)</c> or <c>json_valid(payload)</c>.
    /// Returns null when the complete clause is not a single JSON_VALID call over one
    /// column identifier.
    /// </summary>
    public static string? ExtractJsonValidColumnName(
        string checkClause
    )
    {
        ArgumentNullException.ThrowIfNull(checkClause);

        const string functionName = "json_valid";
        var clause = checkClause.AsSpan().Trim();

        if (!clause.StartsWith(functionName, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        clause = clause[functionName.Length..].TrimStart();

        if (clause.Length < 3
            || clause[0] != '('
            || clause[^1] != ')')
        {
            return null;
        }

        var columnReference = clause[1..^1].Trim();

        if (columnReference.Length >= 2
            && columnReference[0] == '`'
            && columnReference[^1] == '`')
        {
            var quotedIdentifier = columnReference[1..^1];

            if (quotedIdentifier.IsEmpty)
            {
                return null;
            }

            for (var index = 0; index < quotedIdentifier.Length; index++)
            {
                if (quotedIdentifier[index] != '`')
                {
                    continue;
                }

                if (index + 1 >= quotedIdentifier.Length
                    || quotedIdentifier[index + 1] != '`')
                {
                    return null;
                }

                index++;
            }

            var identifier = quotedIdentifier.ToString();

            return identifier.Contains("``", StringComparison.Ordinal)
                ? identifier.Replace("``", "`", StringComparison.Ordinal)
                : identifier;
        }

        foreach (var character in columnReference)
        {
            if (!char.IsLetterOrDigit(character)
                && character is not '_'
                && character is not '$')
            {
                return null;
            }
        }

        return columnReference.IsEmpty ? null : columnReference.ToString();
    }

    /// <summary>
    /// Appends a parametrized TABLE_NAME IN (@t0, @t1, ...) clause to <paramref name="sql"/>
    /// and binds the values via <paramref name="command"/>. When the filter is MatchAll
    /// the method is a no-op so the caller's query stays unfiltered. Returns the number
    /// of parameters bound so the caller can short-circuit further filter logic.
    /// </summary>
    public static int AppendTableNameFilter(
        StringBuilder sql,
        DbCommand command,
        TableFilter filter,
        string columnReference = "TABLE_NAME"
    )
    {
        if (filter.Tables is null
            || filter.Tables.Count == 0)
        {
            return 0;
        }

        sql
            .Append(" AND ")
            .Append(columnReference)
            .Append(" IN (");

        var index = 0;
        foreach (var tableName in filter.Tables)
        {
            if (index > 0)
            {
                sql.Append(", ");
            }

            var parameterName = "@t" + index.ToString(CultureInfo.InvariantCulture);
            sql.Append(parameterName);

            var parameter = command.CreateParameter();
            parameter.ParameterName = parameterName;
            parameter.Value = tableName;
            command.Parameters.Add(parameter);

            index++;
        }

        sql.Append(')');

        return index;
    }
}

/// <summary>
/// Equality comparer for (TableName, ColumnName) tuples with case-insensitive column
/// name comparison. Table names are compared case-sensitively because MySQL respects
/// the underlying filesystem case-sensitivity for table identifiers; column names in
/// MariaDB CHECK_CLAUSE entries may differ in case from the declared column.
/// </summary>
internal sealed class CaseInsensitiveColumnTupleComparer : IEqualityComparer<(string TableName, string ColumnName)>
{
    public static readonly CaseInsensitiveColumnTupleComparer Instance = new();

    public bool Equals(
        (string TableName, string ColumnName) x,
        (string TableName, string ColumnName) y
    )
    {
        return string.Equals(x.TableName, y.TableName, StringComparison.Ordinal)
            && string.Equals(x.ColumnName, y.ColumnName, StringComparison.OrdinalIgnoreCase);
    }

    public int GetHashCode(
        (string TableName, string ColumnName) obj
    )
    {
        return HashCode.Combine(
            StringComparer.Ordinal.GetHashCode(obj.TableName),
            StringComparer.OrdinalIgnoreCase.GetHashCode(obj.ColumnName));
    }
}
