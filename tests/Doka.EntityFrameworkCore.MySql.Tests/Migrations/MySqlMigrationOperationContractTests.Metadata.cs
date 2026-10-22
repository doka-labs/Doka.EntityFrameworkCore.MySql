namespace Doka.EntityFrameworkCore.MySql.Tests;

/// <summary>Verifies typed migration metadata projection and immutable snapshots.</summary>
public sealed partial class MySqlMigrationOperationContractTests
{
    /// <summary>
    /// Verifies public metadata projection rejects a null operation.
    /// </summary>
    [Fact]
    public void Public_metadata_projection_rejects_a_null_operation()
    {
        // Arrange
        MigrationOperation operation = null!;
        Action act = () => operation.GetMySqlMigrationMetadata();

        // Act
        var recordedException = Record.Exception(act);

        // Assert
        Assert.IsType<ArgumentNullException>(recordedException);
    }

    /// <summary>
    /// Verifies public metadata projection reads supported guid formats.
    /// </summary>
    [Theory]
    [InlineData(MySqlGuidFormat.Binary16)]
    [InlineData(MySqlGuidFormat.Char36)]
    public void Public_metadata_projection_reads_supported_guid_formats(
        MySqlGuidFormat format
    )
    {
        // Arrange
        var operation = CreateGuidColumnOperation(format);
        operation[MySqlAnnotationNames.GuidFormat] = format;

        // Act
        var metadata = operation.GetMySqlMigrationMetadata();

        // Assert
        Assert.Equal(format, metadata.GuidFormat);
        Assert.Null(metadata.ValueGenerationStrategy);
        Assert.Null(metadata.IndexPrefixLengths);
    }

    /// <summary>
    /// Verifies public metadata projection reads supported value generation strategies.
    /// </summary>
    [Theory]
    [InlineData(MySqlValueGenerationStrategy.None)]
    [InlineData(MySqlValueGenerationStrategy.AutoIncrement)]
    [InlineData(MySqlValueGenerationStrategy.ClientGuid)]
    [InlineData(MySqlValueGenerationStrategy.HiLo)]
    public void Public_metadata_projection_reads_supported_value_generation_strategies(
        MySqlValueGenerationStrategy strategy
    )
    {
        // Arrange
        var operation = CreateColumnOperation();
        operation[MySqlAnnotationNames.ValueGenerationStrategy] = strategy;

        // Act
        var metadata = operation.GetMySqlMigrationMetadata();

        // Assert
        Assert.Equal(strategy, metadata.ValueGenerationStrategy);
        Assert.Null(metadata.GuidFormat);
        Assert.Null(metadata.IndexPrefixLengths);
    }

    /// <summary>
    /// Verifies public metadata projection preserves future typed value generation values.
    /// </summary>
    [Fact]
    public void Public_metadata_projection_preserves_future_typed_value_generation_values()
    {
        // Arrange
        var futureStrategy = (MySqlValueGenerationStrategy)int.MaxValue;
        var operation = CreateColumnOperation();
        operation[MySqlAnnotationNames.ValueGenerationStrategy] = futureStrategy;

        // Act
        var metadata = operation.GetMySqlMigrationMetadata();

        // Assert
        Assert.Equal(futureStrategy, metadata.ValueGenerationStrategy);
    }

    /// <summary>
    /// Verifies public metadata projection snapshots ordered index prefix lengths.
    /// </summary>
    [Fact]
    public void Public_metadata_projection_snapshots_ordered_index_prefix_lengths()
    {
        // Arrange
        var source = new[] { 24, 0 };
        var operation = new CreateIndexOperation
        {
            Name = "IX_Entries_Name_Code",
            Table = "Entries",
            Columns = ["Name", "Code"],
        };
        operation[MySqlAnnotationNames.IndexPrefixLength] = source;
        var metadata = operation.GetMySqlMigrationMetadata();

        // Act
        source[0] = 1;

        // Assert
        Assert.Equal([24, 0], metadata.IndexPrefixLengths);
    }

    /// <summary>Verifies that an index-prefix snapshot rejects mutation through a collection interface.</summary>
    [Fact]
    public void Public_metadata_index_prefix_snapshot_is_read_only()
    {
        // Arrange
        var operation = new CreateIndexOperation
        {
            Name = "IX_Entries_Name_Code",
            Table = "Entries",
            Columns = ["Name", "Code"],
        };

        operation[MySqlAnnotationNames.IndexPrefixLength] = new[] { 24, 0 };
        var metadata = operation.GetMySqlMigrationMetadata();

        // Act
        var recordedException = Record.Exception(() => ((IList<int>)metadata.IndexPrefixLengths!)[0] = 1);

        // Assert
        Assert.IsType<NotSupportedException>(recordedException);
    }

    /// <summary>
    /// Verifies public metadata projection reads a single index prefix length.
    /// </summary>
    [Fact]
    public void Public_metadata_projection_reads_a_single_index_prefix_length()
    {
        // Arrange
        var operation = new CreateIndexOperation
        {
            Name = "IX_Entries_Name",
            Table = "Entries",
            Columns = ["Name"],
        };

        operation[MySqlAnnotationNames.IndexPrefixLength] = new[] { 16 };

        // Act
        var metadata = operation.GetMySqlMigrationMetadata();

        // Assert
        Assert.Equal([16], metadata.IndexPrefixLengths);
    }

    /// <summary>
    /// Verifies public metadata projection distinguishes absent metadata from explicit zero values.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Public_metadata_projection_distinguishes_absent_metadata_from_explicit_zero_values(
        bool useExplicitValue
    )
    {
        // Arrange
        var operation = CreateColumnOperation();

        if (useExplicitValue)
        {
            operation[MySqlAnnotationNames.ValueGenerationStrategy] = MySqlValueGenerationStrategy.None;
        }

        // Act
        var metadata = operation.GetMySqlMigrationMetadata();

        // Assert
        Assert.Null(metadata.GuidFormat);
        Assert.Null(metadata.IndexPrefixLengths);
        Assert.Equal(useExplicitValue ? MySqlValueGenerationStrategy.None : (MySqlValueGenerationStrategy?)null,
            metadata.ValueGenerationStrategy);
    }

    /// <summary>
    /// Verifies public metadata projection ignores unrelated annotations.
    /// </summary>
    [Fact]
    public void Public_metadata_projection_ignores_unrelated_annotations()
    {
        // Arrange
        var operation = CreateColumnOperation();
        operation["Example:FutureMetadata"] = "opaque";

        // Act
        var metadata = operation.GetMySqlMigrationMetadata();

        // Assert
        Assert.Null(metadata.GuidFormat);
        Assert.Null(metadata.ValueGenerationStrategy);
        Assert.Null(metadata.IndexPrefixLengths);
    }

    /// <summary>
    /// Verifies public metadata projection rejects wrong annotation value types.
    /// </summary>
    [Theory]
    [InlineData(MySqlAnnotationNames.GuidFormat)]
    [InlineData(MySqlAnnotationNames.ValueGenerationStrategy)]
    [InlineData(MySqlAnnotationNames.IndexPrefixLength)]
    public void Public_metadata_projection_rejects_wrong_annotation_value_types(
        string annotationName
    )
    {
        // Arrange
        MigrationOperation operation = annotationName == MySqlAnnotationNames.IndexPrefixLength
            ? new CreateIndexOperation
            {
                Name = "IX_Entries_Name",
                Table = "Entries",
                Columns = ["Name"],
            }
            : CreateColumnOperation();
        operation[annotationName] = "invalid";
        Action act = () => operation.GetMySqlMigrationMetadata();

        // Act
        var recordedException = Record.Exception(act);

        // Assert
        Assert.IsType<InvalidOperationException>(recordedException);
    }

    /// <summary>
    /// Verifies public metadata projection rejects unknown guid format values.
    /// </summary>
    [Fact]
    public void Public_metadata_projection_rejects_unknown_guid_format_values()
    {
        // Arrange
        var operation = CreateColumnOperation();
        operation[MySqlAnnotationNames.GuidFormat] = (MySqlGuidFormat)int.MaxValue;
        Action act = () => operation.GetMySqlMigrationMetadata();

        // Act
        var recordedException = Record.Exception(act);

        // Assert
        Assert.IsType<InvalidOperationException>(recordedException);
    }

    /// <summary>
    /// Verifies public metadata projection rejects guid format conflicts.
    /// </summary>
    [Theory]
    [InlineData(MySqlGuidFormat.Binary16, "char(36)", typeof(Guid))]
    [InlineData(MySqlGuidFormat.Char36, "binary(16)", typeof(Guid))]
    [InlineData(MySqlGuidFormat.Char36, "char(36)", typeof(string))]
    public void Public_metadata_projection_rejects_guid_format_conflicts(
        MySqlGuidFormat format,
        string columnType,
        Type clrType
    )
    {
        // Arrange
        var operation = CreateColumnOperation();
        operation.ClrType = clrType;
        operation.ColumnType = columnType;
        operation[MySqlAnnotationNames.GuidFormat] = format;
        Action act = () => operation.GetMySqlMigrationMetadata();

        // Act
        var recordedException = Record.Exception(act);

        // Assert
        Assert.IsType<InvalidOperationException>(recordedException);
    }

    /// <summary>
    /// Verifies public metadata projection accepts deferred or normalized guid store types.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" BINARY(16) ")]
    public void Public_metadata_projection_accepts_deferred_or_normalized_guid_store_types(
        string? columnType
    )
    {
        // Arrange
        var operation = CreateGuidColumnOperation(MySqlGuidFormat.Binary16);
        operation.ColumnType = columnType!;
        operation[MySqlAnnotationNames.GuidFormat] = MySqlGuidFormat.Binary16;

        // Act
        var metadata = operation.GetMySqlMigrationMetadata();

        // Assert
        Assert.Equal(MySqlGuidFormat.Binary16, metadata.GuidFormat);
    }

    /// <summary>
    /// Verifies public metadata projection accepts required and nullable guid clr types.
    /// </summary>
    [Theory]
    [InlineData(typeof(Guid))]
    [InlineData(typeof(Guid?))]
    public void Public_metadata_projection_accepts_required_and_nullable_guid_clr_types(
        Type clrType
    )
    {
        // Arrange
        var operation = CreateGuidColumnOperation(MySqlGuidFormat.Char36);
        operation.ClrType = clrType;
        operation[MySqlAnnotationNames.GuidFormat] = MySqlGuidFormat.Char36;

        // Act
        var metadata = operation.GetMySqlMigrationMetadata();

        // Assert
        Assert.Equal(MySqlGuidFormat.Char36, metadata.GuidFormat);
    }

    /// <summary>
    /// Verifies public metadata projection rejects metadata on incompatible operation shapes.
    /// </summary>
    [Theory]
    [InlineData(MySqlAnnotationNames.GuidFormat, MySqlGuidFormat.Char36)]
    [InlineData(MySqlAnnotationNames.ValueGenerationStrategy, MySqlValueGenerationStrategy.None)]
    [InlineData(MySqlAnnotationNames.IndexPrefixLength, null)]
    public void Public_metadata_projection_rejects_metadata_on_incompatible_operation_shapes(
        string annotationName,
        object? annotationValue
    )
    {
        // Arrange
        var operation = new SqlOperation { Sql = "SELECT 1;" };
        operation[annotationName] = annotationValue ?? new[] { 0 };
        Action act = () => operation.GetMySqlMigrationMetadata();

        // Act
        var recordedException = Record.Exception(act);

        // Assert
        Assert.IsType<InvalidOperationException>(recordedException);
    }

    /// <summary>
    /// Verifies public metadata projection rejects negative index prefix lengths.
    /// </summary>
    [Fact]
    public void Public_metadata_projection_rejects_negative_index_prefix_lengths()
    {
        // Arrange
        var operation = new CreateIndexOperation
        {
            Name = "IX_Entries_Name",
            Table = "Entries",
            Columns = ["Name"],
        };
        operation[MySqlAnnotationNames.IndexPrefixLength] = new[] { -1 };
        Action act = () => operation.GetMySqlMigrationMetadata();

        // Act
        var recordedException = Record.Exception(act);

        // Assert
        Assert.IsType<InvalidOperationException>(recordedException);
    }

    /// <summary>
    /// Verifies public metadata projection rejects index prefix cardinality mismatch.
    /// </summary>
    [Fact]
    public void Public_metadata_projection_rejects_index_prefix_cardinality_mismatch()
    {
        // Arrange
        var operation = new CreateIndexOperation
        {
            Name = "IX_Entries_Name_Code",
            Table = "Entries",
            Columns = ["Name", "Code"],
        };
        operation[MySqlAnnotationNames.IndexPrefixLength] = new[] { 16 };
        Action act = () => operation.GetMySqlMigrationMetadata();

        // Act
        var recordedException = Record.Exception(act);

        // Assert
        Assert.IsType<InvalidOperationException>(recordedException);
    }

    /// <summary>
    /// Verifies public metadata projection rejects missing index columns.
    /// </summary>
    [Fact]
    public void Public_metadata_projection_rejects_missing_index_columns()
    {
        // Arrange
        var operation = new CreateIndexOperation
        {
            Name = "IX_Entries_Name",
            Table = "Entries",
            Columns = null!,
        };
        operation[MySqlAnnotationNames.IndexPrefixLength] = new[] { 16 };
        Action act = () => operation.GetMySqlMigrationMetadata();

        // Act
        var recordedException = Record.Exception(act);

        // Assert
        Assert.IsType<InvalidOperationException>(recordedException);
    }

    /// <summary>
    /// Verifies operation context exposes the same typed metadata snapshot.
    /// </summary>
    [Fact]
    public void Operation_context_exposes_the_same_typed_metadata_snapshot()
    {
        // Arrange
        var operation = CreateColumnOperation();
        operation[MySqlAnnotationNames.GuidFormat] = MySqlGuidFormat.Char36;
        operation[MySqlAnnotationNames.ValueGenerationStrategy] = MySqlValueGenerationStrategy.ClientGuid;
        var serverVersion = MySqlServerVersion.MySql(new Version(8, 4, 11));
        var context = new MySqlMigrationOperationContext(
            operation,
            model: null,
            MigrationsSqlGenerationOptions.Default,
            serverVersion,
            new MySqlMigrationFeatureSet(serverVersion.Profile),
            operationOrdinal: 0,
            "tests.metadata",
            _ => []);

        // Act
        operation[MySqlAnnotationNames.GuidFormat] = MySqlGuidFormat.Binary16;

        // Assert
        Assert.Equal(MySqlGuidFormat.Char36, context.Metadata.GuidFormat);
        Assert.Equal(MySqlValueGenerationStrategy.ClientGuid, context.Metadata.ValueGenerationStrategy);
    }
}
