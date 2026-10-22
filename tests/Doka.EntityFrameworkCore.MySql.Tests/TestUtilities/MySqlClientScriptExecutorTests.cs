namespace Doka.EntityFrameworkCore.MySql.Tests;

/// <summary>
/// Verifies the client-only delimiter processing used by migration script
/// conformance tests.
/// </summary>
public sealed class MySqlClientScriptExecutorTests
{
    /// <summary>
    /// Verifies custom delimiter preserves the complete routine body.
    /// </summary>
    [Fact]
    public void Custom_delimiter_preserves_the_complete_routine_body()
    {
        // Arrange
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

        // Act
        var statements = MySqlClientScriptExecutor.ParseStatements(script);

        // Assert
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

    /// <summary>
    /// Verifies delimiter tokens inside comments and escaped literals do not split.
    /// </summary>
    [Fact]
    public void Delimiter_tokens_inside_comments_and_escaped_literals_do_not_split()
    {
        // Arrange
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

        // Act
        var statements = MySqlClientScriptExecutor.ParseStatements(script);

        // Assert
        Assert.Equal(2, statements.Count);
        Assert.Contains("'not // split'", statements[0], StringComparison.Ordinal);
        Assert.Contains("-- not // split", statements[0], StringComparison.Ordinal);
        Assert.Equal("SELECT 2", statements[1]);
    }

    /// <summary>
    /// Verifies malformed client scripts fail before execution.
    /// </summary>
    [Theory]
    [InlineData("DELIMITER   \nSELECT 1;")]
    [InlineData("DELIMITER one two\nSELECT 1;")]
    [InlineData("SELECT 'unterminated;")]
    [InlineData("SELECT 1 /* unterminated;")]
    public void Malformed_client_scripts_fail_before_execution(
        string script
    )
    {
        // Arrange
        Action act = () =>
            MySqlClientScriptExecutor.ParseStatements(script);

        // Act
        var recordedException = Record.Exception(act);

        // Assert
        Assert.IsType<InvalidOperationException>(recordedException);
    }
}
