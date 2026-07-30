namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Loads provider-emulated MySQL sequences and native MariaDB sequences into the
/// reverse-engineered database model. Emulation tables are removed from the table
/// surface after their sequence metadata has been materialized.
/// </summary>
internal static class SequenceLoader
{
    private static readonly Version s_informationSchemaSequencesVersion = new(11, 5, 0);

    private static readonly string[] s_emulationMetadataColumns =
    [
        "id",
        "value",
        "start_value",
        "increment_by",
        "min_value",
        "max_value",
        "is_cyclic",
        "is_called",
    ];

    public static void Load(
        ScaffoldingPipelineContext context
    )
    {
        ArgumentNullException.ThrowIfNull(context);

        LoadEmulatedSequences(context);

        if (context.Profile.GetSupport(ProviderCapability.Sequences) == ProviderSupportStatus.Native)
        {
            LoadNativeSequences(context);
        }
    }

    private static void LoadEmulatedSequences(
        ScaffoldingPipelineContext context
    )
    {
        var tables = context
            .TableLookup.Values.Where(IsEmulationTable)
            .OrderBy(table => table.Name, StringComparer.Ordinal)
            .ToArray();

        if (tables.Length == 0)
        {
            return;
        }

        using var command = context.Connection.CreateCommand();
        var sql = new StringBuilder();

        for (var index = 0; index < tables.Length; index++)
        {
            if (index > 0)
            {
                sql.AppendLine("UNION ALL");
            }

            var parameterName = "@sequence" + index.ToString(CultureInfo.InvariantCulture);
            var parameter = command.CreateParameter();
            parameter.ParameterName = parameterName;
            parameter.Value = tables[index]
                .Name[MySqlSequenceNaming.EmulationTablePrefix.Length..];
            command.Parameters.Add(parameter);

            sql
                .Append("SELECT ")
                .Append(parameterName)
                .Append(", ")
                .Append(DependenciesIdentifier("start_value"))
                .Append(", ")
                .Append(DependenciesIdentifier("increment_by"))
                .Append(", ")
                .Append(DependenciesIdentifier("min_value"))
                .Append(", ")
                .Append(DependenciesIdentifier("max_value"))
                .Append(", ")
                .Append(DependenciesIdentifier("is_cyclic"))
                .Append(" FROM ")
                .Append(MySqlIdentifierEscaping.DelimitIdentifier(tables[index].Name))
                .Append(" WHERE ")
                .Append(DependenciesIdentifier("id"))
                .AppendLine(" = 1");
        }

        command.CommandText = sql.ToString();

        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                var sequenceName = reader.GetString(0);
                var tableName = MySqlSequenceNaming.EmulationTableName(sequenceName);
                var table = context.TableLookup[tableName];
                var valueColumn = table.Columns.Single(column => column.Name == "value");

                AddSequence(
                    context,
                    sequenceName,
                    valueColumn.StoreType,
                    reader.GetValue(1),
                    reader.GetValue(2),
                    reader.GetValue(3),
                    reader.GetValue(4),
                    reader.GetValue(5));
            }
        }

        foreach (var table in tables)
        {
            context.DatabaseModel.Tables.Remove(table);
            context.TableLookup.Remove(table.Name);

            foreach (var column in table.Columns)
            {
                context.Columns.Remove((table.Name, column.Name));
            }
        }
    }

    private static void LoadNativeSequences(
        ScaffoldingPipelineContext context
    )
    {
        if (context.Profile.Engine.Version.CompareTo(s_informationSchemaSequencesVersion) >= 0)
        {
            LoadNativeSequencesFromInformationSchema(context);
            return;
        }

        var sequenceNames = LoadNativeSequenceNames(context);

        if (sequenceNames.Count == 0)
        {
            return;
        }

        using var command = context.Connection.CreateCommand();
        var sql = new StringBuilder();

        for (var index = 0; index < sequenceNames.Count; index++)
        {
            if (index > 0)
            {
                sql.AppendLine("UNION ALL");
            }

            var parameterName = "@sequence" + index.ToString(CultureInfo.InvariantCulture);
            var parameter = command.CreateParameter();
            parameter.ParameterName = parameterName;
            parameter.Value = sequenceNames[index];
            command.Parameters.Add(parameter);

            sql
                .Append("SELECT ")
                .Append(parameterName)
                .Append(", 'bigint', ")
                .Append(DependenciesIdentifier("start_value"))
                .Append(", ")
                .Append(DependenciesIdentifier("increment"))
                .Append(", ")
                .Append(DependenciesIdentifier("minimum_value"))
                .Append(", ")
                .Append(DependenciesIdentifier("maximum_value"))
                .Append(", ")
                .Append(DependenciesIdentifier("cycle_option"))
                .Append(" FROM ")
                .Append(MySqlIdentifierEscaping.DelimitIdentifier(sequenceNames[index]))
                .AppendLine();
        }

        command.CommandText = sql.ToString();
        ReadNativeSequences(context, command);
    }

    private static void LoadNativeSequencesFromInformationSchema(
        ScaffoldingPipelineContext context
    )
    {
        using var command = context.Connection.CreateCommand();
        var sql = new StringBuilder(
            """
            SELECT
                SEQUENCE_NAME,
                DATA_TYPE,
                START_VALUE,
                INCREMENT,
                MINIMUM_VALUE,
                MAXIMUM_VALUE,
                CYCLE_OPTION
            FROM information_schema.SEQUENCES
            WHERE SEQUENCE_SCHEMA = DATABASE()
            """);

        ScaffoldingHelpers.AppendTableNameFilter(sql, command, context.TableFilter, "SEQUENCE_NAME");
        sql.Append(" ORDER BY SEQUENCE_NAME;");
        command.CommandText = sql.ToString();

        ReadNativeSequences(context, command);
    }

    private static List<string> LoadNativeSequenceNames(
        ScaffoldingPipelineContext context
    )
    {
        using var command = context.Connection.CreateCommand();
        var sql = new StringBuilder(
            """
            SELECT TABLE_NAME
            FROM information_schema.TABLES
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_TYPE = 'SEQUENCE'
            """);

        ScaffoldingHelpers.AppendTableNameFilter(sql, command, context.TableFilter);
        sql.Append(" ORDER BY TABLE_NAME;");
        command.CommandText = sql.ToString();

        using var reader = command.ExecuteReader();
        var names = new List<string>();

        while (reader.Read())
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }

    private static void ReadNativeSequences(
        ScaffoldingPipelineContext context,
        DbCommand command
    )
    {
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            AddSequence(
                context,
                reader.GetString(0),
                reader.GetString(1),
                reader.GetValue(2),
                reader.GetValue(3),
                reader.GetValue(4),
                reader.GetValue(5),
                reader.GetValue(6));
        }
    }

    private static void AddSequence(
        ScaffoldingPipelineContext context,
        string name,
        string? storeType,
        object startValue,
        object incrementBy,
        object minValue,
        object maxValue,
        object isCyclic
    )
    {
        context.DatabaseModel.Sequences.Add(
            new DatabaseSequence
            {
                Database = context.DatabaseModel,
                Name = name,
                Schema = context.QualifyNamesWithSchema ? context.DatabaseName : null,
                StoreType = storeType,
                StartValue = Convert.ToInt64(startValue, CultureInfo.InvariantCulture),
                IncrementBy = Convert.ToInt32(incrementBy, CultureInfo.InvariantCulture),
                MinValue = Convert.ToInt64(minValue, CultureInfo.InvariantCulture),
                MaxValue = Convert.ToInt64(maxValue, CultureInfo.InvariantCulture),
                IsCyclic = ConvertSequenceBoolean(isCyclic),
            });
    }

    private static bool ConvertSequenceBoolean(
        object value
    ) => value switch
    {
        bool booleanValue => booleanValue,
        "0" => false,
        "1" => true,
        _ => Convert.ToInt64(value, CultureInfo.InvariantCulture) != 0,
    };

    private static bool IsEmulationTable(
        DatabaseTable table
    )
    {
        if (!table.Name.StartsWith(MySqlSequenceNaming.EmulationTablePrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var columnNames = table
            .Columns.Select(column => column.Name)
            .ToHashSet(StringComparer.Ordinal);

        return s_emulationMetadataColumns.All(columnNames.Contains);
    }

    private static string DependenciesIdentifier(
        string identifier
    ) => MySqlIdentifierEscaping.DelimitIdentifier(identifier);
}
