using System.Text;

namespace Doka.EntityFrameworkCore.MySql.Tests;

/// <summary>
/// Tests for pure static helpers in <c>ScaffoldingHelpers</c>: input-table coverage for
/// ResolveValueGenerated, ResolveIsStored, the 12-branch ResolveReferentialAction switch,
/// DeriveCharSetFromCollation, ExtractJsonValidColumnName, and AppendTableNameFilter.
/// </summary>
public sealed class ScaffoldingHelpersTests
{
    // -- ResolveValueGenerated --

    /// <summary>
    /// Verifies <c>ResolveValueGenerated</c> for null or blank returns null.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ResolveValueGenerated_for_null_or_blank_returns_null(string? extra)
    {
        // Arrange: The theory supplies extra.

        // Act
        var actual = ScaffoldingHelpers.ResolveValueGenerated(extra);

        // Assert
        Assert.Null(actual);
    }

    /// <summary>
    /// Verifies <c>ResolveValueGenerated</c> for auto increment returns on add.
    /// </summary>
    [Theory]
    [InlineData("auto_increment")]
    [InlineData("AUTO_INCREMENT")]
    [InlineData("on update CURRENT_TIMESTAMP auto_increment")]
    public void ResolveValueGenerated_for_auto_increment_returns_on_add(string extra)
    {
        // Arrange: The theory supplies extra.

        // Act
        var actual = ScaffoldingHelpers.ResolveValueGenerated(extra);

        // Assert
        Assert.Equal(ValueGenerated.OnAdd, actual);
    }

    /// <summary>
    /// Verifies <c>ResolveValueGenerated</c> for other extras returns null.
    /// </summary>
    [Theory]
    [InlineData("on update CURRENT_TIMESTAMP")]
    [InlineData("VIRTUAL GENERATED")]
    public void ResolveValueGenerated_for_other_extras_returns_null(string extra)
    {
        // Arrange: The theory supplies extra.

        // Act
        var actual = ScaffoldingHelpers.ResolveValueGenerated(extra);

        // Assert
        Assert.Null(actual);
    }

    // -- ResolveIsStored --

    /// <summary>
    /// Verifies <c>ResolveIsStored</c> for null or blank returns null.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ResolveIsStored_for_null_or_blank_returns_null(string? extra)
    {
        // Arrange: The theory supplies extra.

        // Act
        var actual = ScaffoldingHelpers.ResolveIsStored(extra);

        // Assert
        Assert.Null(actual);
    }

    /// <summary>
    /// Verifies <c>ResolveIsStored</c> for stored generated returns true.
    /// </summary>
    [Theory]
    [InlineData("STORED GENERATED")]
    [InlineData("stored generated")]
    public void ResolveIsStored_for_stored_generated_returns_true(string extra)
    {
        // Arrange: The theory supplies extra.

        // Act
        var actual = ScaffoldingHelpers.ResolveIsStored(extra);

        // Assert
        Assert.True(actual);
    }

    /// <summary>
    /// Verifies <c>ResolveIsStored</c> for virtual generated returns false.
    /// </summary>
    [Theory]
    [InlineData("VIRTUAL GENERATED")]
    [InlineData("virtual generated")]
    public void ResolveIsStored_for_virtual_generated_returns_false(string extra)
    {
        // Arrange: The theory supplies extra.

        // Act
        var actual = ScaffoldingHelpers.ResolveIsStored(extra);

        // Assert
        Assert.False(actual);
    }

    /// <summary>
    /// Verifies <c>ResolveIsStored</c> for other extras returns null.
    /// </summary>
    [Theory]
    [InlineData("auto_increment")]
    [InlineData("on update CURRENT_TIMESTAMP")]
    public void ResolveIsStored_for_other_extras_returns_null(string extra)
    {
        // Arrange: The theory supplies extra.

        // Act
        var actual = ScaffoldingHelpers.ResolveIsStored(extra);

        // Assert
        Assert.Null(actual);
    }

    // -- ResolveReferentialAction (the 12-branch switch) --

    /// <summary>
    /// Verifies <c>ResolveReferentialAction</c> for null returns null.
    /// </summary>
    [Fact]
    public void ResolveReferentialAction_for_null_returns_null()
    {
        // Arrange
        string? deleteRule = null;

        // Act
        var actual = ScaffoldingHelpers.ResolveReferentialAction(deleteRule);

        // Assert
        Assert.Null(actual);
    }

    /// <summary>
    /// Verifies <c>ResolveReferentialAction</c> for known rules returns mapped action.
    /// </summary>
    [Theory]
    [InlineData("CASCADE", ReferentialAction.Cascade)]
    [InlineData("cascade", ReferentialAction.Cascade)]
    [InlineData("SET NULL", ReferentialAction.SetNull)]
    [InlineData("set null", ReferentialAction.SetNull)]
    [InlineData("SET DEFAULT", ReferentialAction.SetDefault)]
    [InlineData("RESTRICT", ReferentialAction.Restrict)]
    [InlineData("NO ACTION", ReferentialAction.NoAction)]
    [InlineData("no action", ReferentialAction.NoAction)]
    public void ResolveReferentialAction_for_known_rules_returns_mapped_action(
        string deleteRule,
        ReferentialAction expected)
    {
        // Arrange: The theory supplies deleteRule, expected.

        // Act
        var actual = ScaffoldingHelpers.ResolveReferentialAction(deleteRule);

        // Assert
        Assert.Equal(expected, actual);
    }

    /// <summary>
    /// Verifies <c>ResolveReferentialAction</c> for unknown rule returns null.
    /// </summary>
    [Theory]
    [InlineData("UNKNOWN")]
    [InlineData("")]
    public void ResolveReferentialAction_for_unknown_rule_returns_null(string deleteRule)
    {
        // Arrange: The theory supplies deleteRule.

        // Act
        var actual = ScaffoldingHelpers.ResolveReferentialAction(deleteRule);

        // Assert
        Assert.Null(actual);
    }

    // -- DeriveCharSetFromCollation --

    /// <summary>
    /// Verifies <c>DeriveCharSetFromCollation</c> for null or blank returns null.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void DeriveCharSetFromCollation_for_null_or_blank_returns_null(string? collation)
    {
        // Arrange: The theory supplies collation.

        // Act
        var actual = ScaffoldingHelpers.DeriveCharSetFromCollation(collation);

        // Assert
        Assert.Null(actual);
    }

    /// <summary>
    /// Verifies <c>DeriveCharSetFromCollation</c> for underscore form returns prefix.
    /// </summary>
    [Theory]
    [InlineData("utf8mb4_0900_ai_ci", "utf8mb4")]
    [InlineData("latin1_general_cs", "latin1")]
    [InlineData("ascii_bin", "ascii")]
    public void DeriveCharSetFromCollation_for_underscore_form_returns_prefix(
        string collation,
        string expected)
    {
        // Arrange: The theory supplies collation, expected.

        // Act
        var actual = ScaffoldingHelpers.DeriveCharSetFromCollation(collation);

        // Assert
        Assert.Equal(expected, actual);
    }

    /// <summary>
    /// Verifies <c>DeriveCharSetFromCollation</c> for no underscore returns null.
    /// </summary>
    [Theory]
    [InlineData("utf8mb4")]
    [InlineData("binary")]
    public void DeriveCharSetFromCollation_for_no_underscore_returns_null(string collation)
    {
        // Arrange: The theory supplies collation.

        // Act
        var actual = ScaffoldingHelpers.DeriveCharSetFromCollation(collation);

        // Assert
        Assert.Null(actual);
    }

    // -- ExtractJsonValidColumnName --

    /// <summary>
    /// Verifies <c>ExtractJsonValidColumnName</c> without json valid returns null.
    /// </summary>
    [Theory]
    [InlineData("(`payload` <> 'x')")]
    [InlineData("CHECK (length(name) > 0)")]
    public void ExtractJsonValidColumnName_without_json_valid_returns_null(string clause)
    {
        // Arrange: The theory supplies clause.

        // Act
        var actual = ScaffoldingHelpers.ExtractJsonValidColumnName(clause);

        // Assert
        Assert.Null(actual);
    }

    /// <summary>
    /// Verifies <c>ExtractJsonValidColumnName</c> returns unquoted column.
    /// </summary>
    [Theory]
    [InlineData("json_valid(`payload`)", "payload")]
    [InlineData("json_valid(payload)", "payload")]
    [InlineData("JSON_VALID(`data`)", "data")]
    [InlineData("JSON_VALID(`weird_name`)", "weird_name")]
    public void ExtractJsonValidColumnName_returns_unquoted_column(string clause, string expected)
    {
        // Arrange: The theory supplies clause, expected.

        // Act
        var actual = ScaffoldingHelpers.ExtractJsonValidColumnName(clause);

        // Assert
        Assert.Equal(expected, actual);
    }

    /// <summary>
    /// Verifies <c>ExtractJsonValidColumnName</c> for noncanonical clause returns null.
    /// </summary>
    [Theory]
    [InlineData("json_valid()")]
    [InlineData("json_valid(   )")]
    [InlineData("json_valid(``)")]
    [InlineData("(`payload` IS NULL OR json_valid(`payload`))")]
    [InlineData("json_valid(`payload`) AND `payload` IS NOT NULL")]
    [InlineData("NOT json_valid(`payload`)")]
    [InlineData("json_valid(`payload`.`value`)")]
    public void ExtractJsonValidColumnName_for_noncanonical_clause_returns_null(string clause)
    {
        // Arrange: The theory supplies clause.

        // Act
        var actual = ScaffoldingHelpers.ExtractJsonValidColumnName(clause);

        // Assert
        Assert.Null(actual);
    }

    // -- AppendTableNameFilter --

    /// <summary>
    /// Verifies <c>AppendTableNameFilter</c> for match all returns zero and appends nothing.
    /// </summary>
    [Fact]
    public void AppendTableNameFilter_for_match_all_returns_zero_and_appends_nothing()
    {
        // Arrange
        var sql = new StringBuilder("SELECT 1");
        using var conn = new MySqlConnection();
        using var command = conn.CreateCommand();

        // Act
        var count = ScaffoldingHelpers.AppendTableNameFilter(sql, command, TableFilter.MatchAll);

        // Assert
        Assert.Equal(0, count);
        Assert.Equal("SELECT 1", sql.ToString());
        Assert.Empty(command.Parameters);
    }

    /// <summary>
    /// Verifies <c>AppendTableNameFilter</c> for single table emits one parameter.
    /// </summary>
    [Fact]
    public void AppendTableNameFilter_for_single_table_emits_one_parameter()
    {
        // Arrange
        var sql = new StringBuilder("SELECT 1");
        using var conn = new MySqlConnection();
        using var command = conn.CreateCommand();

        // Act
        var count = ScaffoldingHelpers.AppendTableNameFilter(
            sql,
            command,
            TableFilter.For(["orders"]));

        // Assert
        Assert.Equal(1, count);
        Assert.EndsWith(" AND TABLE_NAME IN (@t0)", sql.ToString());
        Assert.Single(command.Parameters);
    }

    /// <summary>
    /// Verifies <c>AppendTableNameFilter</c> for multiple tables emits indexed parameters.
    /// </summary>
    [Fact]
    public void AppendTableNameFilter_for_multiple_tables_emits_indexed_parameters()
    {
        // Arrange
        var sql = new StringBuilder("SELECT 1");
        using var conn = new MySqlConnection();
        using var command = conn.CreateCommand();

        // Act
        var count = ScaffoldingHelpers.AppendTableNameFilter(
            sql,
            command,
            TableFilter.For(["orders", "customers", "products"]));

        // Assert
        Assert.Equal(3, count);
        var emitted = sql.ToString();
        Assert.Contains("@t0", emitted);
        Assert.Contains("@t1", emitted);
        Assert.Contains("@t2", emitted);
        Assert.Equal(3, command.Parameters.Count);
    }

    /// <summary>
    /// Verifies <c>AppendTableNameFilter</c> with custom column reference uses it.
    /// </summary>
    [Fact]
    public void AppendTableNameFilter_with_custom_column_reference_uses_it()
    {
        // Arrange
        var sql = new StringBuilder("SELECT 1");
        using var conn = new MySqlConnection();
        using var command = conn.CreateCommand();

        // Act
        var count = ScaffoldingHelpers.AppendTableNameFilter(
            sql,
            command,
            TableFilter.For(["t"]),
            columnReference: "T.NAME");

        // Assert
        Assert.Equal(1, count);
        Assert.Contains(" AND T.NAME IN (@t0)", sql.ToString());
    }
}
