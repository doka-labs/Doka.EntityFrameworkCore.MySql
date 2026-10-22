using System.Text;
using Microsoft.EntityFrameworkCore.Update;

namespace Doka.EntityFrameworkCore.MySql.Tests;

/// <summary>
/// Covers provider identity-column classification at the update SQL boundary.
/// </summary>
public sealed partial class MySqlUpdateSqlGeneratorTests
{
    /// <summary>
    /// Verifies one generated-key shape determines whether readback can use LAST_INSERT_ID().
    /// </summary>
    /// <param name="propertyKind">The property or shadow-column shape being classified.</param>
    /// <param name="isKey">Whether the modification identifies a key.</param>
    /// <param name="isRead">Whether the modification reads a generated value.</param>
    /// <param name="isWrite">Whether the modification writes a value.</param>
    /// <param name="expected">Whether the complete shape is an identity column.</param>
    [Theory]
    [InlineData("identity", false, true, false, false)]
    [InlineData("identity", true, false, false, false)]
    [InlineData("identity", true, true, true, false)]
    [InlineData("shadow", true, true, false, true)]
    [InlineData("identity", true, true, false, true)]
    [InlineData("never-generated", true, true, false, true)]
    [InlineData("computed", true, true, false, false)]
    public void Identity_classification_requires_the_complete_generated_key_shape(
        string propertyKind,
        bool isKey,
        bool isRead,
        bool isWrite,
        bool expected
    )
    {
        // Arrange
        using var context = new IdentityContext(CreateOptions());
        var entityType = context.Model.FindEntityType(typeof(IdentityEntity))!;
        var property = propertyKind switch
        {
            "identity" => entityType.FindProperty(nameof(IdentityEntity.Id))!,
            "never-generated" => entityType.FindProperty(nameof(IdentityEntity.ExplicitId))!,
            "computed" => entityType.FindProperty(nameof(IdentityEntity.Computed))!,
            "shadow" => null,
            _ => throw new ArgumentOutOfRangeException(nameof(propertyKind)),
        };

        // Act
        var actual = IsIdentity(property, isKey, isRead, isWrite);

        // Assert
        Assert.Equal(expected, actual);
    }

    /// <summary>
    /// Verifies bulk insert without write columns preserves exact multirow sql and mapping.
    /// </summary>
    [Fact]
    public void Bulk_insert_without_write_columns_preserves_exact_multirow_sql_and_mapping()
    {
        // Arrange
        using var context = new IdentityContext(CreateOptions());
        var generator = (MySqlUpdateSqlGenerator)context.GetService<IUpdateSqlGenerator>();
        var builder = new StringBuilder();
        IReadOnlyModificationCommand[] commands =
        [
            CreateCommand("Rows", schema: null),
            CreateCommand("Rows", schema: null),
            CreateCommand("Rows", schema: null),
        ];

        // Act
        var mapping = generator.AppendBulkInsertOperation(
            builder,
            commands,
            commandPosition: 0,
            out var requiresTransaction);

        // Assert
        Assert.Equal(ResultSetMapping.NoResults, mapping);
        Assert.False(requiresTransaction);
        Assert.Equal(
            """
            INSERT INTO `Rows` ()
            VALUES (),
            (),
            ();

            """,
            builder.ToString(),
            ignoreLineEndingDifferences: true);
    }

    /// <summary>
    /// Verifies bulk insert with write columns preserves exact multirow sql and mapping.
    /// </summary>
    [Fact]
    public void Bulk_insert_with_write_columns_preserves_exact_multirow_sql_and_mapping()
    {
        // Arrange
        using var context = new IdentityContext(CreateOptions());
        var generator = (MySqlUpdateSqlGenerator)context.GetService<IUpdateSqlGenerator>();
        var builder = new StringBuilder();
        IReadOnlyModificationCommand[] commands =
        [
            CreateWriteCommand("Rows", "p0_a", "p0_b"),
            CreateWriteCommand("Rows", "p1_a", "p1_b"),
            CreateWriteCommand("Rows", "p2_a", "p2_b"),
        ];

        // Act
        var mapping = generator.AppendBulkInsertOperation(
            builder,
            commands,
            commandPosition: 0,
            out var requiresTransaction);

        // Assert
        Assert.Equal(ResultSetMapping.NoResults, mapping);
        Assert.False(requiresTransaction);
        Assert.Equal(
            """
            INSERT INTO `Rows` (`WriteA`, `WriteB`)
            VALUES (@p0_a, @p0_b),
            (@p1_a, @p1_b),
            (@p2_a, @p2_b);

            """,
            builder.ToString(),
            ignoreLineEndingDifferences: true);
    }

    /// <summary>
    /// Verifies bulk insert with mixed columns reuses filtered buffer and preserves exact sql.
    /// </summary>
    [Fact]
    public void Bulk_insert_with_mixed_columns_reuses_filtered_buffer_and_preserves_exact_sql()
    {
        // Arrange
        using var context = new IdentityContext(CreateOptions());
        var generator = (MySqlUpdateSqlGenerator)context.GetService<IUpdateSqlGenerator>();
        var builder = new StringBuilder();
        IReadOnlyModificationCommand[] commands =
        [
            CreateMixedWriteCommand("Rows", "p0_a", "p0_b"),
            CreateMixedWriteCommand("Rows", "p1_a", "p1_b"),
            CreateMixedWriteCommand("Rows", "p2_a", "p2_b"),
        ];

        // Act
        var mapping = generator.AppendBulkInsertOperation(
            builder,
            commands,
            commandPosition: 0,
            out var requiresTransaction);

        // Assert
        Assert.Equal(ResultSetMapping.NoResults, mapping);
        Assert.False(requiresTransaction);
        Assert.Equal(
            """
            INSERT INTO `Rows` (`WriteA`, `WriteB`)
            VALUES (@p0_a, @p0_b),
            (@p1_a, @p1_b),
            (@p2_a, @p2_b);

            """,
            builder.ToString(),
            ignoreLineEndingDifferences: true);
    }

    /// <summary>
    /// Verifies bulk insert rejects mismatched write column count.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void Bulk_insert_rejects_mismatched_write_column_count(
        int laterWriteColumnCount
    )
    {
        // Arrange
        using var context = new IdentityContext(CreateOptions());
        var generator = (MySqlUpdateSqlGenerator)context.GetService<IUpdateSqlGenerator>();
        var builder = new StringBuilder();
        var laterColumnNames = laterWriteColumnCount == 1
            ? new[] { "WriteA", }
            : ["WriteA", "WriteB", "WriteC"];
        var laterModifications = laterColumnNames
            .Select(
                (
                    columnName,
                    index
                ) => (IColumnModification)CreateWriteModification(columnName, $"p1_{index}"))
            .ToArray();
        IReadOnlyModificationCommand[] commands =
        [
            CreateMixedWriteCommand("Rows", "p0_a", "p0_b"),
            new TestModificationCommand(
                "Rows",
                schema: null,
                laterModifications),
        ];
        Action act = () => generator.AppendBulkInsertOperation(
                builder,
                commands,
                commandPosition: 0,
                out _);

        // Act
        var recordedException = Record.Exception(act);

        // Assert
        var exception = Assert.IsType<InvalidOperationException>(recordedException);
        Assert.Equal("Modification command shapes do not match.", exception.Message);
    }

    /// <summary>
    /// Verifies bulk insert returning preserves columns that are read and written.
    /// </summary>
    [Fact]
    public void Bulk_insert_returning_preserves_columns_that_are_read_and_written()
    {
        // Arrange
        using var context = new IdentityContext(
            CreateOptions(MySqlServerVersion.MariaDb(new Version(11, 8, 0))));

        var generator = (MySqlUpdateSqlGenerator)context.GetService<IUpdateSqlGenerator>();
        var builder = new StringBuilder();
        IReadOnlyModificationCommand[] commands =
        [
            CreateReadWriteCommand("Rows", "p0_value"),
            CreateReadWriteCommand("Rows", "p1_value"),
        ];

        // Act
        var mapping = generator.AppendBulkInsertOperation(
            builder,
            commands,
            commandPosition: 0,
            out var requiresTransaction);

        // Assert
        Assert.Equal(ResultSetMapping.NotLastInResultSet, mapping);
        Assert.False(requiresTransaction);
        Assert.Equal(
            """
            INSERT INTO `Rows` (`Value`)
            VALUES (@p0_value),
            (@p1_value)
            RETURNING `Value`;

            """,
            builder.ToString(),
            ignoreLineEndingDifferences: true);
    }

    /// <summary>
    /// Verifies bulk insert with readback rejects an uninitialized provider profile.
    /// </summary>
    [Fact]
    public void Bulk_insert_with_readback_rejects_an_uninitialized_provider_profile()
    {
        // Arrange
        using var context = new IdentityContext(CreateOptions());
        var generator = new MySqlUpdateSqlGenerator(
            context.GetService<UpdateSqlGeneratorDependencies>(),
            [new MySqlSingletonOptions()]);
        var builder = new StringBuilder();
        IReadOnlyModificationCommand[] commands =
        [
            CreateReadWriteCommand("Rows", "p0_value"),
            CreateReadWriteCommand("Rows", "p1_value"),
        ];
        Action act = () => generator.AppendBulkInsertOperation(
                builder,
                commands,
                commandPosition: 0,
                out _);

        // Act
        var recordedException = Record.Exception(act);

        // Assert
        var exception = Assert.IsType<InvalidOperationException>(recordedException);
        Assert.Equal(
            "The MySQL update SQL generator requires an initialized provider profile.",
            exception.Message);
        Assert.Empty(builder.ToString());
    }

    /// <summary>
    /// Verifies one ordered modification pair preserves the shared INSERT statement shape contract.
    /// </summary>
    /// <param name="scenario">The named column-order, count, table, or schema boundary.</param>
    /// <param name="expected">Whether both commands may share a statement.</param>
    [Theory]
    [InlineData("same-shape", true)]
    [InlineData("reordered-write", false)]
    [InlineData("different-read", false)]
    [InlineData("extra-write", false)]
    [InlineData("extra-write-reversed", false)]
    [InlineData("missing-read", false)]
    [InlineData("missing-read-reversed", false)]
    [InlineData("reordered-read", false)]
    [InlineData("other-table", false)]
    [InlineData("other-schema", false)]
    public void Batch_shape_comparison_preserves_filtered_column_order_contract(
        string scenario,
        bool expected
    )
    {
        // Arrange
        var (first, second) = CreateBatchShapePair(scenario);

        // Act
        var actual = MySqlModificationCommandBatch.CanBeInsertedInSameStatement(first, second);

        // Assert
        Assert.Equal(expected, actual);
    }
}
