namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Loads named CHECK constraints into provider-owned scaffolding annotations. EF Core's
/// <see cref="DatabaseTable"/> has no CHECK collection, so the annotation bridges the
/// database model to <see cref="MySqlScaffoldingModelFactory"/> without exposing a new
/// public provider API.
/// </summary>
internal static class CheckConstraintLoader
{
    public static void Load(
        ScaffoldingPipelineContext context
    )
    {
        ArgumentNullException.ThrowIfNull(context);

        using var command = context.Connection.CreateCommand();
        var catalogIncludesTableName = context.Profile.Engine.Has(
            EngineCapability.CheckConstraintCatalogIncludesTableName);
        var sql = catalogIncludesTableName ? MariaDbQuery() : MySqlQuery();

        ScaffoldingHelpers.AppendTableNameFilter(
            sql,
            command,
            context.TableFilter,
            catalogIncludesTableName ? "checks.TABLE_NAME" : "constraints.TABLE_NAME");
        sql.Append(" ORDER BY TABLE_NAME, CONSTRAINT_NAME;");
        command.CommandText = sql.ToString();

        using var reader = command.ExecuteReader();
        var constraintsByTable = new Dictionary<string, List<MySqlScaffoldedCheckConstraint>>(StringComparer.Ordinal);

        while (reader.Read())
        {
            var tableName = reader.GetString(0);
            var constraintName = reader.GetString(1);
            var checkClause = reader.GetString(2);

            if (!context.TableFilter.Matches(tableName)
                || !context.TableLookup.TryGetValue(tableName, out _))
            {
                continue;
            }

            if (SpatialColumnLoader.TryApplyMariaDbCheck(context, tableName, constraintName, checkClause))
            {
                continue;
            }

            var constraints = constraintsByTable.TryGetValue(tableName, out var existing)
                ? existing
                : constraintsByTable[tableName] = [];

            constraints.Add(new MySqlScaffoldedCheckConstraint(constraintName, checkClause));
        }

        foreach (var (tableName, constraints) in constraintsByTable)
        {
            context
                .TableLookup[tableName]
                .SetAnnotation(MySqlAnnotationNames.ScaffoldingCheckConstraints, constraints.ToArray());
        }
    }

    private static StringBuilder MySqlQuery() => new(
        """
        SELECT
            constraints.TABLE_NAME,
            constraints.CONSTRAINT_NAME,
            checks.CHECK_CLAUSE
        FROM information_schema.TABLE_CONSTRAINTS AS constraints
        INNER JOIN information_schema.CHECK_CONSTRAINTS AS checks
            ON checks.CONSTRAINT_SCHEMA = constraints.CONSTRAINT_SCHEMA
            AND checks.CONSTRAINT_NAME = constraints.CONSTRAINT_NAME
        WHERE constraints.TABLE_SCHEMA = DATABASE()
          AND constraints.CONSTRAINT_TYPE = 'CHECK'
        """);

    private static StringBuilder MariaDbQuery() => new(
        """
        SELECT
            checks.TABLE_NAME,
            checks.CONSTRAINT_NAME,
            checks.CHECK_CLAUSE
        FROM information_schema.CHECK_CONSTRAINTS AS checks
        WHERE checks.CONSTRAINT_SCHEMA = DATABASE()
        """);
}

internal sealed record MySqlScaffoldedCheckConstraint(
    string Name,
    string Sql
);
