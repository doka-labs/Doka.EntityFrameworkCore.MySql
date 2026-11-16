namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlConnector3CompatibilityTests
{
    /// <summary>Separates full, short, empty and null variable-length binary payloads.</summary>
    public static TheoryData<string, string?> VariableBinaryCases
    {
        get
        {
            var cases = new TheoryData<string, string?>();
            foreach (var target in GetSelectedSupportedTargets())
            {
                cases.Add(target.ToString(), "00112233445566778899AABBCCDDEEFF");
                cases.Add(target.ToString(), "001122");
                cases.Add(target.ToString(), string.Empty);
                cases.Add(target.ToString(), null);
            }

            return cases;
        }
    }

    /// <summary>Distinguishes the corrected variable-binary reader from legacy GUID inference.</summary>
    /// <param name="target">The selected native database engine.</param>
    /// <param name="payloadHex">The exact payload, or null for SQL NULL.</param>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(VariableBinaryCases))]
    public async Task Variable_binary16_get_value_preserves_the_qualified_driver_contract(
        string target,
        string? payloadHex
    )
    {
        // Arrange
        var cancellationToken = CancellationToken.None;
        await using var connection = new MySqlConnection(CreateConnectionString(target));
        await connection.OpenAsync(cancellationToken);
        await ExecuteAsync(connection, "CREATE TEMPORARY TABLE `C3VariableBinary` (`Value` varbinary(16) NULL);", cancellationToken);
        var literal = payloadHex is null ? "NULL" : $"X'{payloadHex}'";
        await ExecuteAsync(connection, $"INSERT INTO `C3VariableBinary` VALUES ({literal});", cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT `Value` FROM `C3VariableBinary`;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        Assert.True(await reader.ReadAsync(cancellationToken));
        object? actual = null;

        // Act
        var failure = Record.Exception(() =>
        {
            actual = reader.GetValue(0);
        });

        // Assert
        if (payloadHex is null)
        {
            Assert.Null(failure);
            Assert.Equal(DBNull.Value, actual);
        }
        else if (UsesConnector3)
        {
            Assert.Null(failure);
            Assert.Equal(Convert.FromHexString(payloadHex), Assert.IsType<byte[]>(actual));
        }
        else if (payloadHex.Length == 32)
        {
            Assert.Null(failure);
            Assert.Equal(Convert.FromHexString(payloadHex), Assert.IsType<Guid>(actual).ToByteArray(bigEndian: true));
        }
        else
        {
            // WHY: Connector 2 treats declared VARBINARY(16) as Guid even when
            // fewer bytes are stored; pin its exact constructor rejection.
            var invalid = Assert.IsType<ArgumentException>(failure);
            Assert.Equal("b", invalid.ParamName);
            Assert.Null(actual);
        }
    }

    /// <summary>Typed GUID reads preserve valid bytes and reject short, empty or null carriers.</summary>
    /// <param name="target">The selected native database engine.</param>
    /// <param name="payloadHex">The exact payload, or null for SQL NULL.</param>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(VariableBinaryCases))]
    public async Task Typed_guid_reader_preserves_valid_variable_binary16_and_rejects_invalid_carriers(
        string target,
        string? payloadHex
    )
    {
        // Arrange
        var cancellationToken = CancellationToken.None;
        await using var connection = new MySqlConnection(CreateConnectionString(target));
        await connection.OpenAsync(cancellationToken);
        await ExecuteAsync(connection, "CREATE TEMPORARY TABLE `C3TypedGuid` (`Value` varbinary(16) NULL);", cancellationToken);
        var literal = payloadHex is null ? "NULL" : $"X'{payloadHex}'";
        await ExecuteAsync(connection, $"INSERT INTO `C3TypedGuid` VALUES ({literal});", cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT `Value` FROM `C3TypedGuid`;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        Assert.True(await reader.ReadAsync(cancellationToken));
        Guid? actual = null;

        // Act
        var failure = Record.Exception(() =>
        {
            actual = reader.GetGuid(0);
        });

        // Assert
        if (payloadHex is { Length: 32 })
        {
            Assert.Null(failure);
            Assert.NotNull(actual);
            Assert.Equal(Convert.FromHexString(payloadHex), actual.Value.ToByteArray(bigEndian: true));
        }
        else if (!UsesConnector3 && payloadHex is not null)
        {
            var invalid = Assert.IsType<ArgumentException>(failure);
            Assert.Equal("b", invalid.ParamName);
            Assert.Null(actual);
        }
        else
        {
            Assert.IsType<InvalidCastException>(failure);
            Assert.Null(actual);
        }
    }

    /// <summary>The fixed-binary GUID carrier retains its established CLR identity on both driver lines.</summary>
    /// <param name="target">The selected native database engine.</param>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(Targets))]
    public async Task Fixed_binary16_get_value_remains_a_guid(
        string target
    )
    {
        // Arrange
        const string hex = "00112233445566778899AABBCCDDEEFF";
        var cancellationToken = CancellationToken.None;
        await using var connection = new MySqlConnection(CreateConnectionString(target));
        await connection.OpenAsync(cancellationToken);
        await ExecuteAsync(connection, "CREATE TEMPORARY TABLE `C3FixedGuid` (`Value` binary(16) NOT NULL);", cancellationToken);
        await ExecuteAsync(connection, $"INSERT INTO `C3FixedGuid` VALUES (X'{hex}');", cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT `Value` FROM `C3FixedGuid`;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        Assert.True(await reader.ReadAsync(cancellationToken));

        // Act
        var actual = reader.GetValue(0);

        // Assert
        Assert.Equal(Convert.FromHexString(hex), Assert.IsType<Guid>(actual).ToByteArray(bigEndian: true));
    }
}
