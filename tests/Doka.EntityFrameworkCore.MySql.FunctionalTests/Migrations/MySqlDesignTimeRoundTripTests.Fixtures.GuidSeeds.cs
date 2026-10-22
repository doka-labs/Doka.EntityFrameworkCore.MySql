namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

/// <summary>
/// Test context for provider-owned Guid seed values across supported storage formats.
/// </summary>
public sealed class GuidSeedDesignContext : DbContext
{
    /// <summary>
    /// Creates the Guid seed design context.
    /// </summary>
    public GuidSeedDesignContext(
        DbContextOptions<GuidSeedDesignContext> options
    ) : base(options) { }

    /// <inheritdoc />
    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        modelBuilder.Entity<GuidSeedDesignRecord>(entity =>
        {
            entity.ToTable("GuidSeedDesignRecords");
            entity.HasKey(record => record.Id);
            entity
                .Property(record => record.ExplicitChar36)
                .HasMySqlGuidFormat(MySqlGuidFormat.Char36);
            entity
                .Property(record => record.Binary16)
                .HasMySqlGuidFormat(MySqlGuidFormat.Binary16);
            entity
                .Property(record => record.DefaultChar36)
                .HasDefaultValue(new Guid("27caab1e-a588-4dcc-bace-fef7cf47e1fd"));
            entity
                .Property(record => record.DefaultBinary16)
                .HasMySqlGuidFormat(MySqlGuidFormat.Binary16)
                .HasDefaultValue(new Guid("5c855603-622d-4bee-b232-f74f63026caf"));
            entity
                .Property(record => record.OptionalDefaultChar36)
                .HasDefaultValue(new Guid("19bbd2b4-6a65-4469-bfaa-6ed3d4c19d81"));
            entity
                .Property(record => record.EmptyDefaultChar36)
                .HasDefaultValue(Guid.Empty);
            entity.HasData(
                new GuidSeedDesignRecord
                {
                    Id = new Guid("bf1da273-beed-4197-ab57-4cf8395244d4"),
                    ExplicitChar36 = new Guid("5153b4e4-0158-4641-b387-433631824557"),
                    Binary16 = new Guid("89a78261-ea26-494e-a520-b518f51ed3d1"),
                    OptionalChar36 = new Guid("1e15bed0-86cb-408e-b1cf-08952340a095"),
                    DefaultChar36 = new Guid("e120f559-9af1-427a-bb5a-ded4877796ac"),
                    DefaultBinary16 = new Guid("9baf398b-0c24-46fe-b841-d50037246dfc"),
                    OptionalDefaultChar36 = new Guid("3f5f76c1-8dd0-4462-af89-9ca8ee53706e"),
                    EmptyDefaultChar36 = new Guid("4e888833-56a7-40de-83e9-21ad53fb6d1a"),
                    Name = "seeded",
                },
                new GuidSeedDesignRecord
                {
                    Id = new Guid("a5e91c65-450d-47fa-9683-b6471d3df651"),
                    ExplicitChar36 = new Guid("1e326748-be2c-4856-bf8d-cffc79c192cf"),
                    Binary16 = new Guid("79cf7ff0-1bb0-4007-8ef4-b345386a6f41"),
                    OptionalChar36 = null,
                    DefaultChar36 = new Guid("bd45b993-2736-4592-905b-c6e8e3913177"),
                    DefaultBinary16 = new Guid("7af4be5f-2999-4c3e-a246-edb247ec8a7e"),
                    OptionalDefaultChar36 = null,
                    EmptyDefaultChar36 = Guid.Empty,
                    Name = "nullable",
                });
        });

        modelBuilder.Entity<GuidSeedDependentRecord>(entity =>
        {
            entity.ToTable("GuidSeedDependentRecords");
            entity.HasKey(record => record.Id);
            entity
                .HasOne(record => record.Principal)
                .WithMany(record => record.RequiredDependents)
                .HasForeignKey(record => record.PrincipalId)
                .OnDelete(DeleteBehavior.Cascade);
            entity
                .HasOne(record => record.OptionalPrincipal)
                .WithMany(record => record.OptionalDependents)
                .HasForeignKey(record => record.OptionalPrincipalId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasData(
                new GuidSeedDependentRecord
                {
                    Id = 1,
                    PrincipalId = new Guid("bf1da273-beed-4197-ab57-4cf8395244d4"),
                    OptionalPrincipalId = new Guid("a5e91c65-450d-47fa-9683-b6471d3df651"),
                    Name = "related",
                },
                new GuidSeedDependentRecord
                {
                    Id = 2,
                    PrincipalId = new Guid("a5e91c65-450d-47fa-9683-b6471d3df651"),
                    OptionalPrincipalId = null,
                    Name = "nullable-related",
                });
        });
    }
}

/// <summary>
/// Entity used by the provider-owned Guid seed design model.
/// </summary>
public sealed class GuidSeedDesignRecord
{
    /// <summary>
    /// Gets or sets the context-default Char36 key.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets an explicitly configured Char36 value.
    /// </summary>
    public Guid ExplicitChar36 { get; set; }

    /// <summary>
    /// Gets or sets an explicitly configured Binary16 value.
    /// </summary>
    public Guid Binary16 { get; set; }

    /// <summary>
    /// Gets or sets an optional context-default Char36 value.
    /// </summary>
    public Guid? OptionalChar36 { get; set; }

    /// <summary>
    /// Gets or sets a value with a provider-owned Char36 default.
    /// </summary>
    public Guid DefaultChar36 { get; set; }

    /// <summary>
    /// Gets or sets a value with a provider-owned Binary16 default.
    /// </summary>
    public Guid DefaultBinary16 { get; set; }

    /// <summary>
    /// Gets or sets an optional value with a provider-owned Char36 default.
    /// </summary>
    public Guid? OptionalDefaultChar36 { get; set; }

    /// <summary>
    /// Gets or sets a value with an empty provider-owned Char36 default.
    /// </summary>
    public Guid EmptyDefaultChar36 { get; set; }

    /// <summary>
    /// Gets or sets the seed label.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets the dependents using the required foreign key.
    /// </summary>
    public ICollection<GuidSeedDependentRecord> RequiredDependents { get; } = [];

    /// <summary>
    /// Gets the dependents using the optional foreign key.
    /// </summary>
    public ICollection<GuidSeedDependentRecord> OptionalDependents { get; } = [];
}

/// <summary>
/// Minimal converted-Char36 design context without an unconverted Guid mapping
/// that could add the required model namespace as a side effect.
/// </summary>
public sealed class Char36SeedOnlyDesignContext : DbContext
{
    /// <summary>
    /// Creates the converted-Char36-only design context.
    /// </summary>
    public Char36SeedOnlyDesignContext(
        DbContextOptions<Char36SeedOnlyDesignContext> options
    ) : base(options) { }

    /// <inheritdoc />
    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        modelBuilder.Entity<Char36SeedOnlyDesignRecord>(entity =>
        {
            entity.HasKey(record => record.Id);
            entity.HasData(
                new Char36SeedOnlyDesignRecord
                {
                    Id = new Guid("ef538fc8-d6f4-46d1-8b4e-6c8b26ab8137"),
                    Name = "seeded",
                });
        });
    }
}

/// <summary>
/// Entity for the converted-Char36-only namespace regression.
/// </summary>
public sealed class Char36SeedOnlyDesignRecord
{
    /// <summary>
    /// Gets or sets the converted Char36 key.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the seed label.
    /// </summary>
    public string Name { get; set; } = string.Empty;
}

/// <summary>
/// Design context whose only converted Guid is nested in a complex property.
/// </summary>
public sealed class Char36ComplexOnlyDesignContext : DbContext
{
    /// <summary>
    /// Creates the converted-Char36 complex-property design context.
    /// </summary>
    public Char36ComplexOnlyDesignContext(
        DbContextOptions<Char36ComplexOnlyDesignContext> options
    ) : base(options) { }

    /// <inheritdoc />
    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        modelBuilder.Entity<Char36ComplexOnlyDesignRecord>(entity =>
        {
            entity.HasKey(record => record.Id);
            entity.ComplexProperty(
                record => record.Details,
                complex =>
                {
                    complex
                        .Property(details => details.ExternalId)
                        .HasDefaultValue(new Guid("0517e651-62e5-4a93-845d-fd87b14e4363"));
                    complex
                        .Property(details => details.ApplicationId)
                        .HasConversion<string>()
                        .HasColumnType("varchar(36)");
                });
        });
    }
}

/// <summary>
/// Entity for the converted-Char36 complex-property namespace regression.
/// </summary>
public sealed class Char36ComplexOnlyDesignRecord
{
    /// <summary>
    /// Gets or sets the key.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Gets or sets the complex details.
    /// </summary>
    public Char36ComplexOnlyDesignDetails Details { get; set; } = new();
}

/// <summary>
/// Complex details containing the only converted Guid in the model.
/// </summary>
public sealed class Char36ComplexOnlyDesignDetails
{
    /// <summary>
    /// Gets or sets the converted identifier.
    /// </summary>
    public Guid ExternalId { get; set; }

    /// <summary>
    /// Gets or sets an application-converted negative control.
    /// </summary>
    public Guid ApplicationId { get; set; }
}

/// <summary>
/// Seeded dependent used to verify provider-owned Guid foreign keys.
/// </summary>
public sealed class GuidSeedDependentRecord
{
    /// <summary>
    /// Gets or sets the dependent key.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Gets or sets the required provider-owned Char36 foreign key.
    /// </summary>
    public Guid PrincipalId { get; set; }

    /// <summary>
    /// Gets or sets the optional provider-owned Char36 foreign key.
    /// </summary>
    public Guid? OptionalPrincipalId { get; set; }

    /// <summary>
    /// Gets or sets the seed label.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the required principal navigation.
    /// </summary>
    public GuidSeedDesignRecord Principal { get; set; } = null!;

    /// <summary>
    /// Gets or sets the optional principal navigation.
    /// </summary>
    public GuidSeedDesignRecord? OptionalPrincipal { get; set; }
}

/// <summary>
/// Test context for an application-owned Guid-to-string seed converter.
/// </summary>
public sealed class ApplicationGuidSeedDesignContext : DbContext
{
    /// <summary>
    /// Creates the application-converter seed design context.
    /// </summary>
    public ApplicationGuidSeedDesignContext(
        DbContextOptions<ApplicationGuidSeedDesignContext> options
    ) : base(options) { }

    /// <inheritdoc />
    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    )
    {
        modelBuilder.Entity<ApplicationGuidSeedDesignRecord>(entity =>
        {
            entity.ToTable("ApplicationGuidSeedDesignRecords");
            entity.HasKey(record => record.Id);
            entity
                .Property(record => record.Id)
                .HasConversion<string>()
                .HasDefaultValue(new Guid("89a78261-ea26-494e-a520-b518f51ed3d1"));
            entity.HasData(
                new ApplicationGuidSeedDesignRecord
                {
                    Id = new Guid("89a78261-ea26-494e-a520-b518f51ed3d1"),
                    Name = "application-converted",
                });
        });
    }
}

/// <summary>
/// Entity used by the application-owned Guid converter seed model.
/// </summary>
public sealed class ApplicationGuidSeedDesignRecord
{
    /// <summary>
    /// Gets or sets the application-converted key.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the seed label.
    /// </summary>
    public string Name { get; set; } = string.Empty;
}
