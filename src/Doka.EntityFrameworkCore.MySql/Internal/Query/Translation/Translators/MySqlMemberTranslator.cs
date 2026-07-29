namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Translates scalar members that are independent of the provider's temporal
/// representation.
/// </summary>
internal sealed class MySqlMemberTranslator : IMemberTranslator
{
    private static readonly RelationalTypeMapping s_intTypeMapping = new IntTypeMapping("int", DbType.Int32);
    private static readonly bool[] s_singleArgumentNullPropagation = [true];

    private readonly ISqlExpressionFactory _sqlExpressionFactory;

    public MySqlMemberTranslator(
        ISqlExpressionFactory sqlExpressionFactory
    )
    {
        _sqlExpressionFactory = sqlExpressionFactory;
    }

    /// <inheritdoc />
    public SqlExpression? Translate(
        SqlExpression? instance,
        MemberInfo member,
        Type returnType,
        IDiagnosticsLogger<DbLoggerCategory.Query> logger
    )
    {
        if (instance is null)
        {
            return null;
        }

        return member.DeclaringType == typeof(string) && member.Name == nameof(string.Length)
            ? _sqlExpressionFactory.Function(
                "CHAR_LENGTH",
                [instance],
                nullable: true,
                argumentsPropagateNullability: s_singleArgumentNullPropagation,
                returnType,
                s_intTypeMapping)
            : null;
    }
}
