using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Doka.EntityFrameworkCore.MySql.Tests;

/// <summary>
/// Verifies the typed application-time and bitemporal metadata contracts.
/// </summary>
public sealed class MySqlApplicationTimeMetadataTests
{
    /// <summary>
    /// The typed table API preserves period identities, explicit columns and the
    /// primary-key <c>WITHOUT OVERLAPS</c> contract in the finalized model.
    /// </summary>
    [Fact]
    public void Application_time_mapping_preserves_typed_configuration()
    {
        using var context = CreateContext<ApplicationTimeConfiguration>(s_mariaDb114);
        var entityType = context.Model.FindEntityType(typeof(ApplicationTimeRecord))!;
        var table = StoreObjectIdentifier.Table("BusinessRecords", null);

        Assert.True(entityType.IsMySqlApplicationTime());
        Assert.Equal("BusinessValidity", entityType.GetMySqlApplicationTimePeriodName());
        Assert.Equal(nameof(ApplicationTimeRecord.ValidFrom),
            entityType.GetMySqlApplicationTimePeriodStartPropertyName());
        Assert.Equal(nameof(ApplicationTimeRecord.ValidTo),
            entityType.GetMySqlApplicationTimePeriodEndPropertyName());
        Assert.Equal("BusinessValidFrom",
            entityType.FindProperty(nameof(ApplicationTimeRecord.ValidFrom))!.GetColumnName(table));
        Assert.Equal("BusinessValidTo",
            entityType.FindProperty(nameof(ApplicationTimeRecord.ValidTo))!.GetColumnName(table));
        Assert.Equal(ValueGenerated.Never,
            entityType.FindProperty(nameof(ApplicationTimeRecord.ValidFrom))!.ValueGenerated);
        Assert.Equal(ValueGenerated.Never,
            entityType.FindProperty(nameof(ApplicationTimeRecord.ValidTo))!.ValueGenerated);
        Assert.True(
            entityType.FindPrimaryKey()!
                .FindAnnotation(MySqlAnnotationNames.ApplicationTimeKeyWithoutOverlaps)
                ?.Value as bool?);
    }

    /// <summary>
    /// Bitemporal configuration combines two independent temporal dimensions on
    /// MariaDB without conflating their period metadata.
    /// </summary>
    [Fact]
    public void Bitemporal_mapping_preserves_both_period_contracts()
    {
        using var context = CreateContext<BitemporalConfiguration>(s_mariaDb114);
        var entityType = context.Model.FindEntityType(typeof(ApplicationTimeRecord))!;

        Assert.True(entityType.IsMySqlTemporal());
        Assert.Equal("SystemFrom", entityType.GetMySqlTemporalPeriodStartPropertyName());
        Assert.Equal("SystemTo", entityType.GetMySqlTemporalPeriodEndPropertyName());
        Assert.True(entityType.IsMySqlApplicationTime());
        Assert.Equal("BusinessValidity", entityType.GetMySqlApplicationTimePeriodName());
        Assert.Equal(nameof(ApplicationTimeRecord.ValidFrom),
            entityType.GetMySqlApplicationTimePeriodStartPropertyName());
        Assert.Equal(nameof(ApplicationTimeRecord.ValidTo),
            entityType.GetMySqlApplicationTimePeriodEndPropertyName());
    }

    /// <summary>
    /// Explicit typed period properties replace the conventional endpoints without
    /// leaving unreferenced convention properties in the finalized model.
    /// </summary>
    [Fact]
    public void Typed_action_with_explicit_properties_does_not_create_default_properties()
    {
        using var context = CreateCustomContext<TypedCustomEndpointConfiguration>();
        var entityType = context.Model.FindEntityType(typeof(CustomApplicationTimeRecord))!;

        Assert.Equal(
            nameof(CustomApplicationTimeRecord.BusinessValidFrom),
            entityType.GetMySqlApplicationTimePeriodStartPropertyName());
        Assert.Equal(
            nameof(CustomApplicationTimeRecord.BusinessValidTo),
            entityType.GetMySqlApplicationTimePeriodEndPropertyName());
        Assert.Null(entityType.FindProperty(MySqlApplicationTimeMetadata.DefaultPeriodStartPropertyName));
        Assert.Null(entityType.FindProperty(MySqlApplicationTimeMetadata.DefaultPeriodEndPropertyName));
    }

    /// <summary>
    /// The non-generic action overload follows the same lazy-default contract as the
    /// typed overload when callers supply custom property names.
    /// </summary>
    [Fact]
    public void String_action_with_explicit_properties_does_not_create_default_properties()
    {
        using var context = new StringApplicationTimeContext(
            CreateOptions<StringApplicationTimeContext>(s_mariaDb114));

        var entityType = context.Model.FindEntityType(typeof(CustomApplicationTimeRecord))!;

        Assert.Equal(
            nameof(CustomApplicationTimeRecord.BusinessValidFrom),
            entityType.GetMySqlApplicationTimePeriodStartPropertyName());
        Assert.Equal(
            nameof(CustomApplicationTimeRecord.BusinessValidTo),
            entityType.GetMySqlApplicationTimePeriodEndPropertyName());
        Assert.Null(entityType.FindProperty(MySqlApplicationTimeMetadata.DefaultPeriodStartPropertyName));
        Assert.Null(entityType.FindProperty(MySqlApplicationTimeMetadata.DefaultPeriodEndPropertyName));
    }

    /// <summary>
    /// An action that replaces one endpoint receives only the missing conventional
    /// endpoint after the caller's configuration has completed.
    /// </summary>
    [Fact]
    public void Action_with_one_explicit_endpoint_adds_only_the_missing_default()
    {
        using var context = CreateCustomContext<SingleCustomEndpointConfiguration>();
        var entityType = context.Model.FindEntityType(typeof(CustomApplicationTimeRecord))!;

        Assert.Equal(
            nameof(CustomApplicationTimeRecord.BusinessValidFrom),
            entityType.GetMySqlApplicationTimePeriodStartPropertyName());
        Assert.Equal(
            MySqlApplicationTimeMetadata.DefaultPeriodEndPropertyName,
            entityType.GetMySqlApplicationTimePeriodEndPropertyName());
        Assert.Null(entityType.FindProperty(MySqlApplicationTimeMetadata.DefaultPeriodStartPropertyName));
        Assert.True(entityType.FindProperty(MySqlApplicationTimeMetadata.DefaultPeriodEndPropertyName)!.IsShadowProperty());
    }

    /// <summary>
    /// The parameterless overload retains the documented conventional endpoint pair.
    /// </summary>
    [Fact]
    public void Parameterless_overload_creates_both_default_properties()
    {
        using var context = CreateCustomContext<DefaultEndpointConfiguration>();
        var entityType = context.Model.FindEntityType(typeof(CustomApplicationTimeRecord))!;

        Assert.Equal(
            MySqlApplicationTimeMetadata.DefaultPeriodStartPropertyName,
            entityType.GetMySqlApplicationTimePeriodStartPropertyName());
        Assert.Equal(
            MySqlApplicationTimeMetadata.DefaultPeriodEndPropertyName,
            entityType.GetMySqlApplicationTimePeriodEndPropertyName());
        Assert.True(entityType.FindProperty(MySqlApplicationTimeMetadata.DefaultPeriodStartPropertyName)!.IsShadowProperty());
        Assert.True(entityType.FindProperty(MySqlApplicationTimeMetadata.DefaultPeriodEndPropertyName)!.IsShadowProperty());
    }

    /// <summary>
    /// Caller-owned properties that happen to use a conventional endpoint name are
    /// never removed when another property becomes the active period endpoint.
    /// </summary>
    [Fact]
    public void Explicit_endpoint_does_not_remove_independently_configured_default_property()
    {
        using var context = CreateCustomContext<CallerOwnedDefaultPropertyConfiguration>();
        var entityType = context.Model.FindEntityType(typeof(CustomApplicationTimeRecord))!;
        var callerOwnedProperty = entityType.FindProperty(MySqlApplicationTimeMetadata.DefaultPeriodStartPropertyName)!;
        var table = StoreObjectIdentifier.Table("CustomApplicationTimeRecords", null);

        Assert.True(callerOwnedProperty.IsShadowProperty());
        Assert.Equal(typeof(DateTime), callerOwnedProperty.ClrType);
        Assert.Equal("AuditValidFrom", callerOwnedProperty.GetColumnName(table));
        Assert.Equal(
            nameof(CustomApplicationTimeRecord.BusinessValidFrom),
            entityType.GetMySqlApplicationTimePeriodStartPropertyName());
    }

    /// <summary>
    /// MySQL cannot silently accept MariaDB-only application-time metadata.
    /// </summary>
    [Fact]
    public void Application_time_mapping_rejects_mysql()
    {
        using var context = CreateContext<ApplicationTimeConfiguration>(s_mySql84);

        var exception = Assert.Throws<InvalidOperationException>(() => _ = context.Model);

        Assert.Contains("does not support application-time periods", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// MariaDB releases that support periods but predate <c>WITHOUT OVERLAPS</c>
    /// fail explicitly instead of generating unsupported DDL.
    /// </summary>
    [Fact]
    public void Without_overlaps_rejects_unsupported_mariadb_version()
    {
        using var context = CreateContext<ApplicationTimeConfiguration>(s_mariaDb1043);

        var exception = Assert.Throws<InvalidOperationException>(() => _ = context.Model);

        Assert.Contains("does not support WITHOUT OVERLAPS", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// MariaDB permits <c>WITHOUT OVERLAPS</c> only on primary or unique keys.
    /// </summary>
    [Fact]
    public void Without_overlaps_rejects_non_unique_index()
    {
        using var context = CreateContext<NonUniqueIndexConfiguration>(s_mariaDb114);

        var exception = Assert.Throws<InvalidOperationException>(() => _ = context.Model);

        Assert.Contains("must be unique before WITHOUT OVERLAPS can be used", exception.Message,
            StringComparison.Ordinal);
    }

    private static readonly MySqlServerVersion s_mySql84 =
        MySqlServerVersion.MySql(new Version(8, 4, 0));

    private static readonly MySqlServerVersion s_mariaDb114 =
        MySqlServerVersion.MariaDb(new Version(11, 4, 0));

    private static readonly MySqlServerVersion s_mariaDb1043 =
        MySqlServerVersion.MariaDb(
            new Version(10, 4, 3),
            MySqlServerVersionCompatibilityMode.AllowUnsupported);

    private static ApplicationTimeContext<TConfiguration> CreateContext<TConfiguration>(
        MySqlServerVersion serverVersion
    )
        where TConfiguration : IApplicationTimeConfiguration, new()
    {
        var options = new DbContextOptionsBuilder<ApplicationTimeContext<TConfiguration>>()
            .UseMySql(
                "Server=localhost;Database=doka;User ID=root;Password=password;",
                serverVersion)
            .Options;

        return new ApplicationTimeContext<TConfiguration>(options);
    }

    private static CustomApplicationTimeContext<TConfiguration> CreateCustomContext<TConfiguration>()
        where TConfiguration : ICustomApplicationTimeConfiguration, new() => new(
        CreateOptions<CustomApplicationTimeContext<TConfiguration>>(s_mariaDb114));

    private static DbContextOptions<TContext> CreateOptions<TContext>(
        MySqlServerVersion serverVersion
    )
        where TContext : DbContext => new DbContextOptionsBuilder<TContext>()
        .UseMySql(
            "Server=localhost;Database=doka;User ID=root;Password=password;",
            serverVersion)
        .Options;

    private interface IApplicationTimeConfiguration
    {
        void Configure(EntityTypeBuilder<ApplicationTimeRecord> entity);
    }

    private interface ICustomApplicationTimeConfiguration
    {
        void Configure(EntityTypeBuilder<CustomApplicationTimeRecord> entity);
    }

    private sealed class ApplicationTimeConfiguration : IApplicationTimeConfiguration
    {
        public void Configure(
            EntityTypeBuilder<ApplicationTimeRecord> entity
        ) => entity.ToTable(
            "BusinessRecords",
            table => table.HasApplicationTimePeriod(period =>
            {
                period.HasPeriodName("BusinessValidity");
                period.HasPeriodStart(record => record.ValidFrom)
                    .HasColumnName("BusinessValidFrom");
                period.HasPeriodEnd(record => record.ValidTo)
                    .HasColumnName("BusinessValidTo");
                period.UseWithoutOverlaps();
            }));
    }

    private sealed class BitemporalConfiguration : IApplicationTimeConfiguration
    {
        public void Configure(
            EntityTypeBuilder<ApplicationTimeRecord> entity
        ) => entity.ToTable(
            "BusinessRecords",
            table => table.IsBitemporal(
                systemTime =>
                {
                    systemTime.HasPeriodStart("SystemFrom");
                    systemTime.HasPeriodEnd("SystemTo");
                },
                applicationTime =>
                {
                    applicationTime.HasPeriodName("BusinessValidity");
                    applicationTime.HasPeriodStart(record => record.ValidFrom);
                    applicationTime.HasPeriodEnd(record => record.ValidTo);
                }));
    }

    private sealed class NonUniqueIndexConfiguration : IApplicationTimeConfiguration
    {
        public void Configure(
            EntityTypeBuilder<ApplicationTimeRecord> entity
        )
        {
            entity.ToTable("BusinessRecords", table => table.HasApplicationTimePeriod());
            entity.HasIndex(record => record.Code)
                .UseWithoutOverlaps();
        }
    }

    private sealed class TypedCustomEndpointConfiguration : ICustomApplicationTimeConfiguration
    {
        public void Configure(
            EntityTypeBuilder<CustomApplicationTimeRecord> entity
        ) => entity.ToTable(
            "CustomApplicationTimeRecords",
            table => table.HasApplicationTimePeriod(period =>
            {
                period.HasPeriodStart(record => record.BusinessValidFrom);
                period.HasPeriodEnd(record => record.BusinessValidTo);
            }));
    }

    private sealed class SingleCustomEndpointConfiguration : ICustomApplicationTimeConfiguration
    {
        public void Configure(
            EntityTypeBuilder<CustomApplicationTimeRecord> entity
        ) => entity.ToTable(
            "CustomApplicationTimeRecords",
            table => table.HasApplicationTimePeriod(period =>
                period.HasPeriodStart(record => record.BusinessValidFrom)));
    }

    private sealed class DefaultEndpointConfiguration : ICustomApplicationTimeConfiguration
    {
        public void Configure(
            EntityTypeBuilder<CustomApplicationTimeRecord> entity
        ) => entity.ToTable("CustomApplicationTimeRecords", table => table.HasApplicationTimePeriod());
    }

    private sealed class CallerOwnedDefaultPropertyConfiguration : ICustomApplicationTimeConfiguration
    {
        public void Configure(
            EntityTypeBuilder<CustomApplicationTimeRecord> entity
        )
        {
            entity
                .Property<DateTime>(MySqlApplicationTimeMetadata.DefaultPeriodStartPropertyName)
                .HasColumnName("AuditValidFrom");
            entity.ToTable(
                "CustomApplicationTimeRecords",
                table => table.HasApplicationTimePeriod(period =>
                {
                    period.HasPeriodStart(record => record.BusinessValidFrom);
                    period.HasPeriodEnd(record => record.BusinessValidTo);
                }));
        }
    }

    private sealed class ApplicationTimeContext<TConfiguration> : DbContext
        where TConfiguration : IApplicationTimeConfiguration, new()
    {
        public ApplicationTimeContext(
            DbContextOptions<ApplicationTimeContext<TConfiguration>> options
        ) : base(options) { }

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            var entity = modelBuilder.Entity<ApplicationTimeRecord>();
            entity.HasKey(record => record.Id);
            new TConfiguration().Configure(entity);
        }
    }

    private sealed class CustomApplicationTimeContext<TConfiguration> : DbContext
        where TConfiguration : ICustomApplicationTimeConfiguration, new()
    {
        public CustomApplicationTimeContext(
            DbContextOptions<CustomApplicationTimeContext<TConfiguration>> options
        ) : base(options) { }

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            var entity = modelBuilder.Entity<CustomApplicationTimeRecord>();
            entity.HasKey(record => record.Id);
            new TConfiguration().Configure(entity);
        }
    }

    private sealed class StringApplicationTimeContext : DbContext
    {
        public StringApplicationTimeContext(
            DbContextOptions<StringApplicationTimeContext> options
        ) : base(options) { }

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            EntityTypeBuilder entity = modelBuilder.Entity<CustomApplicationTimeRecord>();
            entity.HasKey(nameof(CustomApplicationTimeRecord.Id));
            entity.ToTable(
                "CustomApplicationTimeRecords",
                table => table.HasApplicationTimePeriod(period =>
                {
                    period.HasPeriodStart(nameof(CustomApplicationTimeRecord.BusinessValidFrom));
                    period.HasPeriodEnd(nameof(CustomApplicationTimeRecord.BusinessValidTo));
                }));
        }
    }

    private sealed class ApplicationTimeRecord
    {
        public int Id { get; set; }

        public string Code { get; set; } = null!;

        public DateTime ValidFrom { get; set; }

        public DateTime ValidTo { get; set; }
    }

    private sealed class CustomApplicationTimeRecord
    {
        public int Id { get; set; }

        public DateTime BusinessValidFrom { get; set; }

        public DateTime BusinessValidTo { get; set; }
    }
}
