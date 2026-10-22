using Doka.EntityFrameworkCore.MySql;

namespace ConsumerMigrationTests;

/// <summary>
/// Verifies overload resolution from a consumer namespace with both
/// extension namespaces visible rather than from Doka's enclosing namespace.
/// </summary>
public sealed class MySqlLikeOverloadBindingTests
{
    /// <summary>Verifies the owner and generic argument of one consumer-visible overload.</summary>
    [Theory]
    [InlineData("String", typeof(DbFunctionsExtensions), null)]
    [InlineData("NullableString", typeof(DbFunctionsExtensions), null)]
    [InlineData("Int32", typeof(MySqlDbFunctionsExtensions), typeof(int))]
    [InlineData("NullableGuid", typeof(MySqlDbFunctionsExtensions), typeof(Guid?))]
    public void Like_overload_binds_to_its_expected_owner(
        string operand,
        Type declaringType,
        Type? genericArgument
    )
    {
        // Arrange
        System.Linq.Expressions.LambdaExpression expression = operand switch
        {
            "String" => (System.Linq.Expressions.Expression<Func<string, string, bool>>)
                ((value, pattern) => EF.Functions.Like(value, pattern)),
            "NullableString" => (System.Linq.Expressions.Expression<Func<string?, string, bool>>)
                ((value, pattern) => EF.Functions.Like(value!, pattern, "!")),
            "Int32" => (System.Linq.Expressions.Expression<Func<int, string, bool>>)
                ((value, pattern) => EF.Functions.Like(value, pattern)),
            "NullableGuid" => (System.Linq.Expressions.Expression<Func<Guid?, string, bool>>)
                ((value, pattern) => EF.Functions.Like(value, pattern, "!")),
            _ => throw new ArgumentOutOfRangeException(nameof(operand)),
        };

        var expectedArguments = genericArgument is null ? Type.EmptyTypes : [genericArgument];

        // Act
        var call = (System.Linq.Expressions.MethodCallExpression)expression.Body;
        var method = call.Method;

        // Assert
        Assert.Equal(declaringType, method.DeclaringType);
        Assert.Equal(genericArgument is not null, method.IsGenericMethod);
        Assert.Equal(expectedArguments, method.GetGenericArguments());
    }
}
