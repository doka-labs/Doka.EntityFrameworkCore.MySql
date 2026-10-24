using System.Text;
using Microsoft.EntityFrameworkCore.Update;

namespace Doka.EntityFrameworkCore.MySql.Tests;

public sealed partial class MySqlGeneratedValueSqlTests
{
    /// <summary>Counts literal physical keys without reserving nonexistent parameter names.</summary>
    /// <param name="tableName">The mapped key type and converter contract.</param>
    /// <param name="expectedValueBytes">The conservative encoded cost of the repeated key value.</param>
    [Theory]
    [InlineData("Rows", 256L)]
    [InlineData("StringRows", 26L)]
    [InlineData("ConvertedRows", 256L)]
    public void Literal_physical_key_cost_excludes_parameter_names(
        string tableName,
        long expectedValueBytes
    )
    {
        // Arrange
        using var context = CreateContext();
        var command = WithLiteralPhysicalKey(CreateCommands(context, tableName, 1)[0]);
        var generator = (MySqlUpdateSqlGenerator)context.GetService<IUpdateSqlGenerator>();
        var expectedBytes = expectedValueBytes
            + (4L * (96 + (2 * tableName.Length) + 6 + (2 * "Generated".Length) + 16 + (2 * "Id".Length)));

        // Act
        var eligible = generator.TryGetKeyedReadbackCost(command, out var packetBytes);

        // Assert
        Assert.True(eligible);
        Assert.False(command.ColumnModifications[0].UseCurrentValueParameter);
        Assert.Equal(expectedBytes, packetBytes);
    }

    /// <summary>Distinguishes generated zero from stable converted keys in both descriptor forms.</summary>
    /// <param name="key">The explicit provider key, including MySQL's implicit identity sentinel.</param>
    /// <param name="parameterized">Whether the descriptor carries a model value or a provider literal.</param>
    /// <param name="expectedEligible">Whether keyed readback can identify the inserted row.</param>
    [Theory]
    [InlineData(0, false, false)]
    [InlineData(17, false, true)]
    [InlineData(0, true, false)]
    [InlineData(17, true, true)]
    public void Converted_identity_keys_preserve_provider_value_and_zero_guard(
        int key,
        bool parameterized,
        bool expectedEligible
    )
    {
        // Arrange
        using var context = CreateContext();
        var original = CreateCommands(context, "ConvertedIdentityRows", 1, firstKey: key)[0];
        var command = parameterized ? original : WithLiteralPhysicalKey(original, key);
        var generator = (MySqlUpdateSqlGenerator)context.GetService<IUpdateSqlGenerator>();

        // Act
        var eligible = generator.TryGetKeyedReadbackCost(command, out var packetBytes);

        // Assert
        Assert.Equal(parameterized, command.ColumnModifications[0].UseCurrentValueParameter);
        Assert.Equal(expectedEligible, eligible);
        Assert.InRange(packetBytes, expectedEligible ? 1L : 0L, expectedEligible ? long.MaxValue : 0L);
    }

    /// <summary>Preserves literal escaping and conversion in both INSERT and keyed readback.</summary>
    /// <param name="tableName">The mapped key type and converter contract.</param>
    /// <param name="expectedLiteral">The independent SQL representation of the first row's key.</param>
    [Theory]
    [InlineData("Rows", "17")]
    [InlineData("StringRows", "_utf8mb4 X'6F776E657227735C70617468'")]
    [InlineData("ConvertedRows", "17")]
    public void Mixed_literal_and_parameter_keys_preserve_complete_readback_sql(
        string tableName,
        string expectedLiteral
    )
    {
        // Arrange
        using var context = CreateContext();
        var commands = CreateCommands(context, tableName, 2);
        commands[0] = WithLiteralPhysicalKey(commands[0]);
        var generator = (MySqlUpdateSqlGenerator)context.GetService<IUpdateSqlGenerator>();
        var sql = new StringBuilder();
        var expectedSql = ExpectedTwoRowReadbackSql(tableName, expectedLiteral);

        // Act
        var mapping = generator.AppendBulkInsertOperation(sql, commands, 0, out var transaction);

        // Assert
        Assert.Equal(expectedSql, sql.ToString(), ignoreLineEndingDifferences: true);
        Assert.Equal(ResultSetMapping.NotLastInResultSet | ResultSetMapping.IsPositionalResultMappingEnabled, mapping);
        Assert.True(transaction);
        Assert.False(commands[0].ColumnModifications[0].UseCurrentValueParameter);
        Assert.True(commands[1].ColumnModifications[0].UseCurrentValueParameter);
    }

    /// <summary>Ignores nonphysical descriptors that neither write nor read a value.</summary>
    /// <param name="position">The ignored descriptor's position among relevant descriptors.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    public void Ignored_descriptors_preserve_complete_keyed_readback_sql(
        int position
    )
    {
        // Arrange
        using var context = CreateContext();
        var commands = CreateCommands(context, "Rows", 2);
        var modifications = commands[1].ColumnModifications.ToList();
        modifications.Insert(position, CreateNonphysicalPayload(commands[1].Table!, read: false, write: false));
        commands[1] = new TestCommand(commands[1].Table, modifications);
        var generator = (MySqlUpdateSqlGenerator)context.GetService<IUpdateSqlGenerator>();
        var sql = new StringBuilder();
        var expectedSql = ExpectedTwoRowReadbackSql("Rows", "@p0_0");

        // Act
        var mapping = generator.AppendBulkInsertOperation(sql, commands, 0, out var transaction);

        // Assert
        Assert.Equal(expectedSql, sql.ToString(), ignoreLineEndingDifferences: true);
        Assert.Equal(ResultSetMapping.NotLastInResultSet | ResultSetMapping.IsPositionalResultMappingEnabled, mapping);
        Assert.True(transaction);
    }

    /// <summary>Requires equal ordered read and write shapes in both command-list directions.</summary>
    /// <param name="operation">The relevant operation whose count differs.</param>
    /// <param name="longerFirst">Whether the first command contains more relevant operations.</param>
    /// <param name="ignoredTail">Whether the shorter list ends in an irrelevant descriptor.</param>
    [Theory]
    [InlineData("read", false, false)]
    [InlineData("read", false, true)]
    [InlineData("read", true, false)]
    [InlineData("read", true, true)]
    [InlineData("write", false, false)]
    [InlineData("write", false, true)]
    [InlineData("write", true, false)]
    [InlineData("write", true, true)]
    public void Unequal_relevant_shapes_retain_per_row_readback(
        string operation,
        bool longerFirst,
        bool ignoredTail
    )
    {
        // Arrange
        using var context = CreateContext();
        var commands = CreateCommands(context, "Rows", 2);
        var longerIndex = longerFirst ? 0 : 1;
        var shorterIndex = 1 - longerIndex;
        var longer = commands[longerIndex].ColumnModifications.ToList();
        var shorter = commands[shorterIndex].ColumnModifications.ToList();
        var table = commands[0].Table!;

        if (operation == "read")
        {
            // WHY: Keep a read in both commands so this reaches shape comparison,
            // rather than rejecting a command before comparing its read descriptors.
            longer.Add(CreateModification(table.FindColumn("Payload")!, null, "extra_read", read: true, write: false));
        }
        else
        {
            shorter.RemoveAt(1);
        }

        if (ignoredTail)
        {
            shorter.Add(CreateNonphysicalPayload(table, read: false, write: false));
        }

        commands[longerIndex] = new TestCommand(table, longer);
        commands[shorterIndex] = new TestCommand(table, shorter);
        var generator = (MySqlUpdateSqlGenerator)context.GetService<IUpdateSqlGenerator>();
        var sql = new StringBuilder();

        // Act
        var mapping = generator.AppendBulkInsertOperation(sql, commands, 0, out var transaction);

        // Assert
        Assert.Equal(ResultSetMapping.LastInResultSet, mapping);
        Assert.True(transaction);
        Assert.Equal(2, Regex.Count(sql.ToString(), "INSERT INTO"));
        Assert.DoesNotContain("UNION ALL", sql.ToString(), StringComparison.Ordinal);
    }

    /// <summary>Rejects physical keys whose descriptor cannot supply a stable client-known value.</summary>
    /// <param name="failure">The unusable key value or metadata contract.</param>
    [Theory]
    [InlineData("dbnull")]
    [InlineData("missing-property")]
    [InlineData("not-written")]
    public void Unusable_physical_keys_do_not_reserve_readback_cost(
        string failure
    )
    {
        // Arrange
        using var context = CreateContext();
        var original = CreateCommands(context, "Rows", 1)[0];
        var column = original.Table!.PrimaryKey!.Columns[0];
        var property = column.PropertyMappings[0].Property;
        object value = failure == "dbnull" ? DBNull.Value : 1;
        var parameters = new ColumnModificationParameters(
            column,
            originalValue: null,
            value,
            property: failure == "missing-property" ? null : property,
            typeMapping: property.GetRelationalTypeMapping(),
            read: false,
            write: failure != "not-written",
            key: true,
            condition: false,
            sensitiveLoggingEnabled: false,
            isNullable: false);

        var modifications = original.ColumnModifications.ToArray();
        modifications[0] = new ColumnModification(parameters);
        var command = new TestCommand(original.Table, modifications);
        var generator = (MySqlUpdateSqlGenerator)context.GetService<IUpdateSqlGenerator>();

        // Act
        var eligible = generator.TryGetKeyedReadbackCost(command, out var packetBytes);

        // Assert
        Assert.False(eligible);
        Assert.Equal(0, packetBytes);
    }

    /// <summary>Requires physical column identity for every descriptor used by keyed readback.</summary>
    /// <param name="read">Whether the nonphysical descriptor reads a value.</param>
    /// <param name="write">Whether the nonphysical descriptor writes a value.</param>
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Relevant_nonphysical_descriptors_do_not_reserve_readback_cost(
        bool read,
        bool write
    )
    {
        // Arrange
        using var context = CreateContext();
        var original = CreateCommands(context, "Rows", 1)[0];
        var modifications = original.ColumnModifications.ToArray();
        modifications[1] = CreateNonphysicalPayload(original.Table!, read, write);
        var command = new TestCommand(original.Table, modifications);
        var generator = (MySqlUpdateSqlGenerator)context.GetService<IUpdateSqlGenerator>();

        // Act
        var eligible = generator.TryGetKeyedReadbackCost(command, out var packetBytes);

        // Assert
        Assert.Null(modifications[1].Column);
        Assert.False(eligible);
        Assert.Equal(0, packetBytes);
    }

    /// <summary>Retains physical metadata while exercising EF's literal-value descriptor overload.</summary>
    private static TestCommand WithLiteralPhysicalKey(
        IReadOnlyModificationCommand command,
        int key = 17
    )
    {
        var column = command.Table!.PrimaryKey!.Columns[0];
        var property = column.PropertyMappings[0].Property;
        object value = command.TableName switch
        {
            "Rows" => key,
            "StringRows" => "owner's\\path",
            "ConvertedRows" or "ConvertedIdentityRows" => key,
            _ => throw new ArgumentOutOfRangeException(nameof(command)),
        };

        // WHY: EF's nonparameterized descriptors already contain provider values;
        // GenerateProviderValueSqlLiteral must not convert the int key a second time.
        var parameters = new ColumnModificationParameters(
            column,
            originalValue: null,
            value,
            property,
            property.GetRelationalTypeMapping(),
            read: false,
            write: true,
            key: true,
            condition: false,
            sensitiveLoggingEnabled: false,
            isNullable: false);

        var modifications = command.ColumnModifications.ToArray();
        modifications[0] = new ColumnModification(parameters);

        return new TestCommand(command.Table, modifications);
    }

    /// <summary>Uses EF's name-based descriptor without an attached physical column.</summary>
    private static ColumnModification CreateNonphysicalPayload(
        ITable table,
        bool read,
        bool write
    )
    {
        var column = table.FindColumn("Payload")!;
        var property = column.PropertyMappings[0].Property;

        return new ColumnModification(new ColumnModificationParameters(
            columnName: column.Name,
            originalValue: null,
            value: 1,
            property,
            columnType: column.StoreType,
            typeMapping: property.GetRelationalTypeMapping(),
            read,
            write,
            key: false,
            condition: false,
            sensitiveLoggingEnabled: false,
            isNullable: false));
    }

    /// <summary>Defines the two-row protocol independently of generator implementation details.</summary>
    private static string ExpectedTwoRowReadbackSql(
        string tableName,
        string firstKey
    ) => $$"""
           INSERT INTO `{{tableName}}` (`Id`, `Payload`)
           VALUES ({{firstKey}}, @p0_payload),
           (@p1_0, @p1_payload);
           SELECT `Generated`, 0
           FROM `{{tableName}}`
           WHERE ROW_COUNT() = 2 AND `Id` = {{firstKey}}
           UNION ALL
           SELECT `Generated`, 1
           FROM `{{tableName}}`
           WHERE ROW_COUNT() = 2 AND `Id` = @p1_0;

           """;
}
