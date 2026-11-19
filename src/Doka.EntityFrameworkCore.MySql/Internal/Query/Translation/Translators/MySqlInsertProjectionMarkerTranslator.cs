namespace Doka.EntityFrameworkCore.MySql;

/// <summary>Forces each insert assignment to become an ordinary typed SQL projection, including constants.</summary>
internal sealed class MySqlInsertProjectionMarkerTranslator : IMethodCallTranslator
{
    /// <inheritdoc />
    public SqlExpression? Translate(
        SqlExpression? instance,
        MethodInfo method,
        IReadOnlyList<SqlExpression> arguments,
        IDiagnosticsLogger<DbLoggerCategory.Query> logger
    ) => method.DeclaringType == typeof(MySqlInsertProjectionMarker)
        && method.Name == nameof(MySqlInsertProjectionMarker.Value)
        && arguments.Count == 1
            ? arguments[0]
            : null;
}

/// <summary>Identifies an unevaluatable typed SQL value without adding a database function or client materializer.</summary>
internal static class MySqlInsertProjectionMarker
{
    private static readonly NewArrayExpression s_valueArray =
        (NewArrayExpression)((Expression<Func<object[]>>)(() => new object[] { string.Empty })).Body;

    /// <summary>Updates only the elements of a statically typed object[] projection schema.</summary>
    internal static NewArrayExpression CreateValueArray(
        IEnumerable<Expression> values
    ) => s_valueArray.Update(values);

    /// <summary>Fails if an insert-only SQL marker ever escapes server translation.</summary>
    internal static T Value<T>(
        T value
    ) => throw new InvalidOperationException("INSERT SELECT value markers can only be translated to SQL.");

    /// <summary>Closes the one known marker without reflection-based generic construction for every assignment.</summary>
    internal static class Methods<T>
    {
        internal static readonly MethodInfo Value = ((Func<T, T>)MySqlInsertProjectionMarker.Value).Method;
    }
}
