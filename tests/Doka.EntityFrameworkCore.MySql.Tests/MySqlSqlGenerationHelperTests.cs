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
        var helper = CreateHelper();
        var result = helper.DelimitIdentifier("MyTable");
        Assert.Equal("`MyTable`", result);
    }

    /// <summary>Embedded backticks are doubled for escaping.</summary>
    [Fact]
    public void EscapeIdentifier_doubles_backticks()
    {
        var helper = CreateHelper();
        var result = helper.DelimitIdentifier("my`table");
        Assert.Equal("`my``table`", result);
    }

    /// <summary>Schema-qualified identifiers produce schema.name pattern.</summary>
    [Fact]
    public void DelimitIdentifier_with_schema_produces_qualified_name()
    {
        var helper = CreateHelper();
        var result = helper.DelimitIdentifier("MyTable", "myschema");
        Assert.Equal("`myschema`.`MyTable`", result);
    }

    /// <summary>Statement terminator is semicolon.</summary>
    [Fact]
    public void StatementTerminator_is_semicolon()
    {
        var helper = CreateHelper();
        Assert.Equal(";", helper.StatementTerminator);
    }

    /// <summary>StringBuilder overload produces identical output.</summary>
    [Fact]
    public void DelimitIdentifier_StringBuilder_matches_string_overload()
    {
        var helper = CreateHelper();
        var sb = new System.Text.StringBuilder();
        helper.DelimitIdentifier(sb, "Test`Col");
        Assert.Equal("`Test``Col`", sb.ToString());
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
