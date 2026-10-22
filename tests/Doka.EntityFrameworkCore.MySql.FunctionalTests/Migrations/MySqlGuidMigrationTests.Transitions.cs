namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

/// <summary>Contains the transitions responsibility for MySqlGuidMigrationTests.</summary>
public sealed partial class MySqlGuidMigrationTests
{
    /// <summary>
    /// A store-type transition on both sides of an existing foreign key owns a
    /// symmetric drop/alter/recreate lifecycle in both migration directions.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Char36_transition_orders_foreign_key_around_both_column_alters(
        bool nativeIsSource
    )
    {
        // Arrange
        using var converted = new ConvertedGuidContext(CreateOptions<ConvertedGuidContext>());
        using var native = new NativeChar36GuidContext(CreateOptions<NativeChar36GuidContext>());

        // Act
        var result = ProbeTransition(nativeIsSource ? native : converted, nativeIsSource ? converted : native);

        // Assert
        AssertTransition(
            result,
            nativeIsSource ? "char(36)" : "varchar(36)",
            nativeIsSource ? "varchar(36)" : "char(36)",
            nativeIsSource ? typeof(Guid) : typeof(string),
            nativeIsSource ? typeof(string) : typeof(Guid),
            nativeIsSource ? "AlterColumn<string>" : "AlterColumn<Guid>");
    }

    /// <summary>
    /// Application-owned textual Guid storage and provider-native Binary16
    /// storage cannot be changed in place in either direction.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Application_char36_and_native_binary16_transitions_require_explicit_data_migration(
        bool nativeIsSource
    )
    {
        // Arrange
        using var converted = new ConvertedGuidContext(CreateOptions<ConvertedGuidContext>());
        using var binary = new NativeBinary16GuidContext(CreateOptions<NativeBinary16GuidContext>());

        // Act
        var result = ProbeTransition(
            nativeIsSource ? binary : converted,
            nativeIsSource ? converted : binary);

        // Assert
        AssertApplicationNativeBinaryTransitionRejected(result);
    }

    /// <summary>
    /// Matching binary store types do not prove matching byte order. A custom
    /// Guid byte converter and provider-native Binary16 therefore require an
    /// explicit data migration in both directions.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Application_binary_and_native_binary16_transitions_require_explicit_data_migration(
        bool nativeIsSource
    )
    {
        // Arrange
        using var converted = new ConvertedBinaryGuidContext(CreateOptions<ConvertedBinaryGuidContext>());
        using var binary = new NativeBinary16GuidContext(CreateOptions<NativeBinary16GuidContext>());

        // Act
        var result = ProbeTransition(
            nativeIsSource ? binary : converted,
            nativeIsSource ? converted : binary);

        // Assert
        AssertApplicationNativeBinaryTransitionRejected(result);
    }

    /// <summary>
    /// A native UUID annotation on only one side cannot prove that an
    /// application-owned source or destination has equivalent values.
    /// </summary>
    [Theory]
    [InlineData("varchar(36)", false)]
    [InlineData("varchar(36)", true)]
    [InlineData("uuid", false)]
    [InlineData("uuid", true)]
    [InlineData("binary(16)", false)]
    [InlineData("binary(16)", true)]
    public void Application_owned_and_native_uuid_transitions_require_explicit_data_migration(
        string applicationStoreType,
        bool nativeIsSource
    )
    {
        // Arrange
        using var context = new NativeBinary16GuidContext(CreateOptions<NativeBinary16GuidContext>());
        var applicationClrType = applicationStoreType == "binary(16)" ? typeof(byte[]) : typeof(string);
        var operation = new AlterColumnOperation
        {
            Table = "Records",
            Name = "Id",
            ClrType = nativeIsSource ? applicationClrType : typeof(Guid),
            ColumnType = nativeIsSource ? applicationStoreType : "uuid",
            OldColumn =
            {
                ClrType = nativeIsSource ? typeof(Guid) : applicationClrType,
                ColumnType = nativeIsSource ? "uuid" : applicationStoreType,
            },
        };

        if (nativeIsSource)
        {
            operation.OldColumn[MySqlAnnotationNames.GuidFormat] = MySqlGuidFormat.Uuid;
        }
        else
        {
            operation[MySqlAnnotationNames.GuidFormat] = MySqlGuidFormat.Uuid;
        }

        var generator = context.GetService<IMigrationsSqlGenerator>();

        // Act
        var exception = Assert.Throws<InvalidOperationException>(() => generator.Generate(
            [operation],
            context.GetService<IDesignTimeModel>().Model));

        // Assert
        Assert.Contains("explicit data migration", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Provider-native Guid foreign keys keep Guid operation types, nullability,
    /// and defaults in both nullability directions and storage formats.
    /// </summary>
    [Theory]
    [InlineData(MySqlGuidFormat.Char36, "char(36)", false)]
    [InlineData(MySqlGuidFormat.Char36, "char(36)", true)]
    [InlineData(MySqlGuidFormat.Binary16, "binary(16)", false)]
    [InlineData(MySqlGuidFormat.Binary16, "binary(16)", true)]
    [InlineData(MySqlGuidFormat.Uuid, "uuid", false)]
    [InlineData(MySqlGuidFormat.Uuid, "uuid", true)]
    public void Native_guid_nullability_transitions_use_guid_migration_types_in_both_directions(
        MySqlGuidFormat guidFormat,
        string storeType,
        bool targetIsNullable
    )
    {
        // Arrange
        using var nullable = new RequiredBackfillSourceContext(
            CreateOptions<RequiredBackfillSourceContext>(guidFormat));

        using var required = new RequiredBackfillTargetContext(
            CreateOptions<RequiredBackfillTargetContext>(guidFormat));

        // Act
        var result = ProbeTransition(targetIsNullable ? required : nullable, targetIsNullable ? nullable : required);

        // Assert
        AssertNullabilityTransition(
            result,
            guidFormat,
            storeType,
            isNullable: targetIsNullable,
            oldIsNullable: !targetIsNullable,
            expectedDefault: null);
    }

    /// <summary>
    /// Native Char36 and Binary16 relationships keep Guid operation types in
    /// both storage directions for required and nullable foreign keys.
    /// </summary>
    [Theory]
    [InlineData(MySqlGuidFormat.Char36, "char(36)", MySqlGuidFormat.Binary16, "binary(16)", false)]
    [InlineData(MySqlGuidFormat.Char36, "char(36)", MySqlGuidFormat.Binary16, "binary(16)", true)]
    [InlineData(MySqlGuidFormat.Binary16, "binary(16)", MySqlGuidFormat.Char36, "char(36)", false)]
    [InlineData(MySqlGuidFormat.Binary16, "binary(16)", MySqlGuidFormat.Char36, "char(36)", true)]
    [InlineData(MySqlGuidFormat.Char36, "char(36)", MySqlGuidFormat.Uuid, "uuid", false)]
    [InlineData(MySqlGuidFormat.Char36, "char(36)", MySqlGuidFormat.Uuid, "uuid", true)]
    [InlineData(MySqlGuidFormat.Uuid, "uuid", MySqlGuidFormat.Char36, "char(36)", false)]
    [InlineData(MySqlGuidFormat.Uuid, "uuid", MySqlGuidFormat.Char36, "char(36)", true)]
    [InlineData(MySqlGuidFormat.Binary16, "binary(16)", MySqlGuidFormat.Uuid, "uuid", false)]
    [InlineData(MySqlGuidFormat.Binary16, "binary(16)", MySqlGuidFormat.Uuid, "uuid", true)]
    [InlineData(MySqlGuidFormat.Uuid, "uuid", MySqlGuidFormat.Binary16, "binary(16)", false)]
    [InlineData(MySqlGuidFormat.Uuid, "uuid", MySqlGuidFormat.Binary16, "binary(16)", true)]
    public void Native_guid_storage_format_transitions_use_guid_migration_types(
        MySqlGuidFormat sourceFormat,
        string sourceStoreType,
        MySqlGuidFormat targetFormat,
        string targetStoreType,
        bool containsNullableColumn
    )
    {
        // Arrange
        using DbContext source = containsNullableColumn
            ? new RequiredBackfillSourceContext(CreateOptions<RequiredBackfillSourceContext>(sourceFormat))
            : new RelationshipEvolutionTargetContext(CreateOptions<RelationshipEvolutionTargetContext>(sourceFormat));

        using DbContext target = containsNullableColumn
            ? new RequiredBackfillSourceContext(CreateOptions<RequiredBackfillSourceContext>(targetFormat))
            : new RelationshipEvolutionTargetContext(CreateOptions<RelationshipEvolutionTargetContext>(targetFormat));

        // Act
        var result = ProbeTransition(source, target);

        // Assert
        AssertNativeGuidFormatTransition(
            result,
            sourceFormat,
            sourceStoreType,
            targetFormat,
            targetStoreType,
            containsNullableColumn);
    }

    /// <summary>
    /// A simultaneous native Guid storage and nullability transition keeps both
    /// sides of the migration operation model-typed in every direction.
    /// </summary>
    [Theory]
    [InlineData(MySqlGuidFormat.Char36, "char(36)", MySqlGuidFormat.Binary16, "binary(16)", true)]
    [InlineData(MySqlGuidFormat.Char36, "char(36)", MySqlGuidFormat.Binary16, "binary(16)", false)]
    [InlineData(MySqlGuidFormat.Binary16, "binary(16)", MySqlGuidFormat.Char36, "char(36)", true)]
    [InlineData(MySqlGuidFormat.Binary16, "binary(16)", MySqlGuidFormat.Char36, "char(36)", false)]
    [InlineData(MySqlGuidFormat.Uuid, "uuid", MySqlGuidFormat.Char36, "char(36)", true)]
    [InlineData(MySqlGuidFormat.Uuid, "uuid", MySqlGuidFormat.Char36, "char(36)", false)]
    [InlineData(MySqlGuidFormat.Char36, "char(36)", MySqlGuidFormat.Uuid, "uuid", true)]
    [InlineData(MySqlGuidFormat.Char36, "char(36)", MySqlGuidFormat.Uuid, "uuid", false)]
    public void Native_guid_combined_storage_and_nullability_transitions_use_guid_migration_types(
        MySqlGuidFormat sourceFormat,
        string sourceStoreType,
        MySqlGuidFormat targetFormat,
        string targetStoreType,
        bool sourceIsNullable
    )
    {
        // Arrange
        using DbContext source = sourceIsNullable
            ? new RequiredBackfillSourceContext(CreateOptions<RequiredBackfillSourceContext>(sourceFormat))
            : new RequiredBackfillTargetContext(CreateOptions<RequiredBackfillTargetContext>(sourceFormat));

        using DbContext target = sourceIsNullable
            ? new RequiredBackfillTargetContext(CreateOptions<RequiredBackfillTargetContext>(targetFormat))
            : new RequiredBackfillSourceContext(CreateOptions<RequiredBackfillSourceContext>(targetFormat));

        // Act
        var result = ProbeTransition(source, target);

        // Assert
        AssertNativeGuidFormatAndNullabilityTransition(
            result,
            sourceFormat,
            sourceStoreType,
            targetFormat,
            targetStoreType,
            sourceIsNullable);
    }

    /// <summary>
    /// A store-type change outside a foreign-key shape must not rebuild an unrelated
    /// relationship merely because both operations affect the same table.
    /// </summary>
    [Fact]
    public void Unrelated_store_type_change_does_not_rebuild_foreign_key()
    {
        // Arrange
        using var source = new ShortDescriptionGuidContext(CreateOptions<ShortDescriptionGuidContext>());
        using var target = new LongDescriptionGuidContext(CreateOptions<LongDescriptionGuidContext>());

        // Act
        var operations = target
            .GetService<IMigrationsModelDiffer>()
            .GetDifferences(
                source.GetService<IDesignTimeModel>().Model.GetRelationalModel(),
                target.GetService<IDesignTimeModel>().Model.GetRelationalModel());

        var alterColumn = Assert.Single(operations.OfType<AlterColumnOperation>());

        // Assert
        Assert.Equal(nameof(TextGuidDocumentRevision.Description), alterColumn.Name);
        Assert.Empty(operations.OfType<DropForeignKeyOperation>());
        Assert.Empty(operations.OfType<AddForeignKeyOperation>());
    }
}
