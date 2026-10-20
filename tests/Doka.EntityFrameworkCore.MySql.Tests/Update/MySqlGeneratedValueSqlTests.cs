using System.Text;
using Microsoft.EntityFrameworkCore.Update;

namespace Doka.EntityFrameworkCore.MySql.Tests;

/// <summary>
/// Protects keyed generated-value SQL and the physical-key eligibility boundary.
/// </summary>
public sealed partial class MySqlGeneratedValueSqlTests
{
    /// <summary>
    /// Emits one insert and one readback, with a row-relative ordinal and row-count guard in every arm.
    /// </summary>
    [Theory]
    [InlineData(2)]
    [InlineData(64)]
    [InlineData(1000)]
    public void KnownPhysicalKeysUseOneInsertAndOnePositionalReadback(
        int count
    )
    {
        using var context = CreateContext();
        var commands = CreateCommands(context, "Rows", count);
        var generator = (MySqlUpdateSqlGenerator)context.GetService<IUpdateSqlGenerator>();
        var sql = new StringBuilder();

        var mapping = generator.AppendBulkInsertOperation(sql, commands, commandPosition: 71, out var transaction);

        var text = sql.ToString();
        Assert.Equal(ResultSetMapping.NotLastInResultSet | ResultSetMapping.IsPositionalResultMappingEnabled, mapping);
        Assert.True(transaction);
        Assert.Equal(1, Regex.Count(text, "INSERT INTO"));
        Assert.Equal(2, text.Count(character => character == ';'));
        Assert.Equal(count - 1, Regex.Count(text, "UNION ALL"));
        Assert.Equal(count, Regex.Count(text, $"ROW_COUNT\\(\\) = {count} AND `Id` = @p"));
        Assert.Contains("SELECT `Generated`, 0", text, StringComparison.Ordinal);
        Assert.Contains($"SELECT `Generated`, {count - 1}", text, StringComparison.Ordinal);
        Assert.DoesNotContain("LAST_INSERT_ID", text, StringComparison.Ordinal);
        Assert.DoesNotContain("ORDER BY", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// Uses every physical key column and preserves its native type and collation comparison.
    /// </summary>
    [Theory]
    [InlineData("ShadowRows")]
    [InlineData("ExplicitInnoDbRows")]
    [InlineData("CompositeRows")]
    [InlineData("GuidRows")]
    [InlineData("ConvertedRows")]
    [InlineData("StringRows")]
    public void CompositeShadowConvertedAndGuidKeysKeepNativeEquality(
        string tableName
    )
    {
        using var context = CreateContext();
        var commands = CreateCommands(context, tableName, 2);
        var generator = (MySqlUpdateSqlGenerator)context.GetService<IUpdateSqlGenerator>();
        var sql = new StringBuilder();

        var mapping = generator.AppendBulkInsertOperation(sql, commands, 0, out var transaction);

        Assert.True(mapping.HasFlag(ResultSetMapping.IsPositionalResultMappingEnabled));
        Assert.True(transaction);
        Assert.Contains("`Id` = @p0_0", sql.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("CAST", sql.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("COLLATE", sql.ToString(), StringComparison.Ordinal);
        if (tableName == "CompositeRows")
        {
            Assert.Contains("AND `Tenant` = @p0_1", sql.ToString(), StringComparison.Ordinal);
            Assert.Contains("AND `Tenant` = @p1_1", sql.ToString(), StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// A zero-valued key remains stable when its property is explicitly never generated.
    /// </summary>
    [Fact]
    public void NeverGeneratedZeroKeyIsNotMistakenForTemporaryIdentity()
    {
        using var context = CreateContext();
        var commands = CreateCommands(context, "Rows", 2, firstKey: 0);
        var generator = (MySqlUpdateSqlGenerator)context.GetService<IUpdateSqlGenerator>();
        var sql = new StringBuilder();

        var mapping = generator.AppendBulkInsertOperation(sql, commands, 0, out _);

        Assert.True(mapping.HasFlag(ResultSetMapping.IsPositionalResultMappingEnabled));
        Assert.Equal(0, commands[0].ColumnModifications[0].Value);
    }

    /// <summary>
    /// One unsafe command forces the entire group onto its original per-row path.
    /// </summary>
    [Theory]
    [InlineData("null-key")]
    [InlineData("missing-key")]
    [InlineData("read-key")]
    [InlineData("alternate-key-only")]
    [InlineData("different-table")]
    [InlineData("extra-read-column")]
    [InlineData("different-write-order")]
    [InlineData("no-physical-table")]
    [InlineData("wrong-state")]
    [InlineData("wrong-table-name")]
    [InlineData("wrong-schema")]
    [InlineData("foreign-column")]
    public void UnsafeLaterCommandDoesNotEnterPositionalReadback(
        string failure
    )
    {
        using var context = CreateContext();
        var commands = CreateCommands(context, "Rows", 2);
        commands[1] = UnsafeCommand(context, commands[1], failure);
        var generator = (MySqlUpdateSqlGenerator)context.GetService<IUpdateSqlGenerator>();
        var sql = new StringBuilder();

        var mapping = generator.AppendBulkInsertOperation(sql, commands, 0, out _);

        Assert.False(mapping.HasFlag(ResultSetMapping.IsPositionalResultMappingEnabled));
        Assert.DoesNotContain("UNION ALL", sql.ToString(), StringComparison.Ordinal);
        Assert.Equal(2, Regex.Count(sql.ToString(), "INSERT INTO"));
    }

    /// <summary>
    /// Modeled triggers, temporal emulation and nontransactional tables retain the safe fallback.
    /// </summary>
    [Theory]
    [InlineData("TriggeredRows")]
    [InlineData("TemporalRows")]
    [InlineData("NontransactionalRows")]
    [InlineData("SharedTriggeredRows")]
    [InlineData("SharedEngineRows")]
    public void UnsafePhysicalTableMetadataRetainsPerRowReadback(
        string tableName
    )
    {
        using var context = CreateContext();
        var commands = CreateCommands(context, tableName, 2);
        var generator = (MySqlUpdateSqlGenerator)context.GetService<IUpdateSqlGenerator>();
        var sql = new StringBuilder();

        var mapping = generator.AppendBulkInsertOperation(sql, commands, 0, out _);

        Assert.Equal(ResultSetMapping.LastInResultSet, mapping);
        Assert.Equal(2, Regex.Count(sql.ToString(), "INSERT INTO"));
        Assert.DoesNotContain("UNION ALL", sql.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// A custom sentinel cannot turn MySQL's physical auto-increment zero into a stable written key.
    /// </summary>
    [Fact]
    public void WrittenAutoIncrementZeroRetainsPerRowReadback()
    {
        using var context = CreateContext();
        var commands = CreateCommands(context, "ZeroSentinelIdentityRows", 2, firstKey: 0);
        var generator = (MySqlUpdateSqlGenerator)context.GetService<IUpdateSqlGenerator>();
        var sql = new StringBuilder();

        var mapping = generator.AppendBulkInsertOperation(sql, commands, 0, out _);

        Assert.Equal(ResultSetMapping.LastInResultSet, mapping);
        Assert.Equal(2, Regex.Count(sql.ToString(), "INSERT INTO"));
        Assert.DoesNotContain("UNION ALL", sql.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Every supported integer key shape treats a written zero as a server-generated identity.
    /// </summary>
    [Theory]
    [MemberData(nameof(NumericIdentityTables))]
    public void NumericAutoIncrementZeroCannotCorrelateReadback(
        string tableName
    )
    {
        using var context = CreateContext();
        var commands = CreateCommands(context, tableName, 2, firstKey: 0);
        var generator = (MySqlUpdateSqlGenerator)context.GetService<IUpdateSqlGenerator>();
        var sql = new StringBuilder();

        var mapping = generator.AppendBulkInsertOperation(sql, commands, 0, out _);

        Assert.Equal(ResultSetMapping.LastInResultSet, mapping);
        Assert.Equal(2, Regex.Count(sql.ToString(), "INSERT INTO"));
        Assert.DoesNotContain("UNION ALL", sql.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Explicit nonzero integer identities remain stable across the insert and keyed readback.
    /// </summary>
    [Theory]
    [MemberData(nameof(NumericIdentityTables))]
    public void NumericAutoIncrementNonzeroKeysCanCorrelateReadback(
        string tableName
    )
    {
        using var context = CreateContext();
        var commands = CreateCommands(context, tableName, 2);
        var generator = (MySqlUpdateSqlGenerator)context.GetService<IUpdateSqlGenerator>();
        var sql = new StringBuilder();

        var mapping = generator.AppendBulkInsertOperation(sql, commands, 0, out _);

        Assert.True(mapping.HasFlag(ResultSetMapping.IsPositionalResultMappingEnabled));
        Assert.Equal(1, Regex.Count(sql.ToString(), "INSERT INTO"));
        Assert.Contains("UNION ALL", sql.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Supplies all signed and unsigned integer CLR types accepted by the identity guard.
    /// </summary>
    public static IEnumerable<object[]> NumericIdentityTables()
    {
        yield return ["SByteIdentityRows"];
        yield return ["ByteIdentityRows"];
        yield return ["Int16IdentityRows"];
        yield return ["UInt16IdentityRows"];
        yield return ["Int32IdentityRows"];
        yield return ["UInt32IdentityRows"];
        yield return ["Int64IdentityRows"];
        yield return ["UInt64IdentityRows"];
    }

    /// <summary>
    /// Temporary generated keys retain the existing identity-based per-row protocol.
    /// </summary>
    [Fact]
    public void TrackedTemporaryKeysCannotEnablePositionalReadback()
    {
        using var context = CreateContext();
        context.AddRange(new IdentityRow { Payload = 11, }, new IdentityRow { Payload = 22, });
        var entries = context.GetService<IUpdateAdapterFactory>().Create().Entries.ToArray();
        var commands = CreateTrackedCommands(context, "IdentityRows", entries);
        var generator = (MySqlUpdateSqlGenerator)context.GetService<IUpdateSqlGenerator>();
        var sql = new StringBuilder();

        var mapping = generator.AppendBulkInsertOperation(sql, commands, 0, out var transaction);

        Assert.True(transaction);
        Assert.Equal(ResultSetMapping.LastInResultSet, mapping);
        Assert.Contains("LAST_INSERT_ID()", sql.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("UNION ALL", sql.ToString(), StringComparison.Ordinal);
        Assert.All(
            entries,
            entry => Assert.True(entry.HasTemporaryValue(entry.EntityType.FindPrimaryKey()!.Properties[0])));
    }

    /// <summary>
    /// Conventional client-generated GUIDs remain eligible when EF writes a known, non-temporary key.
    /// </summary>
    [Fact]
    public void ClientGeneratedGuidOnAddRetainsKeyedReadback()
    {
        using var context = CreateContext();
        context.AddRange(new GuidIdentityRow { Payload = 11, }, new GuidIdentityRow { Payload = 22, });
        var entries = context.GetService<IUpdateAdapterFactory>().Create().Entries.ToArray();
        var commands = CreateTrackedCommands(context, "GuidIdentityRows", entries);
        var generator = (MySqlUpdateSqlGenerator)context.GetService<IUpdateSqlGenerator>();
        var sql = new StringBuilder();

        var mapping = generator.AppendBulkInsertOperation(sql, commands, 0, out var transaction);

        Assert.True(transaction);
        Assert.True(mapping.HasFlag(ResultSetMapping.IsPositionalResultMappingEnabled));
        Assert.All(entries, entry =>
        {
            var key = entry.EntityType.FindPrimaryKey()!.Properties[0];
            Assert.Equal(ValueGenerated.OnAdd, key.ValueGenerated);
            Assert.False(entry.HasTemporaryValue(key));
            Assert.NotEqual(Guid.Empty, entry.GetCurrentValue<Guid>(key));
        });
        Assert.All(commands, command =>
        {
            var key = Assert.Single(command.ColumnModifications, modification => modification.IsKey);
            Assert.True(key.IsWrite);
            Assert.False(key.IsRead);
        });
    }

    /// <summary>
    /// The batch can explicitly retain per-row SQL when no transaction can protect keyed readback.
    /// </summary>
    [Fact]
    public void TransactionPermissionCannotBeAssumedByTheGenerator()
    {
        using var context = CreateContext();
        var commands = CreateCommands(context, "Rows", 2);
        var generator = (MySqlUpdateSqlGenerator)context.GetService<IUpdateSqlGenerator>();
        var sql = new StringBuilder();

        var mapping = generator.AppendBulkInsertOperation(sql, commands, 0, allowKeyedReadback: false, out _);

        Assert.Equal(ResultSetMapping.LastInResultSet, mapping);
        Assert.Equal(2, Regex.Count(sql.ToString(), "INSERT INTO"));
        Assert.DoesNotContain("UNION ALL", sql.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Repeated physical key values count toward packet size without doubling the INSERT placeholder limit.
    /// </summary>
    [Theory]
    [InlineData("Rows", 1)]
    [InlineData("CompositeRows", 2)]
    [InlineData("ConvertedRows", 1)]
    [InlineData("StringRows", 1)]
    public void PacketCostCountsOnlyRepeatedPhysicalKeys(
        string tableName,
        int expectedKeyParameters
    )
    {
        using var context = CreateContext();
        var commands = CreateCommands(context, tableName, 2);
        var generator = (MySqlUpdateSqlGenerator)context.GetService<IUpdateSqlGenerator>();
        var sql = new StringBuilder();

        var eligible = generator.TryGetKeyedReadbackCost(commands[0], out var packetBytes);
        generator.AppendBulkInsertOperation(sql, commands, 0, out _);

        Assert.True(eligible);
        Assert.Equal(4 * expectedKeyParameters, Regex.Count(sql.ToString(), "@p[0-9]+_[0-9]+\\b"));
        Assert.InRange(packetBytes, 1, 10_000);
    }

    /// <summary>
    /// Native RETURNING and unsafe table contracts add no keyed-readback packet reservation.
    /// </summary>
    [Theory]
    [InlineData(false, "TriggeredRows")]
    [InlineData(false, "TemporalRows")]
    [InlineData(false, "SharedEngineRows")]
    [InlineData(true, "Rows")]
    public void FallbackDoesNotReserveKeyedReadbackCost(
        bool mariaDb,
        string tableName
    )
    {
        using var context = CreateContext(mariaDb);
        var command = CreateCommands(context, tableName, 2)[0];
        var generator = (MySqlUpdateSqlGenerator)context.GetService<IUpdateSqlGenerator>();

        var eligible = generator.TryGetKeyedReadbackCost(command, out var packetBytes);

        Assert.False(eligible);
        Assert.Equal(0, packetBytes);
    }

    /// <summary>
    /// MariaDB continues to use its native multirow RETURNING statement.
    /// </summary>
    [Fact]
    public void MariaDbReadbackPreservesReturningWithoutOrdinal()
    {
        using var context = CreateContext(mariaDb: true);
        var commands = CreateCommands(context, "Rows", 2);
        var generator = (MySqlUpdateSqlGenerator)context.GetService<IUpdateSqlGenerator>();
        var sql = new StringBuilder();

        var mapping = generator.AppendBulkInsertOperation(sql, commands, 0, out var transaction);

        Assert.Equal(ResultSetMapping.NotLastInResultSet, mapping);
        Assert.False(transaction);
        Assert.Contains("RETURNING `Generated`", sql.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("UNION ALL", sql.ToString(), StringComparison.Ordinal);
    }
}
