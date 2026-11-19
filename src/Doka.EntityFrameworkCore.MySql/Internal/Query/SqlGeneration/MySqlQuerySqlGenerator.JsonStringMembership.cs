namespace Doka.EntityFrameworkCore.MySql;

internal sealed partial class MySqlQuerySqlGenerator
{
    /// <summary>Compares decoded JSON strings while retaining row/member paths and exact equality.</summary>
    private void GenerateDecodedJsonStringMembership(
        MySqlJsonTableExpression source,
        SqlExpression item,
        bool negated
    )
    {
        var value = source.ColumnInfos!
            .Single(column => column is { Name: "value", ForOrdinality: false });

        var rawSource = new MySqlJsonTableExpression(
            source.Alias,
            source.JsonExpression,
            source.Path,
            [
                value with
                {
                    AsJson = true,
                    ResultTypeMapping = null,
                },
            ]);

        // WHY: MariaDB before 12.3.3 compares JSON_CONTAINS strings by raw escape bytes
        // (MDEV-39995). Raw JSON extraction plus JSON_UNQUOTE decodes once per member;
        // binary equality preserves case, accents and trailing spaces without JSON normalization.
        // The same rowset preserves nested projections which whole-document membership cannot express.
        // Source: https://github.com/MariaDB/server/blob/mariadb-12.3.3/sql/item_jsonfunc.cc
        Sql.Append("(CASE WHEN ");
        Visit(source.JsonExpression);
        Sql.Append(" IS NULL THEN NULL ELSE (");
        Sql.AppendLine();

        using (Sql.Indent())
        {
            Sql.AppendLine("SELECT 1");
            Sql.Append("FROM ");
            Visit(rawSource);
            Sql.AppendLine();
            Sql.Append("WHERE ((");
            AppendRawJsonMembershipColumn(source.Alias, value.Name);
            Sql.Append(" IS NULL OR JSON_TYPE(");
            AppendRawJsonMembershipColumn(source.Alias, value.Name);
            Sql.Append(") = 'NULL') AND ");
            Visit(item);
            Sql.Append(" IS NULL) OR (JSON_TYPE(");
            AppendRawJsonMembershipColumn(source.Alias, value.Name);
            Sql.Append(") = 'STRING' AND CAST(JSON_UNQUOTE(");
            AppendRawJsonMembershipColumn(source.Alias, value.Name);
            Sql.Append(") AS BINARY) = CAST(");
            GenerateJsonStringCandidate(item);
            Sql.AppendLine(" AS BINARY))");
            Sql.AppendLine("LIMIT 1");
        }

        Sql.Append(negated
            ? ") IS NULL END)"
            : ") IS NOT NULL END)");
    }

    /// <summary>Uses the already unique row-source alias and its quoted column name.</summary>
    private void AppendRawJsonMembershipColumn(
        string alias,
        string columnName
    ) => Sql
        .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(alias))
        .Append(".")
        .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(columnName));
}
