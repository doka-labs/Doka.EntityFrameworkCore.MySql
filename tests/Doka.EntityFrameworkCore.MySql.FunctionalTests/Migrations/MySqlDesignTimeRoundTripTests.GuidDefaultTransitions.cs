namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

public sealed partial class MySqlDesignTimeRoundTripTests
{
    /// <summary>Provides each expression-bearing storage format and its distinct generation contracts.</summary>
    public static IEnumerable<object?[]> GuidExpressionGenerationCases()
    {
        foreach (var format in new[] { MySqlGuidFormat.Char36, MySqlGuidFormat.Binary16, MySqlGuidFormat.Uuid })
        {
            foreach (var computed in new[] { false, true })
            {
                yield return [format, computed, null];
                yield return [format, computed, MySqlValueGenerationStrategy.ClientGuid];
                yield return [format, computed, MySqlValueGenerationStrategy.None];
            }
        }
    }

    /// <summary>
    /// Public operation metadata retains generation choices omitted from physical-column comparisons.
    /// </summary>
    [Theory]
    [MemberData(nameof(GuidExpressionGenerationCases))]
    public void Guid_expression_initial_operations_preserve_explicit_generation_metadata(
        MySqlGuidFormat format,
        bool computed,
        MySqlValueGenerationStrategy? strategy
    )
    {
        // Arrange
        using var context = new GuidExpressionDesignContext(
            CreateOptions<GuidExpressionDesignContext>(MySqlServerVersion.MariaDb(new Version(11, 8, 0))),
            format,
            computed ? "computed" : "default",
            target: false,
            strategy);

        // Act
        var generated = GenerateAndCompile(context);
        var sourceModel = context.GetService<IDesignTimeModel>().Model;
        var sourceOperations = CaptureInitialOperations(context, sourceModel);
        var snapshotOperations = CaptureInitialOperations(context, generated.SnapshotModel);
        var designerOperations = CaptureInitialOperations(context, generated.DesignerModel);
        var snapshotRoundTrip = CaptureRoundTrip(context, generated.SnapshotModel);
        var designerRoundTrip = CaptureRoundTrip(context, generated.DesignerModel);

        // Assert
        AssertGuidExpressionOperationMetadata(sourceOperations, format, strategy);
        AssertGuidExpressionOperationMetadata(snapshotOperations, format, strategy);
        AssertGuidExpressionOperationMetadata(designerOperations, format, strategy);
        AssertGuidExpressionPropertyStrategy(sourceModel, strategy);
        AssertGuidExpressionPropertyStrategy(generated.SnapshotModel, strategy);
        AssertGuidExpressionPropertyStrategy(generated.DesignerModel, strategy);
        AssertRoundTripsWithoutOperations(snapshotRoundTrip);
        AssertRoundTripsWithoutOperations(designerRoundTrip);
    }

    /// <summary>Provides real physical changes independently of the runtime-only generation annotation.</summary>
    public static IEnumerable<object?[]> GuidExpressionTransitionCases()
    {
        foreach (var format in new[] { MySqlGuidFormat.Char36, MySqlGuidFormat.Binary16, MySqlGuidFormat.Uuid })
        {
            yield return [format, "default", null];
            yield return [format, "default", MySqlValueGenerationStrategy.ClientGuid];
            yield return [format, "default", MySqlValueGenerationStrategy.None];
        }

        foreach (var change in new[] { "computed", "stored" })
        {
            yield return [MySqlGuidFormat.Char36, change, null];
            yield return [MySqlGuidFormat.Char36, change, MySqlValueGenerationStrategy.ClientGuid];
            yield return [MySqlGuidFormat.Char36, change, MySqlValueGenerationStrategy.None];
        }
    }

    /// <summary>
    /// Suppressing runtime-only comparison metadata must not hide a real expression or storage change.
    /// </summary>
    [Theory]
    [MemberData(nameof(GuidExpressionTransitionCases))]
    public void Guid_expression_changes_preserve_alter_column_and_old_generation_metadata(
        MySqlGuidFormat format,
        string change,
        MySqlValueGenerationStrategy? strategy
    )
    {
        // Arrange
        using var source = new GuidExpressionDesignContext(
            CreateOptions<GuidExpressionDesignContext>(MySqlServerVersion.MariaDb(new Version(11, 8, 0))),
            format,
            change,
            target: false,
            strategy);

        using var target = new GuidExpressionDesignContext(
            CreateOptions<GuidExpressionDesignContext>(MySqlServerVersion.MariaDb(new Version(11, 8, 0))),
            format,
            change,
            target: true,
            strategy);

        // Act
        var generated = GenerateAndCompile(source);
        var snapshotRoundTrip = CaptureRoundTrip(target, generated.SnapshotModel);
        var designerRoundTrip = CaptureRoundTrip(target, generated.DesignerModel);

        // Assert
        AssertGuidExpressionTransition(snapshotRoundTrip, format, change, strategy);
        AssertGuidExpressionTransition(designerRoundTrip, format, change, strategy);
    }

    /// <summary>Older computed snapshots serialized None, which is not a physical schema difference.</summary>
    [Theory]
    [InlineData(MySqlGuidFormat.Char36)]
    [InlineData(MySqlGuidFormat.Binary16)]
    [InlineData(MySqlGuidFormat.Uuid)]
    public void Legacy_none_computed_guid_snapshot_has_no_physical_diff_to_new_implicit_model(
        MySqlGuidFormat format
    )
    {
        // Arrange
        using var legacy = new GuidExpressionDesignContext(
            CreateOptions<GuidExpressionDesignContext>(MySqlServerVersion.MariaDb(new Version(11, 8, 0))),
            format,
            "computed",
            target: false,
            MySqlValueGenerationStrategy.None);

        using var target = new GuidExpressionDesignContext(
            CreateOptions<GuidExpressionDesignContext>(MySqlServerVersion.MariaDb(new Version(11, 8, 0))),
            format,
            "computed",
            target: false,
            strategy: null);

        // Act
        var generated = GenerateAndCompile(legacy);
        var snapshotRoundTrip = CaptureRoundTrip(target, generated.SnapshotModel);
        var designerRoundTrip = CaptureRoundTrip(target, generated.DesignerModel);

        // Assert
        AssertGuidExpressionPropertyStrategy(generated.SnapshotModel, MySqlValueGenerationStrategy.None);
        AssertGuidExpressionPropertyStrategy(generated.DesignerModel, MySqlValueGenerationStrategy.None);
        AssertGuidExpressionPropertyStrategy(target.GetService<IDesignTimeModel>().Model, strategy: null);
        AssertRoundTripsWithoutOperations(snapshotRoundTrip);
        AssertRoundTripsWithoutOperations(designerRoundTrip);
    }

    /// <summary>Combines explicit renames with expression changes for both legacy runtime strategies.</summary>
    public static IEnumerable<object[]> GuidExpressionRenameCases()
    {
        foreach (var rename in new[] { "column", "table", "both" })
        {
            foreach (var change in new[] { "default", "computed" })
            {
                yield return [rename, change, MySqlValueGenerationStrategy.ClientGuid];
                yield return [rename, change, MySqlValueGenerationStrategy.None];
            }
        }
    }

    /// <summary>OldColumn retains source strategy metadata when its physical identity also changes.</summary>
    [Theory]
    [MemberData(nameof(GuidExpressionRenameCases))]
    public void Guid_expression_renames_preserve_old_and_new_operation_metadata(
        string rename,
        string change,
        MySqlValueGenerationStrategy sourceStrategy
    )
    {
        // Arrange
        // Different endpoint strategies expose accidental reads from the target instead of the source.
        var targetStrategy = sourceStrategy == MySqlValueGenerationStrategy.ClientGuid
            ? MySqlValueGenerationStrategy.None
            : MySqlValueGenerationStrategy.ClientGuid;

        using var source = new GuidExpressionDesignContext(
            CreateOptions<GuidExpressionDesignContext>(MySqlServerVersion.MariaDb(new Version(11, 8, 0))),
            MySqlGuidFormat.Char36,
            change,
            target: false,
            sourceStrategy,
            rename);

        using var target = new GuidExpressionDesignContext(
            CreateOptions<GuidExpressionDesignContext>(MySqlServerVersion.MariaDb(new Version(11, 8, 0))),
            MySqlGuidFormat.Char36,
            change,
            target: true,
            targetStrategy,
            rename);

        // Act
        var generated = GenerateAndCompile(source);
        var snapshotRoundTrip = CaptureRoundTrip(target, generated.SnapshotModel);
        var designerRoundTrip = CaptureRoundTrip(target, generated.DesignerModel);

        // Assert
        AssertGuidExpressionRenameTransition(snapshotRoundTrip, rename, change, sourceStrategy, targetStrategy);
        AssertGuidExpressionRenameTransition(designerRoundTrip, rename, change, sourceStrategy, targetStrategy);
    }

    /// <summary>Provides rename-only controls without any physical expression change.</summary>
    public static IEnumerable<object[]> GuidExpressionRenameOnlyCases()
    {
        foreach (var rename in new[] { "column", "table", "both" })
        {
            yield return [rename, MySqlValueGenerationStrategy.ClientGuid];
            yield return [rename, MySqlValueGenerationStrategy.None];
        }
    }

    /// <summary>Metadata restoration must not manufacture an AlterColumn when only an identity changes.</summary>
    [Theory]
    [MemberData(nameof(GuidExpressionRenameOnlyCases))]
    public void Guid_expression_rename_without_expression_change_has_no_alter_column(
        string rename,
        MySqlValueGenerationStrategy strategy
    )
    {
        // Arrange
        using var source = new GuidExpressionDesignContext(
            CreateOptions<GuidExpressionDesignContext>(MySqlServerVersion.MariaDb(new Version(11, 8, 0))),
            MySqlGuidFormat.Char36,
            "default",
            target: false,
            strategy,
            rename,
            changeExpression: false);

        using var target = new GuidExpressionDesignContext(
            CreateOptions<GuidExpressionDesignContext>(MySqlServerVersion.MariaDb(new Version(11, 8, 0))),
            MySqlGuidFormat.Char36,
            "default",
            target: true,
            strategy,
            rename,
            changeExpression: false);

        // Act
        var generated = GenerateAndCompile(source);
        var snapshotRoundTrip = CaptureRoundTrip(target, generated.SnapshotModel);
        var designerRoundTrip = CaptureRoundTrip(target, generated.DesignerModel);

        // Assert
        AssertGuidExpressionRenames(snapshotRoundTrip, rename);
        AssertGuidExpressionRenames(designerRoundTrip, rename);
        Assert.Empty(snapshotRoundTrip.Operations.OfType<AlterColumnOperation>());
        Assert.Empty(designerRoundTrip.Operations.OfType<AlterColumnOperation>());
    }

    /// <summary>Checks the source identity independently of the target identity on altered columns.</summary>
    private static void AssertGuidExpressionRenameTransition(
        RoundTripResult roundTrip,
        string rename,
        string change,
        MySqlValueGenerationStrategy sourceStrategy,
        MySqlValueGenerationStrategy targetStrategy
    )
    {
        AssertGuidExpressionRenames(roundTrip, rename);

        var operation = Assert.Single(roundTrip.Operations.OfType<AlterColumnOperation>());

        Assert.Equal(rename is "column" or "both" ? "RenamedValue" : "Value", operation.Name);
        Assert.Equal(
            rename is "table" or "both" ? "RenamedGuidExpressionDesignRecord" : "GuidExpressionDesignRecord",
            operation.Table);

        Assert.Equal(targetStrategy, operation.GetMySqlMigrationMetadata().ValueGenerationStrategy);
        Assert.Equal(sourceStrategy, operation.OldColumn.GetMySqlMigrationMetadata().ValueGenerationStrategy);

        if (change == "default")
        {
            Assert.NotEqual(operation.OldColumn.DefaultValueSql, operation.DefaultValueSql);
        }
        else
        {
            Assert.NotEqual(operation.OldColumn.ComputedColumnSql, operation.ComputedColumnSql);
        }
    }

    /// <summary>Proves that EF emitted an explicit rename rather than replacing the column or table.</summary>
    private static void AssertGuidExpressionRenames(
        RoundTripResult roundTrip,
        string rename
    )
    {
        Assert.True(roundTrip.HasDifferences);
        Assert.Empty(roundTrip.Operations.OfType<AddColumnOperation>());
        Assert.Empty(roundTrip.Operations.OfType<DropColumnOperation>());
        Assert.Empty(roundTrip.Operations.OfType<CreateTableOperation>());
        Assert.Empty(roundTrip.Operations.OfType<DropTableOperation>());

        if (rename is "column" or "both")
        {
            var column = Assert.Single(roundTrip.Operations.OfType<RenameColumnOperation>());

            Assert.Equal("Value", column.Name);
            Assert.Equal("RenamedValue", column.NewName);
        }

        if (rename is "table" or "both")
        {
            var table = Assert.Single(roundTrip.Operations.OfType<RenameTableOperation>());

            Assert.Equal("GuidExpressionDesignRecord", table.Name);
            Assert.Equal("RenamedGuidExpressionDesignRecord", table.NewName);
        }
    }

    /// <summary>Checks the public column contract rather than its internal annotation representation.</summary>
    private static void AssertGuidExpressionOperationMetadata(
        IReadOnlyList<MigrationOperation> operations,
        MySqlGuidFormat format,
        MySqlValueGenerationStrategy? strategy
    )
    {
        var table = Assert.Single(operations.OfType<CreateTableOperation>());
        var column = Assert.Single(table.Columns, candidate => candidate.Name == "Value");

        Assert.Equal(typeof(Guid), column.ClrType);
        Assert.Equal(format, column.GetMySqlMigrationMetadata().GuidFormat);
        Assert.Equal(strategy, column.GetMySqlMigrationMetadata().ValueGenerationStrategy);
    }

    /// <summary>
    /// Proves that physical normalization does not erase an application's model-level generation choice.
    /// </summary>
    private static void AssertGuidExpressionPropertyStrategy(
        IModel model,
        MySqlValueGenerationStrategy? strategy
    )
    {
        var property = model.FindEntityType("GuidExpressionDesignRecord")!.FindProperty("Value")!;

        Assert.Equal(strategy, property.GetMySqlValueGenerationStrategy());
    }

    /// <summary>Checks both endpoints of one genuine physical column transition.</summary>
    private static void AssertGuidExpressionTransition(
        RoundTripResult roundTrip,
        MySqlGuidFormat format,
        string change,
        MySqlValueGenerationStrategy? strategy
    )
    {
        Assert.True(roundTrip.HasDifferences);

        var operation = Assert.IsType<AlterColumnOperation>(Assert.Single(roundTrip.Operations));

        Assert.Equal("Value", operation.Name);
        Assert.Equal(format, operation.GetMySqlMigrationMetadata().GuidFormat);
        Assert.Equal(strategy, operation.GetMySqlMigrationMetadata().ValueGenerationStrategy);
        Assert.Equal(strategy, operation.OldColumn.GetMySqlMigrationMetadata().ValueGenerationStrategy);

        if (change == "default")
        {
            Assert.NotEqual(operation.OldColumn.DefaultValueSql, operation.DefaultValueSql);
        }
        else if (change == "computed")
        {
            Assert.NotEqual(operation.OldColumn.ComputedColumnSql, operation.ComputedColumnSql);
        }
        else
        {
            Assert.False(operation.OldColumn.IsStored);
            Assert.True(operation.IsStored);
        }
    }

    /// <summary>
    /// Provides one expression-bearing Guid whose runtime strategy and physical shape vary independently.
    /// </summary>
    public sealed class GuidExpressionDesignContext : DbContext
    {
        private readonly MySqlGuidFormat _format;
        private readonly string _change;
        private readonly bool _target;
        private readonly MySqlValueGenerationStrategy? _strategy;
        private readonly string _rename;
        private readonly bool _changeExpression;

        /// <summary>Creates one independently cached endpoint of the physical-column comparison.</summary>
        public GuidExpressionDesignContext(
            DbContextOptions<GuidExpressionDesignContext> options,
            MySqlGuidFormat format,
            string change,
            bool target,
            MySqlValueGenerationStrategy? strategy,
            string rename = "none",
            bool changeExpression = true
        ) : base(options)
        {
            _format = format;
            _change = change;
            _target = target;
            _strategy = strategy;
            _rename = rename;
            _changeExpression = changeExpression;
        }

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.SharedTypeEntity<Dictionary<string, object>>("GuidExpressionDesignRecord", entity =>
            {
                entity.ToTable(
                    _target && _rename is "table" or "both"
                        ? "RenamedGuidExpressionDesignRecord"
                        : "GuidExpressionDesignRecord");

                entity.IndexerProperty<int>("Id").ValueGeneratedNever();

                // Keep key identity stable so rename cases isolate column metadata from constraint renaming.
                entity.HasKey("Id").HasName("PK_GuidExpressionDesignRecord");

                var property = entity.IndexerProperty<Guid>("Value").HasMySqlGuidFormat(_format);

                if (_target && _rename is "column" or "both")
                {
                    property.HasColumnName("RenamedValue");
                }

                if (_change == "default")
                {
                    var function = _target && _changeExpression ? "UUID_v7()" : "UUID()";
                    var expression = _format == MySqlGuidFormat.Binary16
                        ? $"UNHEX(REPLACE({function}, '-', ''))"
                        : function;

                    property.HasDefaultValueSql(expression);
                }
                else
                {
                    var value = _change == "computed" && _target && _changeExpression
                        ? "00112233-4455-6677-8899-aabbccddee00"
                        : "00112233-4455-6677-8899-aabbccddeeff";

                    var expression = _format == MySqlGuidFormat.Binary16
                        ? $"UNHEX(REPLACE('{value}', '-', ''))"
                        : $"'{value}'";

                    property.HasComputedColumnSql(
                        expression,
                        stored: _change == "stored" && _target && _changeExpression);
                }

                if (_strategy is { } strategy)
                {
                    // Legacy snapshots serialize the prior convention's strategy as explicit configuration.
                    property.HasMySqlValueGenerationStrategy(strategy);
                }
            });
        }
    }
}
