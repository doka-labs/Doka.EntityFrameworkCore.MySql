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

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ResolveValueGenerated_for_null_or_blank_returns_null(string? extra) =>
        Assert.Null(ScaffoldingHelpers.ResolveValueGenerated(extra));

    [Theory]
    [InlineData("auto_increment")]
    [InlineData("AUTO_INCREMENT")]
    [InlineData("on update CURRENT_TIMESTAMP auto_increment")]
    public void ResolveValueGenerated_for_auto_increment_returns_on_add(string extra) =>
        Assert.Equal(ValueGenerated.OnAdd, ScaffoldingHelpers.ResolveValueGenerated(extra));

    [Theory]
    [InlineData("on update CURRENT_TIMESTAMP")]
    [InlineData("VIRTUAL GENERATED")]
    public void ResolveValueGenerated_for_other_extras_returns_null(string extra) =>
        Assert.Null(ScaffoldingHelpers.ResolveValueGenerated(extra));

    // -- ResolveIsStored --

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ResolveIsStored_for_null_or_blank_returns_null(string? extra) =>
        Assert.Null(ScaffoldingHelpers.ResolveIsStored(extra));

    [Theory]
    [InlineData("STORED GENERATED")]
    [InlineData("stored generated")]
    public void ResolveIsStored_for_stored_generated_returns_true(string extra) =>
        Assert.True(ScaffoldingHelpers.ResolveIsStored(extra));

    [Theory]
    [InlineData("VIRTUAL GENERATED")]
    [InlineData("virtual generated")]
    public void ResolveIsStored_for_virtual_generated_returns_false(string extra) =>
        Assert.False(ScaffoldingHelpers.ResolveIsStored(extra));

    [Theory]
    [InlineData("auto_increment")]
    [InlineData("on update CURRENT_TIMESTAMP")]
    public void ResolveIsStored_for_other_extras_returns_null(string extra) =>
        Assert.Null(ScaffoldingHelpers.ResolveIsStored(extra));

    // -- ResolveReferentialAction (the 12-branch switch) --

    [Fact]
    public void ResolveReferentialAction_for_null_returns_null() =>
        Assert.Null(ScaffoldingHelpers.ResolveReferentialAction(null));

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
        ReferentialAction expected) =>
        Assert.Equal(expected, ScaffoldingHelpers.ResolveReferentialAction(deleteRule));

    [Theory]
    [InlineData("UNKNOWN")]
    [InlineData("")]
    public void ResolveReferentialAction_for_unknown_rule_returns_null(string deleteRule) =>
        Assert.Null(ScaffoldingHelpers.ResolveReferentialAction(deleteRule));

    // -- DeriveCharSetFromCollation --

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void DeriveCharSetFromCollation_for_null_or_blank_returns_null(string? collation) =>
        Assert.Null(ScaffoldingHelpers.DeriveCharSetFromCollation(collation));

    [Theory]
    [InlineData("utf8mb4_0900_ai_ci", "utf8mb4")]
    [InlineData("latin1_general_cs", "latin1")]
    [InlineData("ascii_bin", "ascii")]
    public void DeriveCharSetFromCollation_for_underscore_form_returns_prefix(
        string collation,
        string expected) =>
        Assert.Equal(expected, ScaffoldingHelpers.DeriveCharSetFromCollation(collation));

    [Theory]
    [InlineData("utf8mb4")]
    [InlineData("binary")]
    public void DeriveCharSetFromCollation_for_no_underscore_returns_null(string collation) =>
        Assert.Null(ScaffoldingHelpers.DeriveCharSetFromCollation(collation));

    // -- ExtractJsonValidColumnName --

    [Theory]
    [InlineData("(`payload` <> 'x')")]
    [InlineData("CHECK (length(name) > 0)")]
    public void ExtractJsonValidColumnName_without_json_valid_returns_null(string clause) =>
        Assert.Null(ScaffoldingHelpers.ExtractJsonValidColumnName(clause));

    [Theory]
    [InlineData("json_valid(`payload`)", "payload")]
    [InlineData("json_valid(payload)", "payload")]
    [InlineData("JSON_VALID(`data`)", "data")]
    [InlineData("JSON_VALID(`weird_name`)", "weird_name")]
    public void ExtractJsonValidColumnName_returns_unquoted_column(string clause, string expected) =>
        Assert.Equal(expected, ScaffoldingHelpers.ExtractJsonValidColumnName(clause));

    [Theory]
    [InlineData("json_valid()")]
    [InlineData("json_valid(   )")]
    [InlineData("json_valid(``)")]
    public void ExtractJsonValidColumnName_for_empty_argument_returns_null(string clause) =>
        Assert.Null(ScaffoldingHelpers.ExtractJsonValidColumnName(clause));

    // -- AppendTableNameFilter --

    [Fact]
    public void AppendTableNameFilter_for_match_all_returns_zero_and_appends_nothing()
    {
        var sql = new StringBuilder("SELECT 1");
        using var conn = new MySqlConnection();
        using var command = conn.CreateCommand();

        var count = ScaffoldingHelpers.AppendTableNameFilter(sql, command, TableFilter.MatchAll);

        Assert.Equal(0, count);
        Assert.Equal("SELECT 1", sql.ToString());
        Assert.Empty(command.Parameters);
    }

    [Fact]
    public void AppendTableNameFilter_for_single_table_emits_one_parameter()
    {
        var sql = new StringBuilder("SELECT 1");
        using var conn = new MySqlConnection();
        using var command = conn.CreateCommand();

        var count = ScaffoldingHelpers.AppendTableNameFilter(
            sql,
            command,
            TableFilter.For(["orders"]));

        Assert.Equal(1, count);
        Assert.EndsWith(" AND TABLE_NAME IN (@t0)", sql.ToString());
        Assert.Single(command.Parameters);
    }

    [Fact]
    public void AppendTableNameFilter_for_multiple_tables_emits_indexed_parameters()
    {
        var sql = new StringBuilder("SELECT 1");
        using var conn = new MySqlConnection();
        using var command = conn.CreateCommand();

        var count = ScaffoldingHelpers.AppendTableNameFilter(
            sql,
            command,
            TableFilter.For(["orders", "customers", "products"]));

        Assert.Equal(3, count);
        var emitted = sql.ToString();

        Assert.Contains("@t0", emitted);
        Assert.Contains("@t1", emitted);
        Assert.Contains("@t2", emitted);
        Assert.Equal(3, command.Parameters.Count);
    }

    [Fact]
    public void AppendTableNameFilter_with_custom_column_reference_uses_it()
    {
        var sql = new StringBuilder("SELECT 1");
        using var conn = new MySqlConnection();
        using var command = conn.CreateCommand();

        var count = ScaffoldingHelpers.AppendTableNameFilter(
            sql,
            command,
            TableFilter.For(["t"]),
            columnReference: "T.NAME");

        Assert.Equal(1, count);
        Assert.Contains(" AND T.NAME IN (@t0)", sql.ToString());
    }
}
