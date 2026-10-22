namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

/// <summary>Supplies default-bearing Guid keys to the snapshot and designer pipeline.</summary>
public sealed class GuidSqlDefaultDesignContext : GuidSqlDefaultDesignContextBase
{
    /// <summary>Creates the implicit store-generated Guid model.</summary>
    public GuidSqlDefaultDesignContext(
        DbContextOptions<GuidSqlDefaultDesignContext> options
    ) : base(options) { }

    /// <inheritdoc />
    protected override bool UseExplicitClientGuid => false;
}

/// <summary>Recreates the explicit ClientGuid metadata serialized by older snapshots.</summary>
public sealed class LegacyGuidSqlDefaultDesignContext : GuidSqlDefaultDesignContextBase
{
    /// <summary>Creates the legacy snapshot model.</summary>
    public LegacyGuidSqlDefaultDesignContext(
        DbContextOptions<LegacyGuidSqlDefaultDesignContext> options
    ) : base(options) { }

    /// <inheritdoc />
    protected override bool UseExplicitClientGuid => true;
}

/// <summary>Keeps legacy and implicit models physically identical for the compatibility comparison.</summary>
public abstract class GuidSqlDefaultDesignContextBase : DbContext
{
    /// <summary>Creates a model using the supplied provider options.</summary>
    protected GuidSqlDefaultDesignContextBase(
        DbContextOptions options
    ) : base(options) { }

    /// <summary>Gets whether client generation was explicitly serialized by an earlier snapshot.</summary>
    protected abstract bool UseExplicitClientGuid { get; }

    /// <inheritdoc />
    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        ConfigureGuid(modelBuilder, "SqlDefaultChar36DesignRecord", MySqlGuidFormat.Char36, "UUID()");
        ConfigureGuid(modelBuilder, "SqlDefaultNativeUuidDesignRecord", MySqlGuidFormat.Uuid, "UUID()");
        ConfigureGuid(
            modelBuilder,
            "SqlDefaultBinary16DesignRecord",
            MySqlGuidFormat.Binary16,
            "UNHEX(REPLACE(UUID(), '-', ''))");
    }

    /// <summary>Applies the same storage contract with only the legacy generation choice varying.</summary>
    private void ConfigureGuid(
        ModelBuilder modelBuilder,
        string entityName,
        MySqlGuidFormat format,
        string defaultSql
    )
    {
        modelBuilder.SharedTypeEntity<Dictionary<string, object>>(entityName, entity =>
        {
            entity.ToTable(entityName);

            var property = entity
                .IndexerProperty<Guid>("Id")
                .HasMySqlGuidFormat(format)
                .HasDefaultValueSql(defaultSql);

            entity.HasKey("Id");

            if (UseExplicitClientGuid)
            {
                // Serialized annotations are explicit user configuration when an old snapshot is replayed.
                property.UseMySqlClientGuidValueGeneration();
            }
        });
    }
}
