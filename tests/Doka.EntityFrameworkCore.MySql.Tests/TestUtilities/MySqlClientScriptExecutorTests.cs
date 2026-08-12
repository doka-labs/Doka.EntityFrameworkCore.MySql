namespace Doka.EntityFrameworkCore.MySql.Tests;

/// <summary>
/// Verifies the client-only delimiter processing used by migration script
/// conformance tests.
/// </summary>
public sealed class MySqlClientScriptExecutorTests
{
    [Fact]
    public void Custom_delimiter_preserves_the_complete_routine_body()
    {
        const string script = """
                              DROP PROCEDURE IF EXISTS `apply_migration`;
                              DELIMITER //
                              CREATE PROCEDURE `apply_migration`()
                              BEGIN
                                  SELECT 'literal;value';
                                  SELECT `semi;colon`;
                              END //
                              DELIMITER ;
                              CALL `apply_migration`();
                              DROP PROCEDURE IF EXISTS `apply_migration`;
                              """;

        var statements = MySqlClientScriptExecutor.ParseStatements(script);

        Assert.Collection(
            statements,
            statement => Assert.Equal("DROP PROCEDURE IF EXISTS `apply_migration`", statement),
            statement => Assert.Equal(
                """
                CREATE PROCEDURE `apply_migration`()
                BEGIN
                    SELECT 'literal;value';
                    SELECT `semi;colon`;
                END
                """,
                statement),
            statement => Assert.Equal("CALL `apply_migration`()", statement),
            statement => Assert.Equal("DROP PROCEDURE IF EXISTS `apply_migration`", statement));
    }

    [Fact]
    public void Delimiter_tokens_inside_comments_and_escaped_literals_do_not_split()
    {
        const string script = """
                              DELIMITER //
                              CREATE PROCEDURE `quoted``name`()
                              BEGIN
                                  SELECT 'not // split', 'escaped \' // value';
                                  -- not // split
                                  /* not // split */
                                  SELECT 1;
                              END //
                              DELIMITER ;
                              SELECT 2;
                              """;

        var statements = MySqlClientScriptExecutor.ParseStatements(script);

        Assert.Equal(2, statements.Count);
        Assert.Contains("'not // split'", statements[0], StringComparison.Ordinal);
        Assert.Contains("-- not // split", statements[0], StringComparison.Ordinal);
        Assert.Equal("SELECT 2", statements[1]);
    }

    [Theory]
    [InlineData("DELIMITER   \nSELECT 1;")]
    [InlineData("DELIMITER one two\nSELECT 1;")]
    [InlineData("SELECT 'unterminated;")]
    [InlineData("SELECT 1 /* unterminated;")]
    public void Malformed_client_scripts_fail_before_execution(
        string script
    )
    {
        Assert.Throws<InvalidOperationException>(() =>
            MySqlClientScriptExecutor.ParseStatements(script));
    }
}
