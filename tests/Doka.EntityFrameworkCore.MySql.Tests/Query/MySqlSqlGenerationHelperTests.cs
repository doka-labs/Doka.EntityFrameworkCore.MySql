namespace Doka.EntityFrameworkCore.MySql.Tests;

/// <summary>
/// Tests backtick identifier escaping and delimiter generation.
/// </summary>
public sealed class MySqlSqlGenerationHelperTests
{
    /// <summary>Identifiers are wrapped in backticks.</summary>
    [Fact]
    public void DelimitIdentifier_wraps_in_backticks()
    {
        // Arrange
        var helper = CreateHelper();

        // Act
        var result = helper.DelimitIdentifier("MyTable");

        // Assert
        Assert.Equal("`MyTable`", result);
    }

    /// <summary>Embedded backticks are doubled for escaping.</summary>
    [Fact]
    public void EscapeIdentifier_doubles_backticks()
    {
        // Arrange
        var helper = CreateHelper();

        // Act
        var result = helper.DelimitIdentifier("my`table");

        // Assert
        Assert.Equal("`my``table`", result);
    }

    /// <summary>Schema-qualified identifiers produce schema.name pattern.</summary>
    [Fact]
    public void DelimitIdentifier_with_schema_produces_qualified_name()
    {
        // Arrange
        var helper = CreateHelper();

        // Act
        var result = helper.DelimitIdentifier("MyTable", "myschema");

        // Assert
        Assert.Equal("`myschema`.`MyTable`", result);
    }

    /// <summary>Statement terminator is semicolon.</summary>
    [Fact]
    public void StatementTerminator_is_semicolon()
    {
        // Arrange
        var helper = CreateHelper();

        // Act
        var terminator = helper.StatementTerminator;

        // Assert
        Assert.Equal(";", terminator);
    }

    /// <summary>StringBuilder overload produces identical output.</summary>
    [Fact]
    public void DelimitIdentifier_StringBuilder_matches_string_overload()
    {
        // Arrange
        var helper = CreateHelper();
        var sb = new System.Text.StringBuilder();

        // Act
        helper.DelimitIdentifier(sb, "Test`Col");
        var actual = sb.ToString();

        // Assert
        Assert.Equal("`Test``Col`", actual);
    }

    /// <summary>
    /// String and StringBuilder overloads must produce byte-identical output for the
    /// no-backtick fast-path, the per-char slow-path, and edge cases (leading,
    /// trailing, consecutive, single-char backticks). The dual-overload contract is
    /// the safety net for the hot-path optimization: any divergence would surface as
    /// inconsistent SQL between two call sites that quote the same identifier.
    /// </summary>
    [Theory]
    [InlineData("Customer", "`Customer`")]
    [InlineData("a", "`a`")]
    [InlineData("`leading", "```leading`")]
    [InlineData("trailing`", "`trailing```")]
    [InlineData("`both`", "```both```")]
    [InlineData("`", "````")]
    [InlineData("``", "``````")]
    [InlineData("Order`Line", "`Order``Line`")]
    [InlineData("a`b`c", "`a``b``c`")]
    [InlineData("`a`b`c`", "```a``b``c```")]
    [InlineData("name_with_underscores", "`name_with_underscores`")]
    [InlineData("MixedCaseIdentifier", "`MixedCaseIdentifier`")]
    public void DelimitIdentifier_string_and_builder_overloads_agree(
        string identifier,
        string expected
    )
    {
        // Arrange
        var helper = CreateHelper();
        var builderOutput = new System.Text.StringBuilder();

        // Act
        helper.DelimitIdentifier(builderOutput, identifier);
        var actual = helper.DelimitIdentifier(identifier);

        // Assert
        Assert.Equal(expected, actual);
        Assert.Equal(expected, builderOutput.ToString());
    }

    /// <summary>
    /// EscapeIdentifier without delimiters: fast-path returns the original string,
    /// slow-path doubles every backtick. Both the string and StringBuilder overloads
    /// must agree.
    /// </summary>
    [Theory]
    [InlineData("Customer", "Customer")]
    [InlineData("a`b", "a``b")]
    [InlineData("`", "``")]
    [InlineData("``", "````")]
    [InlineData("no_special", "no_special")]
    public void EscapeIdentifier_string_and_builder_overloads_agree(
        string identifier,
        string expected
    )
    {
        // Arrange
        var helper = (MySqlSqlGenerationHelper)CreateHelper();
        var builderOutput = new System.Text.StringBuilder();

        // Act
        helper.EscapeIdentifier(builderOutput, identifier);
        var actual = helper.EscapeIdentifier(identifier);

        // Assert
        Assert.Equal(expected, actual);
        Assert.Equal(expected, builderOutput.ToString());
    }

    /// <summary>
    /// Schema-qualified DelimitIdentifier delegates to two single-identifier calls.
    /// Both overloads must produce the same schema.name shape and honor the fast vs
    /// slow path independently per identifier (schema fast-path, name slow-path is a
    /// valid mixed case).
    /// </summary>
    [Theory]
    [InlineData("Plain", "schema", "`schema`.`Plain`")]
    [InlineData("a`b", "schema", "`schema`.`a``b`")]
    [InlineData("Plain", "sch`ema", "`sch``ema`.`Plain`")]
    [InlineData("a`b", "sch`ema", "`sch``ema`.`a``b`")]
    public void DelimitIdentifier_with_schema_overloads_agree(
        string name,
        string schema,
        string expected
    )
    {
        // Arrange
        var helper = CreateHelper();
        var builderOutput = new System.Text.StringBuilder();

        // Act
        helper.DelimitIdentifier(builderOutput, name, schema);
        var actual = helper.DelimitIdentifier(name, schema);

        // Assert
        Assert.Equal(expected, actual);
        Assert.Equal(expected, builderOutput.ToString());
    }

    /// <summary>
    /// Null or whitespace schema collapses to the single-name overload.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void DelimitIdentifier_with_null_or_whitespace_schema_omits_prefix(
        string? schema
    )
    {
        // Arrange
        var helper = CreateHelper();
        var builderOutput = new System.Text.StringBuilder();

        // Act
        helper.DelimitIdentifier(builderOutput, "Plain", schema);
        var actual = helper.DelimitIdentifier("Plain", schema);

        // Assert
        Assert.Equal("`Plain`", actual);
        Assert.Equal("`Plain`", builderOutput.ToString());
    }

    private static ISqlGenerationHelper CreateHelper()
    {
        var builder = new DbContextOptionsBuilder();
        builder.UseMySql(
            "Server=localhost;Database=doka;User ID=root;Password=password;",
            MySqlServerVersion.MySql(new Version(8, 4, 0)));

        var context = new HelperTestContext(builder.Options);

        return context.GetService<ISqlGenerationHelper>();
    }

    private sealed class HelperTestContext : DbContext
    {
        public HelperTestContext(
            DbContextOptions options
        ) : base(options) { }

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        ) => modelBuilder.Entity<HelperTestEntity>(e => { e.HasKey(x => x.Id); });
    }

    private sealed class HelperTestEntity
    {
        public int Id { get; set; }
    }
}
