namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Provider-specific extension methods for <see cref="DbFunctions"/>.
/// </summary>
public static class MySqlDbFunctionsExtensions
{
    /// <summary>
    /// Translates to <c>REGEXP_LIKE(input, pattern)</c> on MySQL 8.0+ or
    /// <c>input REGEXP pattern</c> on MariaDB.
    /// </summary>
    /// <param name="functions">The <see cref="DbFunctions"/> instance.</param>
    /// <param name="input">The input string to match against.</param>
    /// <param name="pattern">The regular expression pattern.</param>
    /// <returns><c>true</c> if the input matches the pattern.</returns>
    public static bool Regexp(
        this DbFunctions functions,
        string input,
        string pattern
    ) => throw new InvalidOperationException("This method is for use with EF Core LINQ queries only.");

    /// <summary>
    /// Translates to MySQL <c>MATCH(columns) AGAINST('term')</c> for full-text search.
    /// </summary>
    /// <param name="functions">The <see cref="DbFunctions"/> instance.</param>
    /// <param name="column">The column to search.</param>
    /// <param name="searchTerm">The search term.</param>
    /// <returns><c>true</c> if the column matches the search term.</returns>
    public static bool Match(
        this DbFunctions functions,
        string column,
        string searchTerm
    ) => throw new InvalidOperationException("This method is for use with EF Core LINQ queries only.");

    /// <summary>
    /// Translates to MySQL <c>MATCH(columns) AGAINST('term' IN BOOLEAN MODE)</c>.
    /// </summary>
    /// <param name="functions">The <see cref="DbFunctions"/> instance.</param>
    /// <param name="column">The column to search.</param>
    /// <param name="searchTerm">The boolean-mode search expression.</param>
    /// <returns><c>true</c> if the column matches the search term in boolean mode.</returns>
    public static bool MatchInBooleanMode(
        this DbFunctions functions,
        string column,
        string searchTerm
    ) => throw new InvalidOperationException("This method is for use with EF Core LINQ queries only.");

    // -- JSON Manipulation --

    /// <summary>
    /// Translates to MySQL <c>JSON_SET(json, path, value)</c>.
    /// Sets a value in a JSON document, inserting if the path does not exist.
    /// </summary>
    public static string JsonSet(
        this DbFunctions functions,
        string json,
        string path,
        object value
    ) => throw new InvalidOperationException("This method is for use with EF Core LINQ queries only.");

    /// <summary>
    /// Translates to MySQL <c>JSON_REPLACE(json, path, value)</c>.
    /// Replaces a value in a JSON document only if the path exists.
    /// </summary>
    public static string JsonReplace(
        this DbFunctions functions,
        string json,
        string path,
        object value
    ) => throw new InvalidOperationException("This method is for use with EF Core LINQ queries only.");

    /// <summary>
    /// Translates to MySQL <c>JSON_REMOVE(json, path)</c>.
    /// Removes a value from a JSON document at the specified path.
    /// </summary>
    public static string JsonRemove(
        this DbFunctions functions,
        string json,
        string path
    ) => throw new InvalidOperationException("This method is for use with EF Core LINQ queries only.");

    // -- JSON Construction --

    /// <summary>
    /// Translates to MySQL <c>JSON_ARRAY(val1, val2, ...)</c>.
    /// Constructs a JSON array from the provided values.
    /// </summary>
    public static string JsonArray(
        this DbFunctions functions,
        params object[] values
    ) => throw new InvalidOperationException("This method is for use with EF Core LINQ queries only.");

    /// <summary>
    /// Translates to MySQL <c>JSON_OBJECT(key1, val1, key2, val2, ...)</c>.
    /// Constructs a JSON object from key-value pairs.
    /// </summary>
    public static string JsonObject(
        this DbFunctions functions,
        params object[] keyValuePairs
    ) => throw new InvalidOperationException("This method is for use with EF Core LINQ queries only.");

    // -- JSON Inspection --

    /// <summary>
    /// Translates to MySQL <c>JSON_DEPTH(json)</c>.
    /// Returns the maximum depth of a JSON document.
    /// </summary>
    public static int JsonDepth(
        this DbFunctions functions,
        string json
    ) => throw new InvalidOperationException("This method is for use with EF Core LINQ queries only.");

    /// <summary>
    /// Translates to MySQL <c>JSON_LENGTH(json)</c>.
    /// Returns the number of elements in a JSON array or keys in a JSON object.
    /// </summary>
    public static int JsonLength(
        this DbFunctions functions,
        string json
    ) => throw new InvalidOperationException("This method is for use with EF Core LINQ queries only.");

    /// <summary>
    /// Translates to MySQL <c>JSON_TYPE(json)</c>.
    /// Returns the type of the outermost JSON value as a string.
    /// </summary>
    public static string JsonType(
        this DbFunctions functions,
        string json
    ) => throw new InvalidOperationException("This method is for use with EF Core LINQ queries only.");

    /// <summary>
    /// Translates to MySQL <c>JSON_KEYS(json)</c>.
    /// Returns the keys from the top-level value of a JSON object as a JSON array.
    /// </summary>
    public static string JsonKeys(
        this DbFunctions functions,
        string json
    ) => throw new InvalidOperationException("This method is for use with EF Core LINQ queries only.");

    /// <summary>
    /// Translates to MySQL <c>JSON_CONTAINS(target, candidate)</c>.
    /// Returns whether <paramref name="candidate"/> is contained within <paramref name="target"/>.
    /// </summary>
    public static bool JsonContains(
        this DbFunctions functions,
        string target,
        string candidate
    ) => throw new InvalidOperationException("This method is for use with EF Core LINQ queries only.");
}
