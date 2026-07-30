namespace Doka.EntityFrameworkCore.MySql;

internal sealed partial class MySqlMigrationsSqlGenerator
{
    /// <summary>
    /// Generates CREATE SEQUENCE DDL -- native on MariaDB 10.3+, table-based emulation on MySQL.
    /// </summary>
    protected override void Generate(
        CreateSequenceOperation operation,
        IModel? model,
        MigrationCommandListBuilder builder
    )
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(builder);

        if (UsesNativeSequences())
        {
            builder
                .Append("CREATE SEQUENCE ")
                .Append(DelimitMigrationIdentifier(operation.Name));

            if (Profile.Engine.Version.CompareTo(new Version(11, 5, 0)) >= 0)
            {
                builder
                    .Append(" AS ")
                    .Append(GetSequenceTypeInfo(operation.ClrType).StoreType);
            }

            builder
                .Append(" START WITH ")
                .Append(operation.StartValue.ToString(CultureInfo.InvariantCulture))
                .Append(" INCREMENT BY ")
                .Append(operation.IncrementBy.ToString(CultureInfo.InvariantCulture));

            if (operation.MinValue.HasValue)
            {
                builder
                    .Append(" MINVALUE ")
                    .Append(operation.MinValue.Value.ToString(CultureInfo.InvariantCulture));
            }

            if (operation.MaxValue.HasValue)
            {
                builder
                    .Append(" MAXVALUE ")
                    .Append(operation.MaxValue.Value.ToString(CultureInfo.InvariantCulture));
            }

            builder.Append(operation.IsCyclic ? " CYCLE" : " NOCYCLE");
            builder.AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);
            builder.EndCommand();
            return;
        }

        var typeInfo = GetSequenceTypeInfo(operation.ClrType);
        ValidateSequenceIncrement(typeInfo, operation.IncrementBy);

        var minimumValue = operation.MinValue
            ?? GetDefaultSequenceMinimum(typeInfo, operation.IncrementBy);
        var maximumValue = operation.MaxValue
            ?? GetDefaultSequenceMaximum(typeInfo, operation.IncrementBy);
        var tableName = MySqlSequenceNaming.EmulationTableName(operation.Name);
        var delimitedTableName = Dependencies.SqlGenerationHelper.DelimitIdentifier(tableName);

        builder
            .Append("CREATE TABLE ")
            .Append(delimitedTableName)
            .AppendLine(" (")
            .Append("    ")
            .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier("id"))
            .AppendLine(" TINYINT UNSIGNED NOT NULL,")
            .Append("    ")
            .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier("value"))
            .Append(" ")
            .Append(typeInfo.StoreType)
            .AppendLine(" NOT NULL,")
            .Append("    ")
            .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier("start_value"))
            .Append(" ")
            .Append(typeInfo.StoreType)
            .AppendLine(" NOT NULL,")
            .Append("    ")
            .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier("increment_by"))
            .AppendLine(" INT NOT NULL,")
            .Append("    ")
            .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier("min_value"))
            .Append(" ")
            .Append(typeInfo.StoreType)
            .AppendLine(" NOT NULL,")
            .Append("    ")
            .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier("max_value"))
            .Append(" ")
            .Append(typeInfo.StoreType)
            .AppendLine(" NOT NULL,")
            .Append("    ")
            .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier("is_cyclic"))
            .AppendLine(" BOOLEAN NOT NULL,")
            .Append("    ")
            .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier("is_called"))
            .AppendLine(" BOOLEAN NOT NULL,")
            .Append("    PRIMARY KEY (")
            .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier("id"))
            .AppendLine("),")
            .Append("    CHECK (")
            .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier("id"))
            .AppendLine(" = 1)")
            .Append(") ENGINE=InnoDB")
            .AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);

        builder.EndCommand();

        builder
            .Append("INSERT INTO ")
            .Append(delimitedTableName)
            .Append(" (")
            .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier("id"))
            .Append(", ")
            .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier("value"))
            .Append(", ")
            .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier("start_value"))
            .Append(", ")
            .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier("increment_by"))
            .Append(", ")
            .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier("min_value"))
            .Append(", ")
            .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier("max_value"))
            .Append(", ")
            .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier("is_cyclic"))
            .Append(", ")
            .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier("is_called"))
            .Append(") VALUES (1, ")
            .Append(operation.StartValue.ToString(CultureInfo.InvariantCulture))
            .Append(", ")
            .Append(operation.StartValue.ToString(CultureInfo.InvariantCulture))
            .Append(", ")
            .Append(operation.IncrementBy.ToString(CultureInfo.InvariantCulture))
            .Append(", ")
            .Append(minimumValue.ToString(CultureInfo.InvariantCulture))
            .Append(", ")
            .Append(maximumValue.ToString(CultureInfo.InvariantCulture))
            .Append(", ")
            .Append(operation.IsCyclic ? "TRUE" : "FALSE")
            .Append(", FALSE)")
            .AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);

        builder.EndCommand();
    }

    /// <summary>
    /// Generates DROP SEQUENCE DDL -- native on MariaDB 10.3+, drops emulation table on MySQL.
    /// </summary>
    protected override void Generate(
        DropSequenceOperation operation,
        IModel? model,
        MigrationCommandListBuilder builder
    )
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(builder);

        if (UsesNativeSequences())
        {
            builder
                .Append("DROP SEQUENCE IF EXISTS ")
                .Append(DelimitMigrationIdentifier(operation.Name))
                .AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);
            builder.EndCommand();
            return;
        }

        var tableName = MySqlSequenceNaming.EmulationTableName(operation.Name);

        builder
            .Append("DROP TABLE IF EXISTS ")
            .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(tableName))
            .AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);

        builder.EndCommand();
    }

    /// <summary>
    /// Generates ALTER SEQUENCE DDL -- native on MariaDB 10.3+, updates emulation table on MySQL.
    /// </summary>
    protected override void Generate(
        AlterSequenceOperation operation,
        IModel? model,
        MigrationCommandListBuilder builder
    )
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(builder);

        if (UsesNativeSequences())
        {
            builder
                .Append("ALTER SEQUENCE ")
                .Append(DelimitMigrationIdentifier(operation.Name))
                .Append(" INCREMENT BY ")
                .Append(operation.IncrementBy.ToString(CultureInfo.InvariantCulture));

            builder.Append(
                operation.MinValue.HasValue
                    ? " MINVALUE " + operation.MinValue.Value.ToString(CultureInfo.InvariantCulture)
                    : " NO MINVALUE");
            builder.Append(
                operation.MaxValue.HasValue
                    ? " MAXVALUE " + operation.MaxValue.Value.ToString(CultureInfo.InvariantCulture)
                    : " NO MAXVALUE");
            builder.Append(operation.IsCyclic ? " CYCLE" : " NOCYCLE");

            builder.AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);
            builder.EndCommand();
            return;
        }

        var clrType = model
                ?.FindSequence(operation.Name, operation.Schema)
                ?.Type
            ?? (operation.OldSequence as CreateSequenceOperation)?.ClrType
            ?? typeof(long);
        var typeInfo = GetSequenceTypeInfo(clrType);
        ValidateSequenceIncrement(typeInfo, operation.IncrementBy);

        var minimumValue = operation.MinValue
            ?? GetDefaultSequenceMinimum(typeInfo, operation.IncrementBy);
        var maximumValue = operation.MaxValue
            ?? GetDefaultSequenceMaximum(typeInfo, operation.IncrementBy);
        var tableName = MySqlSequenceNaming.EmulationTableName(operation.Name);

        builder
            .Append("UPDATE ")
            .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(tableName))
            .Append(" SET ")
            .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier("increment_by"))
            .Append(" = ")
            .Append(operation.IncrementBy.ToString(CultureInfo.InvariantCulture))
            .Append(", ")
            .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier("min_value"))
            .Append(" = ")
            .Append(minimumValue.ToString(CultureInfo.InvariantCulture))
            .Append(", ")
            .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier("max_value"))
            .Append(" = ")
            .Append(maximumValue.ToString(CultureInfo.InvariantCulture))
            .Append(", ")
            .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier("is_cyclic"))
            .Append(" = ")
            .Append(operation.IsCyclic ? "TRUE" : "FALSE")
            .Append(" WHERE ")
            .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier("id"))
            .Append(" = 1")
            .AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);
        builder.EndCommand();
    }

    /// <summary>
    /// Renames a native MariaDB sequence or the MySQL emulation table.
    /// </summary>
    protected override void Generate(
        RenameSequenceOperation operation,
        IModel? model,
        MigrationCommandListBuilder builder
    )
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(builder);

        var newName = operation.NewName ?? operation.Name;

        if (string.Equals(operation.Name, newName, StringComparison.Ordinal))
        {
            return;
        }

        if (UsesNativeSequences())
        {
            builder
                .Append("RENAME TABLE ")
                .Append(DelimitMigrationIdentifier(operation.Name))
                .Append(" TO ")
                .Append(DelimitMigrationIdentifier(newName))
                .AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);
            builder.EndCommand();
            return;
        }

        var oldTableName = MySqlSequenceNaming.EmulationTableName(operation.Name);
        var newTableName = MySqlSequenceNaming.EmulationTableName(newName);

        builder
            .Append("RENAME TABLE ")
            .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(oldTableName))
            .Append(" TO ")
            .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(newTableName))
            .AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);

        builder.EndCommand();
    }

    /// <summary>
    /// Restarts a native MariaDB sequence or the MySQL emulation row.
    /// </summary>
    protected override void Generate(
        RestartSequenceOperation operation,
        IModel? model,
        MigrationCommandListBuilder builder
    )
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(builder);

        if (UsesNativeSequences())
        {
            builder
                .Append("ALTER SEQUENCE ")
                .Append(DelimitMigrationIdentifier(operation.Name))
                .Append(" ");

            if (operation.StartValue.HasValue)
            {
                builder
                    .Append("START WITH ")
                    .Append(operation.StartValue.Value.ToString(CultureInfo.InvariantCulture))
                    .Append(" RESTART WITH ")
                    .Append(operation.StartValue.Value.ToString(CultureInfo.InvariantCulture));
            }
            else
            {
                builder.Append("RESTART");
            }

            builder.AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);
            EndStatement(builder);
            return;
        }

        var tableName = MySqlSequenceNaming.EmulationTableName(operation.Name);

        builder
            .Append("UPDATE ")
            .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(tableName))
            .Append(" SET ")
            .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier("value"))
            .Append(" = ")
            .Append(
                operation.StartValue?.ToString(CultureInfo.InvariantCulture)
                ?? Dependencies.SqlGenerationHelper.DelimitIdentifier("start_value"));

        if (operation.StartValue.HasValue)
        {
            builder
                .Append(", ")
                .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier("start_value"))
                .Append(" = ")
                .Append(operation.StartValue.Value.ToString(CultureInfo.InvariantCulture));
        }

        builder
            .Append(", ")
            .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier("is_called"))
            .Append(" = FALSE WHERE ")
            .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier("id"))
            .Append(" = 1")
            .AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);
        EndStatement(builder);
    }

    private static SequenceTypeInfo GetSequenceTypeInfo(
        Type? clrType
    )
    {
        // EF Core's non-generic CreateSequence API defaults to Int64. Operations
        // constructed directly can omit ClrType and must retain the same contract.
        clrType ??= typeof(long);

        clrType = Nullable.GetUnderlyingType(clrType) ?? clrType;

        if (clrType == typeof(sbyte))
        {
            return new SequenceTypeInfo("TINYINT", sbyte.MinValue, sbyte.MaxValue, false);
        }

        if (clrType == typeof(byte))
        {
            return new SequenceTypeInfo("TINYINT UNSIGNED", byte.MinValue, byte.MaxValue, true);
        }

        if (clrType == typeof(short))
        {
            return new SequenceTypeInfo("SMALLINT", short.MinValue, short.MaxValue, false);
        }

        if (clrType == typeof(ushort))
        {
            return new SequenceTypeInfo("SMALLINT UNSIGNED", ushort.MinValue, ushort.MaxValue, true);
        }

        if (clrType == typeof(int))
        {
            return new SequenceTypeInfo("INT", int.MinValue, int.MaxValue, false);
        }

        if (clrType == typeof(uint))
        {
            return new SequenceTypeInfo("INT UNSIGNED", uint.MinValue, uint.MaxValue, true);
        }

        if (clrType == typeof(long))
        {
            return new SequenceTypeInfo("BIGINT", long.MinValue, long.MaxValue, false);
        }

        if (clrType == typeof(ulong))
        {
            return new SequenceTypeInfo("BIGINT UNSIGNED", 0, long.MaxValue, true);
        }

        throw new InvalidOperationException(
            $"The CLR type '{clrType.ShortDisplayName()}' cannot back a MySQL-family sequence.");
    }

    private static long GetDefaultSequenceMinimum(
        SequenceTypeInfo typeInfo,
        int increment
    ) => increment > 0 ? 1 : checked(typeInfo.MinimumValue + 1);

    private static long GetDefaultSequenceMaximum(
        SequenceTypeInfo typeInfo,
        int increment
    ) => increment > 0 ? checked(typeInfo.MaximumValue - 1) : -1;

    private static void ValidateSequenceIncrement(
        SequenceTypeInfo typeInfo,
        int increment
    )
    {
        if (increment == 0)
        {
            throw new InvalidOperationException("A sequence increment cannot be zero.");
        }

        if (increment < 0
            && typeInfo.IsUnsigned)
        {
            throw new InvalidOperationException(
                $"The unsigned sequence store type '{typeInfo.StoreType}' cannot use a negative increment.");
        }
    }

    private bool UsesNativeSequences() =>
        Profile.GetSupport(ProviderCapability.Sequences) == ProviderSupportStatus.Native;

    private readonly record struct SequenceTypeInfo(
        string StoreType,
        long MinimumValue,
        long MaximumValue,
        bool IsUnsigned
    );
}
