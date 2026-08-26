using Doka.EntityFrameworkCore.MySql;

namespace ConsumerMigrationTests;

/// <summary>
/// Verifies overload resolution from a consumer namespace with both
/// extension namespaces visible rather than from Doka's enclosing namespace.
/// </summary>
public sealed class MySqlLikeOverloadBindingTests
{
    [Fact]
    public void Strings_bind_to_EF_Core_and_scalars_bind_to_Doka()
    {
        System.Linq.Expressions.Expression<Func<string, string, bool>> text =
            (value, pattern) => EF.Functions.Like(value, pattern);

        System.Linq.Expressions.Expression<Func<string?, string, bool>> nullableText =
            (value, pattern) => EF.Functions.Like(value!, pattern, "!");

        System.Linq.Expressions.Expression<Func<int, string, bool>> number =
            (value, pattern) => EF.Functions.Like(value, pattern);

        System.Linq.Expressions.Expression<Func<Guid?, string, bool>> nullableGuid =
            (value, pattern) => EF.Functions.Like(value, pattern, "!");

        var textCall = Assert.IsType<System.Linq.Expressions.MethodCallExpression>(text.Body, exactMatch: false);
        var nullableTextCall = Assert.IsType<System.Linq.Expressions.MethodCallExpression>(nullableText.Body, exactMatch: false);
        var numberCall = Assert.IsType<System.Linq.Expressions.MethodCallExpression>(number.Body, exactMatch: false);
        var nullableGuidCall = Assert.IsType<System.Linq.Expressions.MethodCallExpression>(nullableGuid.Body, exactMatch: false);

        Assert.Equal(typeof(DbFunctionsExtensions), textCall.Method.DeclaringType);
        Assert.Equal(typeof(DbFunctionsExtensions), nullableTextCall.Method.DeclaringType);
        Assert.False(textCall.Method.IsGenericMethod);
        Assert.False(nullableTextCall.Method.IsGenericMethod);
        Assert.Equal(typeof(MySqlDbFunctionsExtensions), numberCall.Method.DeclaringType);
        Assert.Equal(typeof(MySqlDbFunctionsExtensions), nullableGuidCall.Method.DeclaringType);
        Assert.Equal([typeof(int)], numberCall.Method.GetGenericArguments());
        Assert.Equal([typeof(Guid?)], nullableGuidCall.Method.GetGenericArguments());
    }
}
