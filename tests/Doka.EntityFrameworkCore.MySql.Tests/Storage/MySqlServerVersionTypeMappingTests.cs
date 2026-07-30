namespace Doka.EntityFrameworkCore.MySql.Tests;

/// <summary>
/// Verifies code literals for supported and explicitly unsupported server
/// descriptors.
/// </summary>
public sealed class MySqlServerVersionTypeMappingTests
{
    /// <summary>
    /// Ensures a supported descriptor keeps the compact one-argument factory call.
    /// </summary>
    [Fact]
    public void Supported_version_literal_uses_default_factory_overload()
    {
        var mapping = new MySqlServerVersionTypeMapping();
        var expression = Assert.IsAssignableFrom<MethodCallExpression>(
            mapping.GenerateCodeLiteral(MySqlServerVersion.MySql(new Version(8, 4, 7))));

        Assert.Equal(nameof(MySqlServerVersion.MySql), expression.Method.Name);
        Assert.Single(expression.Arguments);
    }

    /// <summary>
    /// Ensures an unsupported descriptor preserves its explicit compatibility
    /// mode in generated code.
    /// </summary>
    [Fact]
    public void Unsupported_version_literal_preserves_explicit_compatibility_mode()
    {
        var mapping = new MySqlServerVersionTypeMapping();
        var expression = Assert.IsAssignableFrom<MethodCallExpression>(
            mapping.GenerateCodeLiteral(
                MySqlServerVersion.MariaDb(
                    new Version(11, 6, 2),
                    MySqlServerVersionCompatibilityMode.AllowUnsupported)));

        Assert.Equal(nameof(MySqlServerVersion.MariaDb), expression.Method.Name);
        Assert.Equal(2, expression.Arguments.Count);
        Assert.Equal(
            MySqlServerVersionCompatibilityMode.AllowUnsupported,
            Assert.IsType<ConstantExpression>(expression.Arguments[1])
                .Value);
    }
}
