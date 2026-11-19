namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlCteIntegrationTests
{
    private const string ReaderPresence = "__doka_name_presence";
    private const string ReaderInvalidLimit = "__doka_name_invalid";
    private const string ReaderNameSql = "SELECT 7 AS payload, 1 AS __doka_name_presence, 0 AS __doka_name_invalid;";

    private static readonly string[] s_visibleReaderNames = ["payload", "PAYLOAD"];
    private static readonly int[] s_invalidReaderOrdinals = [-1, 1, 2];

    private static readonly string[] s_missingReaderNames =
    [
        ReaderPresence,
        ReaderPresence.ToUpperInvariant(),
        ReaderInvalidLimit,
        ReaderInvalidLimit.ToUpperInvariant(),
        "missing",
    ];

    /// <summary>Reports visible native name spellings and both named lookup paths per engine.</summary>
    public static TheoryData<string, string, bool> VisibleReaderNameCases
    {
        get
        {
            var cases = new TheoryData<string, string, bool>();

            foreach (var target in SelectedTargets)
            {
                foreach (var name in s_visibleReaderNames)
                {
                    cases.Add(target, name, false);
                    cases.Add(target, name, true);
                }
            }

            return cases;
        }
    }

    /// <summary>Reports both private aliases and a genuinely missing native name independently.</summary>
    public static TheoryData<string, string, bool> MissingReaderNameCases
    {
        get
        {
            var cases = new TheoryData<string, string, bool>();

            foreach (var target in SelectedTargets)
            {
                foreach (var name in s_missingReaderNames)
                {
                    cases.Add(target, name, false);
                    cases.Add(target, name, true);
                }
            }

            return cases;
        }
    }

    /// <summary>Reports negative and hidden native ordinals through value getter and indexer paths.</summary>
    public static TheoryData<string, int, bool> InvalidReaderOrdinalCases
    {
        get
        {
            var cases = new TheoryData<string, int, bool>();

            foreach (var target in SelectedTargets)
            {
                foreach (var ordinal in s_invalidReaderOrdinals)
                {
                    cases.Add(target, ordinal, false);
                    cases.Add(target, ordinal, true);
                }
            }

            return cases;
        }
    }

    /// <summary>The recursive projection preserves the actual connector's visible name matching and value access.</summary>
    /// <param name="target">The selected pinned database engine.</param>
    /// <param name="name">The visible name spelling.</param>
    /// <param name="indexer">Whether to retrieve the value rather than its ordinal.</param>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(VisibleReaderNameCases))]
    public async Task Native_recursive_visible_names_preserve_connector_lookup(
        string target,
        string name,
        bool indexer
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        var options = new MySqlConnectionStringBuilder(
            IntegrationTestEnvironment.GetBaseConnectionString(Enum.Parse<IntegrationDatabaseTarget>(target)))
        {
            Pooling = false,
            AutoEnlist = false,
        };

        await using var connection = new MySqlConnection(options.ConnectionString);
        await connection.OpenAsync(safety.Token);
        await using var command = new MySqlCommand(ReaderNameSql, connection);
        await using var raw = await command.ExecuteReaderAsync(safety.Token);
        var policy = new MySqlRecursiveQueryPolicy([], ReaderPresence, ReaderInvalidLimit, 1);
        await using var reader = new MySqlRecursiveDataReader(raw, policy);
        Assert.True(await reader.ReadAsync(safety.Token));

        // Act
        var actual = indexer
            ? reader[name]
            : reader.GetOrdinal(name);

        // Assert
        Assert.Equal(indexer
                ? 7
                : 0,
            Convert.ToInt32(actual, CultureInfo.InvariantCulture));

        Assert.Equal(1, reader.FieldCount);
    }

    /// <summary>Private control names and absent names expose the same native missing-column exception contract.</summary>
    /// <param name="target">The selected pinned database engine.</param>
    /// <param name="name">The private or absent name.</param>
    /// <param name="indexer">Whether to exercise the named value indexer.</param>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(MissingReaderNameCases))]
    public async Task Native_recursive_hidden_and_missing_names_have_the_same_contract(
        string target,
        string name,
        bool indexer
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        var options = new MySqlConnectionStringBuilder(
            IntegrationTestEnvironment.GetBaseConnectionString(Enum.Parse<IntegrationDatabaseTarget>(target)))
        {
            Pooling = false,
            AutoEnlist = false,
        };

        await using var connection = new MySqlConnection(options.ConnectionString);
        await connection.OpenAsync(safety.Token);
        await using var command = new MySqlCommand(ReaderNameSql, connection);
        await using var raw = await command.ExecuteReaderAsync(safety.Token);
        var policy = new MySqlRecursiveQueryPolicy([], ReaderPresence, ReaderInvalidLimit, 1);
        await using var reader = new MySqlRecursiveDataReader(raw, policy);
        Assert.True(await reader.ReadAsync(safety.Token));

        // Act
        var exception = Record.Exception(() => indexer
            ? reader[name]
            : reader.GetOrdinal(name));

        // Assert
        var missing = Assert.IsType<IndexOutOfRangeException>(exception);
        Assert.Equal($"The column name '{name}' does not exist in the result set.", missing.Message);
        Assert.Equal(1, reader.FieldCount);
    }

    /// <summary>Negative indexes and both private controls follow the native invalid-ordinal exception contract.</summary>
    /// <param name="target">The selected pinned database engine.</param>
    /// <param name="ordinal">The invalid or hidden ordinal.</param>
    /// <param name="indexer">Whether to exercise the integer value indexer.</param>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(InvalidReaderOrdinalCases))]
    public async Task Native_recursive_invalid_ordinals_follow_reader_contract(
        string target,
        int ordinal,
        bool indexer
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        var options = new MySqlConnectionStringBuilder(
            IntegrationTestEnvironment.GetBaseConnectionString(Enum.Parse<IntegrationDatabaseTarget>(target)))
        {
            Pooling = false,
            AutoEnlist = false,
        };

        await using var connection = new MySqlConnection(options.ConnectionString);
        await connection.OpenAsync(safety.Token);
        await using var command = new MySqlCommand(ReaderNameSql, connection);
        await using var raw = await command.ExecuteReaderAsync(safety.Token);
        var policy = new MySqlRecursiveQueryPolicy([], ReaderPresence, ReaderInvalidLimit, 1);
        await using var reader = new MySqlRecursiveDataReader(raw, policy);
        Assert.True(await reader.ReadAsync(safety.Token));

        // Act
        var exception = Record.Exception(() => indexer
            ? reader[ordinal]
            : reader.GetValue(ordinal));

        // Assert
        Assert.IsType<IndexOutOfRangeException>(exception);
        Assert.Equal(1, reader.FieldCount);
    }
}
