namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

/// <summary>Contains the json cases and shared support.</summary>
public sealed partial class MySqlNewFeatureTests
{
    // -- JSON Manipulation Functions --

    /// <summary>
    /// EF.Functions.JsonSet translates to JSON_SET SQL.
    /// </summary>
    [Fact]
    public void JsonSet_translates_to_json_set_sql()
    {
        // Arrange
        using var context = CreateContext<JsonFunctionContext>();

        // Act
        var query = context
            .Set<JsonFunctionEntity>()
            .Select(e => EF.Functions.JsonSet(e.Data, "$.name", "test"))
            .ToQueryString();

        // Assert
        MySqlSqlAssert.ContainsFunction(query, "JSON_SET");
    }

    /// <summary>
    /// EF.Functions.JsonRemove translates to JSON_REMOVE SQL.
    /// </summary>
    [Fact]
    public void JsonRemove_translates_to_json_remove_sql()
    {
        // Arrange
        using var context = CreateContext<JsonFunctionContext>();

        // Act
        var query = context
            .Set<JsonFunctionEntity>()
            .Select(e => EF.Functions.JsonRemove(e.Data, "$.name"))
            .ToQueryString();

        // Assert
        MySqlSqlAssert.ContainsFunction(query, "JSON_REMOVE");
    }

    /// <summary>
    /// Ordinary C# params syntax is flattened into independent JSON_ARRAY SQL
    /// arguments while captured values remain parameters.
    /// </summary>
    [Fact]
    public void JsonArray_params_translate_to_variadic_parameterized_sql()
    {
        // Arrange
        using var context = CreateContext<JsonFunctionContext>();
        var suffix = "tail";

        // Act
        var query = context
            .Set<JsonFunctionEntity>()
            .Select(entity => EF.Functions.JsonArray(entity.Data, suffix))
            .ToQueryString();

        // Assert
        Assert.Contains("JSON_ARRAY(`j`.`Data", query, StringComparison.Ordinal);
        Assert.Contains("@suffix", query, StringComparison.Ordinal);
    }

    /// <summary>
    /// JSON_OBJECT receives alternating keys and values as individual SQL arguments.
    /// </summary>
    [Fact]
    public void JsonObject_params_translate_to_variadic_sql()
    {
        // Arrange
        using var context = CreateContext<JsonFunctionContext>();

        // Act
        var query = context
            .Set<JsonFunctionEntity>()
            .Select(entity => EF.Functions.JsonObject("data", entity.Data, "id", entity.Id))
            .ToQueryString();

        // Assert
        Assert.Contains("JSON_OBJECT('data', `j`.`Data`, 'id', `j`.`Id`)", query, StringComparison.Ordinal);
    }

    /// <summary>
    /// SQL NULL remains an argument of each JSON constructor instead of making the
    /// constructor untranslatable or null-propagating the complete result.
    /// </summary>
    [Theory]
    [InlineData(false, "JSON_ARRAY(`j`.`Data`, NULL)")]
    [InlineData(true, "JSON_OBJECT('data', `j`.`Data`, 'missing', NULL)")]
    public void Json_constructors_translate_null_arguments_as_json_null_inputs(
        bool jsonObject,
        string expected
    )
    {
        // Arrange
        using var context = CreateContext<JsonFunctionContext>();

        // Act
        var sql = jsonObject
            ? context.Set<JsonFunctionEntity>()
                .Select(entity => EF.Functions.JsonObject("data", entity.Data, "missing", null)).ToQueryString()
            : context.Set<JsonFunctionEntity>()
                .Select(entity => EF.Functions.JsonArray(entity.Data, null)).ToQueryString();

        // Assert
        Assert.Contains(expected, sql, StringComparison.Ordinal);
    }

    /// <summary>
    /// Empty params arrays map to the engines' documented empty JSON constructors.
    /// </summary>
    [Theory]
    [InlineData(false, "JSON_ARRAY()")]
    [InlineData(true, "JSON_OBJECT()")]
    public void Empty_json_constructors_translate_without_arguments(
        bool jsonObject,
        string expected
    )
    {
        // Arrange
        using var context = CreateContext<JsonFunctionContext>();

        // Act
        var sql = jsonObject
            ? context.Set<JsonFunctionEntity>().Select(_ => EF.Functions.JsonObject()).ToQueryString()
            : context.Set<JsonFunctionEntity>().Select(_ => EF.Functions.JsonArray()).ToQueryString();

        // Assert
        Assert.Contains(expected, sql, StringComparison.Ordinal);
    }

    /// <summary>
    /// An incomplete key/value pair is rejected during translation with a focused
    /// provider diagnostic instead of reaching query execution.
    /// </summary>
    [Fact]
    public void JsonObject_rejects_an_odd_argument_count_before_execution()
    {
        // Arrange
        using var context = CreateContext<JsonFunctionContext>();

        // Act
        var exception = Assert.Throws<InvalidOperationException>(() => context
            .Set<JsonFunctionEntity>()
            .Select(entity => EF.Functions.JsonObject("data", entity.Data, "orphan"))
            .ToQueryString());

        // Assert
        Assert.Contains("even number of key and value arguments", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Composite client objects cannot cross the JSON-construction translation
    /// boundary because their representation is not a database scalar.
    /// </summary>
    [Fact]
    public void JsonArray_rejects_an_unsupported_nested_client_value_before_execution()
    {
        // Arrange
        using var context = CreateContext<JsonFunctionContext>();

        // Act
        var exception = Assert.Throws<InvalidOperationException>(() => context
            .Set<JsonFunctionEntity>()
            .Select(entity => EF.Functions.JsonArray(new UnsupportedJsonValue(entity.Data)))
            .ToQueryString());

        // Assert
        Assert.Contains("argument 1 cannot be translated to SQL", exception.Message, StringComparison.Ordinal);
    }

    // -- JSON Inspection Functions --

    /// <summary>
    /// EF.Functions.JsonDepth translates to JSON_DEPTH SQL.
    /// </summary>
    [Fact]
    public void JsonDepth_translates_to_json_depth_sql()
    {
        // Arrange
        using var context = CreateContext<JsonFunctionContext>();

        // Act
        var query = context
            .Set<JsonFunctionEntity>()
            .Select(e => EF.Functions.JsonDepth(e.Data))
            .ToQueryString();

        // Assert
        MySqlSqlAssert.ContainsFunction(query, "JSON_DEPTH");
    }

    /// <summary>
    /// EF.Functions.JsonLength translates to JSON_LENGTH SQL.
    /// </summary>
    [Fact]
    public void JsonLength_translates_to_json_length_sql()
    {
        // Arrange
        using var context = CreateContext<JsonFunctionContext>();

        // Act
        var query = context
            .Set<JsonFunctionEntity>()
            .Select(e => EF.Functions.JsonLength(e.Data))
            .ToQueryString();

        // Assert
        MySqlSqlAssert.ContainsFunction(query, "JSON_LENGTH");
    }

    /// <summary>
    /// EF.Functions.JsonType translates to JSON_TYPE SQL.
    /// </summary>
    [Fact]
    public void JsonType_translates_to_json_type_sql()
    {
        // Arrange
        using var context = CreateContext<JsonFunctionContext>();

        // Act
        var query = context
            .Set<JsonFunctionEntity>()
            .Select(e => EF.Functions.JsonType(e.Data))
            .ToQueryString();

        // Assert
        MySqlSqlAssert.ContainsFunction(query, "JSON_TYPE");
    }

    /// <summary>
    /// EF.Functions.JsonKeys translates to JSON_KEYS SQL.
    /// </summary>
    [Fact]
    public void JsonKeys_translates_to_json_keys_sql()
    {
        // Arrange
        using var context = CreateContext<JsonFunctionContext>();

        // Act
        var query = context
            .Set<JsonFunctionEntity>()
            .Select(e => EF.Functions.JsonKeys(e.Data))
            .ToQueryString();

        // Assert
        MySqlSqlAssert.ContainsFunction(query, "JSON_KEYS");
    }

    /// <summary>
    /// EF.Functions.JsonContains translates to JSON_CONTAINS SQL.
    /// </summary>
    [Fact]
    public void JsonContains_translates_to_json_contains_sql()
    {
        // Arrange
        using var context = CreateContext<JsonFunctionContext>();

        // Act
        var query = context
            .Set<JsonFunctionEntity>()
            .Where(e => EF.Functions.JsonContains(e.Data, "\"value\""))
            .ToQueryString();

        // Assert
        MySqlSqlAssert.ContainsFunction(query, "JSON_CONTAINS");
    }
}
