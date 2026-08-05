namespace Doka.EntityFrameworkCore.MySql;

internal sealed partial class MySqlQuerySqlGenerator
{
    /// <summary>
    /// Emits the engine-specific table source for a provider temporal query root.
    /// </summary>
    /// <remarks>
    /// MariaDB owns the native <c>FOR SYSTEM_TIME</c> grammar. MySQL uses a
    /// provider-owned current/history union whose boundary predicates deliberately
    /// match the public query operators rather than approximating them in LINQ.
    /// </remarks>
    private bool TryGenerateTemporalTable(
        TableExpression tableExpression
    )
    {
        var operationAnnotation = tableExpression.FindAnnotation(MySqlAnnotationNames.TemporalOperation);

        if (operationAnnotation?.Value is not MySqlTemporalQueryOperation operation)
        {
            return false;
        }

        if (_mutationTargetTable is not null)
        {
            throw new InvalidOperationException(
                "Temporal query roots cannot be used as ExecuteUpdate or ExecuteDelete sources.");
        }

        return Profile.GetSupport(ProviderCapability.TemporalTables) switch
        {
            ProviderSupportStatus.Native => GenerateNativeTemporalTable(tableExpression, operation),
            ProviderSupportStatus.Emulated => GenerateEmulatedTemporalTable(tableExpression, operation),
            ProviderSupportStatus.UnsupportedByEngine => throw new InvalidOperationException(
                "The configured database engine cannot supply temporal table queries."),
            _ => throw new InvalidOperationException(
                $"Unknown temporal-table support status for '{Profile.Engine.Family}'."),
        };
    }

    private bool GenerateNativeTemporalTable(
        TableExpression tableExpression,
        MySqlTemporalQueryOperation operation
    )
    {
        if (operation == MySqlTemporalQueryOperation.ContainedIn)
        {
            GenerateNativeContainedInTable(tableExpression);
            return true;
        }

        AppendDelimitedTable(tableExpression.Name, tableExpression.Schema);
        Sql.Append(" FOR SYSTEM_TIME ");

        switch (operation)
        {
            case MySqlTemporalQueryOperation.AsOf:
                Sql.Append("AS OF ");
                AppendTemporalLiteral(GetRequiredTemporalPoint(tableExpression));
                break;

            case MySqlTemporalQueryOperation.FromTo:
                Sql.Append("FROM ");
                AppendTemporalLiteral(GetRequiredTemporalRangeStart(tableExpression));
                Sql.Append(" TO ");
                AppendTemporalLiteral(GetRequiredTemporalRangeEnd(tableExpression));
                break;

            case MySqlTemporalQueryOperation.Between:
                Sql.Append("BETWEEN ");
                AppendTemporalLiteral(GetRequiredTemporalRangeStart(tableExpression));
                Sql.Append(" AND ");
                AppendTemporalLiteral(GetRequiredTemporalRangeEnd(tableExpression));
                break;

            case MySqlTemporalQueryOperation.All:
                Sql.Append("ALL");
                break;

            default:
                throw UnknownTemporalOperation(operation);
        }

        AppendTableAlias(tableExpression.Alias);
        return true;
    }

    private void GenerateNativeContainedInTable(
        TableExpression tableExpression
    )
    {
        Sql.AppendLine("(");
        using (Sql.Indent())
        {
            Sql.Append("SELECT * FROM ");
            AppendDelimitedTable(tableExpression.Name, tableExpression.Schema);
            Sql.AppendLine(" FOR SYSTEM_TIME ALL");
            Sql.Append("WHERE ");
            AppendDelimitedColumn(
                GetRequiredStringAnnotation(tableExpression, MySqlAnnotationNames.TemporalPeriodStartColumn));
            Sql.Append(" >= ");
            AppendTemporalLiteral(GetRequiredTemporalRangeStart(tableExpression));
            Sql.Append(" AND ");
            AppendDelimitedColumn(
                GetRequiredStringAnnotation(tableExpression, MySqlAnnotationNames.TemporalPeriodEndColumn));
            Sql.Append(" <= ");
            AppendTemporalLiteral(GetRequiredTemporalRangeEnd(tableExpression));
            Sql.AppendLine();
        }

        Sql.Append(")");
        AppendTableAlias(tableExpression.Alias);
    }

    private bool GenerateEmulatedTemporalTable(
        TableExpression tableExpression,
        MySqlTemporalQueryOperation operation
    )
    {
        var historyTableName = GetRequiredStringAnnotation(tableExpression, MySqlAnnotationNames.TemporalHistoryTable);
        var historyTableSchema = tableExpression.FindAnnotation(MySqlAnnotationNames.TemporalHistorySchema)
                ?.Value as string
            ?? tableExpression.Schema;

        Sql.AppendLine("(");
        using (Sql.Indent())
        {
            // UNION ALL preserves separate versions with identical payloads and avoids the
            // duplicate-elimination work that UNION would add to every emulated temporal query.
            GenerateEmulatedTemporalSelect(tableExpression.Name, tableExpression.Schema, tableExpression, operation);
            Sql.AppendLine();
            Sql.AppendLine("UNION ALL");
            GenerateEmulatedTemporalSelect(historyTableName, historyTableSchema, tableExpression, operation);
            Sql.AppendLine();
        }

        Sql.Append(")");
        AppendTableAlias(tableExpression.Alias);
        return true;
    }

    private void GenerateEmulatedTemporalSelect(
        string tableName,
        string? schema,
        TableExpression tableExpression,
        MySqlTemporalQueryOperation operation
    )
    {
        Sql.Append("SELECT * FROM ");
        AppendDelimitedTable(tableName, schema);

        if (operation == MySqlTemporalQueryOperation.All)
        {
            return;
        }

        var periodStartColumn = GetRequiredStringAnnotation(
            tableExpression,
            MySqlAnnotationNames.TemporalPeriodStartColumn);
        var periodEndColumn = GetRequiredStringAnnotation(
            tableExpression,
            MySqlAnnotationNames.TemporalPeriodEndColumn);

        Sql.AppendLine();
        Sql.Append("WHERE ");

        switch (operation)
        {
            case MySqlTemporalQueryOperation.AsOf:
                AppendDelimitedColumn(periodStartColumn);
                Sql.Append(" <= ");
                AppendTemporalLiteral(GetRequiredTemporalPoint(tableExpression));
                Sql.Append(" AND ");
                AppendDelimitedColumn(periodEndColumn);
                Sql.Append(" > ");
                AppendTemporalLiteral(GetRequiredTemporalPoint(tableExpression));
                break;

            case MySqlTemporalQueryOperation.FromTo:
                AppendDelimitedColumn(periodStartColumn);
                Sql.Append(" < ");
                AppendTemporalLiteral(GetRequiredTemporalRangeEnd(tableExpression));
                Sql.Append(" AND ");
                AppendDelimitedColumn(periodEndColumn);
                Sql.Append(" > ");
                AppendTemporalLiteral(GetRequiredTemporalRangeStart(tableExpression));
                break;

            case MySqlTemporalQueryOperation.Between:
                AppendDelimitedColumn(periodStartColumn);
                Sql.Append(" <= ");
                AppendTemporalLiteral(GetRequiredTemporalRangeEnd(tableExpression));
                Sql.Append(" AND ");
                AppendDelimitedColumn(periodEndColumn);
                Sql.Append(" > ");
                AppendTemporalLiteral(GetRequiredTemporalRangeStart(tableExpression));
                break;

            case MySqlTemporalQueryOperation.ContainedIn:
                AppendDelimitedColumn(periodStartColumn);
                Sql.Append(" >= ");
                AppendTemporalLiteral(GetRequiredTemporalRangeStart(tableExpression));
                Sql.Append(" AND ");
                AppendDelimitedColumn(periodEndColumn);
                Sql.Append(" <= ");
                AppendTemporalLiteral(GetRequiredTemporalRangeEnd(tableExpression));
                break;

            default:
                throw UnknownTemporalOperation(operation);
        }
    }

    private void AppendDelimitedTable(
        string name,
        string? schema
    ) => Sql.Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(name, schema));

    private void AppendDelimitedColumn(
        string name
    ) => Sql.Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(name));

    private void AppendTableAlias(
        string alias
    )
    {
        Sql.Append(AliasSeparator);
        Sql.Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(alias));
    }

    private void AppendTemporalLiteral(
        DateTime value
    ) => Sql.Append(MySqlDateTimeTypeMapping.Default.GenerateSqlLiteral(value));

    private static DateTime GetRequiredTemporalPoint(
        TableExpression tableExpression
    ) => GetRequiredDateTimeAnnotation(tableExpression, MySqlAnnotationNames.TemporalPointInTime);

    private static DateTime GetRequiredTemporalRangeStart(
        TableExpression tableExpression
    ) => GetRequiredDateTimeAnnotation(tableExpression, MySqlAnnotationNames.TemporalRangeStart);

    private static DateTime GetRequiredTemporalRangeEnd(
        TableExpression tableExpression
    ) => GetRequiredDateTimeAnnotation(tableExpression, MySqlAnnotationNames.TemporalRangeEnd);

    private static DateTime GetRequiredDateTimeAnnotation(
        TableExpression tableExpression,
        string annotationName
    ) => tableExpression.FindAnnotation(annotationName)
        ?.Value is DateTime value
        ? value
        : throw new InvalidOperationException(
            $"Temporal table source '{tableExpression.Name}' is missing '{annotationName}'.");

    private static string GetRequiredStringAnnotation(
        TableExpression tableExpression,
        string annotationName
    ) => tableExpression.FindAnnotation(annotationName)
            ?.Value is string value
        && !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new InvalidOperationException(
                $"Temporal table source '{tableExpression.Name}' is missing '{annotationName}'.");

    private static InvalidOperationException UnknownTemporalOperation(
        MySqlTemporalQueryOperation operation
    ) => new($"Unknown temporal query operation '{operation}'.");
}
