namespace Doka.EntityFrameworkCore.MySql;

internal sealed class MySqlUpdateSqlGenerator : UpdateAndSelectSqlGenerator
{
    public MySqlUpdateSqlGenerator(
        UpdateSqlGeneratorDependencies dependencies
    ) : base(dependencies) { }

    protected override void AppendIdentityWhereCondition(
        StringBuilder commandStringBuilder,
        IColumnModification columnModification
    )
    {
        ArgumentNullException.ThrowIfNull(commandStringBuilder);
        ArgumentNullException.ThrowIfNull(columnModification);

        SqlGenerationHelper.DelimitIdentifier(commandStringBuilder, columnModification.ColumnName);
        commandStringBuilder.Append(" = LAST_INSERT_ID()");
    }

    protected override void AppendRowsAffectedWhereCondition(
        StringBuilder commandStringBuilder,
        int expectedRowsAffected
    )
    {
        ArgumentNullException.ThrowIfNull(commandStringBuilder);

        commandStringBuilder.Append("ROW_COUNT() = ");
        commandStringBuilder.Append(expectedRowsAffected.ToString(CultureInfo.InvariantCulture));
    }

    protected override ResultSetMapping AppendSelectAffectedCountCommand(
        StringBuilder commandStringBuilder,
        string name,
        string? schema,
        int commandPosition
    )
    {
        ArgumentNullException.ThrowIfNull(commandStringBuilder);

        commandStringBuilder
            .Append("SELECT ROW_COUNT()")
            .AppendLine(SqlGenerationHelper.StatementTerminator);

        return ResultSetMapping.LastInResultSet | ResultSetMapping.ResultSetWithRowsAffectedOnly;
    }
}
