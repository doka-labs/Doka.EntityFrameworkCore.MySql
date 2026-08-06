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

    private interface IApplicationTimeConfiguration
    {
        void Configure(EntityTypeBuilder<ApplicationTimeRecord> entity);
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

    private sealed class ApplicationTimeRecord
    {
        public int Id { get; set; }

        public string Code { get; set; } = null!;

        public DateTime ValidFrom { get; set; }

        public DateTime ValidTo { get; set; }
    }
}
