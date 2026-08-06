using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Doka.EntityFrameworkCore.MySql.Tests;

/// <summary>
/// Verifies temporal metadata defaults and model-validation boundaries.
/// </summary>
public sealed class MySqlTemporalMetadataTests
{
    /// <summary>
    /// MySQL emulation receives a separate history table and UTC-safe period columns.
    /// </summary>
    [Fact]
    public void MySql_temporal_mapping_uses_emulation_defaults()
    {
        using var context = CreateContext<MySqlDefaultConfiguration>(s_mySql84);
        var entityType = context.Model.FindEntityType(typeof(TemporalRecord))!;

        Assert.True(entityType.IsMySqlTemporal());
        Assert.Equal("TemporalRecordsHistory", entityType.GetMySqlTemporalHistoryTableName());
        AssertPeriodProperty(entityType, "PeriodStart", "datetime(6)");
        AssertPeriodProperty(entityType, "PeriodEnd", "datetime(6)");
    }

    /// <summary>
    /// Native MariaDB versioning keeps history in the source table.
    /// </summary>
    [Fact]
    public void MariaDb_temporal_mapping_uses_native_defaults()
    {
        using var context = CreateContext<MariaDbDefaultConfiguration>(s_mariaDb114);
        var entityType = context.Model.FindEntityType(typeof(TemporalRecord))!;

        Assert.True(entityType.IsMySqlTemporal());
        Assert.Null(entityType.GetMySqlTemporalHistoryTableName());
        AssertPeriodProperty(entityType, "PeriodStart", "timestamp(6)");
        AssertPeriodProperty(entityType, "PeriodEnd", "timestamp(6)");
    }

    /// <summary>
    /// Explicit emulation names survive convention processing unchanged.
    /// </summary>
    [Fact]
    public void MySql_temporal_mapping_preserves_explicit_names()
    {
        using var context = CreateContext<CustomNamesConfiguration>(s_mySql84);
        var entityType = context.Model.FindEntityType(typeof(TemporalRecord))!;

        Assert.Equal("TemporalRecordAudit", entityType.GetMySqlTemporalHistoryTableName());
        Assert.Equal("audit", entityType.GetMySqlTemporalHistoryTableSchema());
        Assert.Equal("ValidFrom", entityType.GetMySqlTemporalPeriodStartPropertyName());
        Assert.Equal("ValidUntil", entityType.GetMySqlTemporalPeriodEndPropertyName());
        AssertPeriodProperty(entityType, "ValidFrom", "datetime(6)");
        AssertPeriodProperty(entityType, "ValidUntil", "datetime(6)");
    }

    /// <summary>
    /// Long table names receive deterministic, collision-resistant history identifiers.
    /// </summary>
    [Fact]
    public void Default_history_table_name_respects_engine_identifier_limit()
    {
        const string tableName =
            "TemporalRecordsWhoseNameIsDeliberatelyLongEnoughToRequireStableTruncation";

        var first = MySqlTemporalMetadata.CreateDefaultHistoryTableName(tableName);
        var second = MySqlTemporalMetadata.CreateDefaultHistoryTableName(tableName);

        Assert.Equal(first, second);
        Assert.Equal(MySqlConventionSetBuilder.MaxIdentifierLength, first.Length);
        Assert.Matches("^[A-Za-z0-9]+_[0-9a-f]{16}$", first);
    }

    /// <summary>
    /// The provider-owned trigger marker preserves every identifier without relying
    /// on delimiters that may legally occur in schema or column names.
    /// </summary>
    [Fact]
    public void Emulation_marker_round_trips_temporal_identifiers()
    {
        var encodedMarker = MySqlTemporalMetadata.CreateEmulationMarker(
            "audit:schema",
            "Order History",
            "Valid:From",
            "Valid Until");

        var recognized = MySqlTemporalMetadata.TryParseEmulationMarker(
            $"BEGIN /* {encodedMarker} */ SET @ignored = 1; END",
            out var marker);

        Assert.True(recognized);
        Assert.NotNull(marker);
        Assert.Equal("audit:schema", marker.HistorySchema);
        Assert.Equal("Order History", marker.HistoryTable);
        Assert.Equal("Valid:From", marker.PeriodStartColumn);
        Assert.Equal("Valid Until", marker.PeriodEndColumn);
    }

    /// <summary>
    /// Similar comments, malformed hex and ambiguous duplicate markers must not
    /// turn user-owned triggers into provider-managed temporal infrastructure.
    /// </summary>
    [Theory]
    [InlineData("BEGIN /* doka-temporal-v2::41:42:43 */ END")]
    [InlineData("BEGIN /* doka-temporal-v1::GG:42:43 */ END")]
    [InlineData("BEGIN /* doka-temporal-v1::41:42 */ END")]
    [InlineData("BEGIN /* doka-temporal-v1::41:42:42 */ END")]
    [InlineData("BEGIN /* doka-temporal-v1::41:42:43 */ /* doka-temporal-v1::41:42:43 */ END")]
    public void Invalid_emulation_markers_are_not_recognized(
        string actionStatement
    )
    {
        var recognized = MySqlTemporalMetadata.TryParseEmulationMarker(
            actionStatement,
            out var marker);

        Assert.False(recognized);
        Assert.Null(marker);
    }

    /// <summary>
    /// MariaDB retains the provider-independent history metadata while native
    /// system versioning keeps the physical history in the source table.
    /// </summary>
    [Fact]
    public void MariaDb_native_mapping_retains_history_table_configuration_as_metadata()
    {
        using var context = CreateContext<NativeHistoryTableConfiguration>(s_mariaDb114);

        var entityType = context.Model.FindEntityType(typeof(TemporalRecord));

        Assert.NotNull(entityType);
        Assert.Equal("TemporalRecordAudit", entityType.GetMySqlTemporalHistoryTableName());
    }

    /// <summary>
    /// Period boundaries must map to different model properties.
    /// </summary>
    [Fact]
    public void Temporal_mapping_rejects_identical_period_properties()
    {
        using var context = CreateContext<IdenticalPeriodsConfiguration>(s_mySql84);

        var exception = Assert.Throws<InvalidOperationException>(() => _ = context.Model);

        Assert.Contains("distinct temporal period properties", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Period boundaries must be non-nullable DateTime properties.
    /// </summary>
    [Fact]
    public void Temporal_mapping_rejects_invalid_period_property_type()
    {
        using var context = CreateContext<InvalidPeriodTypeConfiguration>(s_mySql84);

        var exception = Assert.Throws<InvalidOperationException>(() => _ = context.Model);

        Assert.Contains("must be a non-nullable DateTime property", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// MariaDB releases before native system versioning fail at model validation.
    /// </summary>
    [Fact]
    public void Temporal_mapping_rejects_engine_without_temporal_support()
    {
        using var context = CreateContext<UnsupportedEngineConfiguration>(s_mariaDb1033);

        var exception = Assert.Throws<InvalidOperationException>(() => _ = context.Model);

        Assert.Contains("cannot supply temporal tables", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// MySQL emulation requires transactional trigger and history writes.
    /// </summary>
    [Fact]
    public void MySql_temporal_mapping_rejects_non_transactional_storage_engine()
    {
        using var context = CreateContext<MyIsamConfiguration>(s_mySql84);

        var exception = Assert.Throws<InvalidOperationException>(() => _ = context.Model);

        Assert.Contains("must use InnoDB", exception.Message, StringComparison.Ordinal);
        Assert.Contains("one atomic transaction", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Database cascades are rejected because MySQL does not invoke temporal triggers for them.
    /// </summary>
    [Fact]
    public void MySql_temporal_mapping_rejects_database_cascade()
    {
        using var context = CreateContext<CascadeConfiguration>(s_mySql84);

        var exception = Assert.Throws<InvalidOperationException>(() => _ = context.Model);

        Assert.Contains("cascaded foreign-key actions do not activate triggers", exception.Message,
            StringComparison.Ordinal);
        Assert.Contains("without a corresponding history record", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// MariaDB native system versioning rejects generated entity columns.
    /// </summary>
    [Fact]
    public void MariaDb_temporal_mapping_rejects_generated_columns()
    {
        using var context = CreateContext<NativeGeneratedColumnConfiguration>(s_mariaDb114);

        var exception = Assert.Throws<InvalidOperationException>(() => _ = context.Model);

        Assert.Contains("generated columns cannot be system-versioned", exception.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(TemporalRecord.NameLength), exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Disabling temporal mapping does not create hidden period properties.
    /// </summary>
    [Fact]
    public void Disabled_temporal_mapping_does_not_add_temporal_metadata()
    {
        using var context = CreateContext<DisabledConfiguration>(s_mySql84);
        var entityType = context.Model.FindEntityType(typeof(TemporalRecord))!;

        Assert.False(entityType.IsMySqlTemporal());
        Assert.Null(entityType.GetMySqlTemporalHistoryTableName());
        Assert.Null(entityType.GetMySqlTemporalPeriodStartPropertyName());
        Assert.Null(entityType.GetMySqlTemporalPeriodEndPropertyName());
        Assert.Null(entityType.FindProperty("PeriodStart"));
        Assert.Null(entityType.FindProperty("PeriodEnd"));
    }

    private static readonly MySqlServerVersion s_mySql84 =
        MySqlServerVersion.MySql(new Version(8, 4, 0));

    private static readonly MySqlServerVersion s_mariaDb114 =
        MySqlServerVersion.MariaDb(new Version(11, 4, 0));

    private static readonly MySqlServerVersion s_mariaDb1033 =
        MySqlServerVersion.MariaDb(
            new Version(10, 3, 3),
            MySqlServerVersionCompatibilityMode.AllowUnsupported);

    private static TemporalContext<TConfiguration> CreateContext<TConfiguration>(
        MySqlServerVersion serverVersion
    )
        where TConfiguration : ITemporalConfiguration, new()
    {
        var options = new DbContextOptionsBuilder<TemporalContext<TConfiguration>>()
            .UseMySql(
                "Server=localhost;Database=doka;User ID=root;Password=password;",
                serverVersion)
            .Options;

        return new TemporalContext<TConfiguration>(options);
    }

    private static void AssertPeriodProperty(
        IReadOnlyEntityType entityType,
        string propertyName,
        string storeType
    )
    {
        var property = entityType.FindProperty(propertyName)!;

        Assert.Equal(typeof(DateTime), property.ClrType);
        Assert.False(property.IsNullable);
        Assert.Equal(storeType, property.GetColumnType());
        Assert.Equal(ValueGenerated.OnAddOrUpdate, property.ValueGenerated);
    }

    private interface ITemporalConfiguration
    {
        void Configure(EntityTypeBuilder<TemporalRecord> entity);
    }

    private sealed class MySqlDefaultConfiguration : ITemporalConfiguration
    {
        public void Configure(
            EntityTypeBuilder<TemporalRecord> entity
        ) => entity.ToTable("TemporalRecords", table => table.IsTemporal());
    }

    private sealed class MariaDbDefaultConfiguration : ITemporalConfiguration
    {
        public void Configure(
            EntityTypeBuilder<TemporalRecord> entity
        ) => entity.ToTable("TemporalRecords", table => table.IsTemporal());
    }

    private sealed class CustomNamesConfiguration : ITemporalConfiguration
    {
        public void Configure(
            EntityTypeBuilder<TemporalRecord> entity
        ) => entity.ToTable(
            "TemporalRecords",
            table => table.IsTemporal(
                temporal =>
                {
                    temporal.UseHistoryTable("TemporalRecordAudit", "audit");
                    temporal.HasPeriodStart("ValidFrom");
                    temporal.HasPeriodEnd("ValidUntil");
                }));
    }

    private sealed class NativeHistoryTableConfiguration : ITemporalConfiguration
    {
        public void Configure(
            EntityTypeBuilder<TemporalRecord> entity
        ) => entity.ToTable(
            "TemporalRecords",
            table => table.IsTemporal(
                temporal => temporal.UseHistoryTable("TemporalRecordAudit")));
    }

    private sealed class IdenticalPeriodsConfiguration : ITemporalConfiguration
    {
        public void Configure(
            EntityTypeBuilder<TemporalRecord> entity
        ) => entity.ToTable(
            "TemporalRecords",
            table => table.IsTemporal(
                temporal =>
                {
                    temporal.HasPeriodStart("ValidAt");
                    temporal.HasPeriodEnd("ValidAt");
                }));
    }

    private sealed class InvalidPeriodTypeConfiguration : ITemporalConfiguration
    {
        public void Configure(
            EntityTypeBuilder<TemporalRecord> entity
        )
        {
            entity.Property<string>("ValidFrom");
            entity.ToTable(
                "TemporalRecords",
                table => table.IsTemporal(
                    temporal => temporal.HasPeriodStart("ValidFrom")));
        }
    }

    private sealed class UnsupportedEngineConfiguration : ITemporalConfiguration
    {
        public void Configure(
            EntityTypeBuilder<TemporalRecord> entity
        ) => entity.ToTable("TemporalRecords", table => table.IsTemporal());
    }

    private sealed class MyIsamConfiguration : ITemporalConfiguration
    {
        public void Configure(
            EntityTypeBuilder<TemporalRecord> entity
        )
        {
            entity.UseStorageEngine("MyISAM");
            entity.ToTable("TemporalRecords", table => table.IsTemporal());
        }
    }

    private sealed class CascadeConfiguration : ITemporalConfiguration
    {
        public void Configure(
            EntityTypeBuilder<TemporalRecord> entity
        )
        {
            entity.HasOne(record => record.Parent)
                .WithMany()
                .HasForeignKey(record => record.ParentId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.ToTable("TemporalRecords", table => table.IsTemporal());
        }
    }

    private sealed class NativeGeneratedColumnConfiguration : ITemporalConfiguration
    {
        public void Configure(
            EntityTypeBuilder<TemporalRecord> entity
        )
        {
            entity.Property(record => record.NameLength)
                .HasComputedColumnSql("CHAR_LENGTH(`Name`)", stored: true);
            entity.ToTable("TemporalRecords", table => table.IsTemporal());
        }
    }

    private sealed class DisabledConfiguration : ITemporalConfiguration
    {
        public void Configure(
            EntityTypeBuilder<TemporalRecord> entity
        ) => entity.ToTable("TemporalRecords", table => table.IsTemporal(false));
    }

    private sealed class TemporalContext<TConfiguration> : DbContext
        where TConfiguration : ITemporalConfiguration, new()
    {
        public TemporalContext(
            DbContextOptions<TemporalContext<TConfiguration>> options
        ) : base(options) { }

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            var entity = modelBuilder.Entity<TemporalRecord>();
            entity.HasKey(record => record.Id);
            new TConfiguration().Configure(entity);
        }
    }

    private sealed class TemporalRecord
    {
        public int Id { get; set; }

        public string Name { get; set; } = null!;

        public int NameLength { get; set; }

        public int? ParentId { get; set; }

        public TemporalRecord? Parent { get; set; }
    }
}
