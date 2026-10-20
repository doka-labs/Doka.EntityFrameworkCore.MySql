namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

/// <summary>Verifies binding owned references without an unrelated owned collection in the model.</summary>
public sealed class MySqlMaterializedSourceOwnedTests
{
    private const string TableName = "BindingReferenceOwners";
    private const string ProfileTableName = "BindingReferenceProfiles";
    private const string OwnerLabelColumn = "OwnerLabel";
    private const string ProfileNameColumn = "ProfileName";
    private const string ProfileTokenColumn = "ProfileToken";
    private const string InlineRequiredModel = "inline-required";
    private const string InlineOptionalModel = "inline-optional";
    private const string SeparateRequiredModel = "separate-required";
    private const string SeparateOptionalModel = "separate-optional";

    /// <summary>Owned scalar predicates and output are legal after binding the owner entity itself.</summary>
    [Theory]
    [InlineData(false, InlineRequiredModel)]
    [InlineData(true, InlineRequiredModel)]
    [InlineData(false, InlineOptionalModel)]
    [InlineData(true, InlineOptionalModel)]
    [InlineData(false, SeparateRequiredModel)]
    [InlineData(true, SeparateRequiredModel)]
    [InlineData(false, SeparateOptionalModel)]
    [InlineData(true, SeparateOptionalModel)]
    public void Bound_owner_preserves_owned_scalar_output(
        bool mariaDb,
        string mapping
    )
    {
        using var context = CreateContext(mariaDb, mapping);
        var reference = context.Owners.Where(owner => owner.Profile != null && owner.Profile.Name == "selected")
            .Select(owner => new { owner.Id, owner.Profile!.Name, owner.Profile!.Token })
            .OrderBy(row => row.Id)
            .ToQueryString();

        var query = context.Owners.WithMaterializedSource(source => source
                .Where(owner => owner.Profile != null && owner.Profile.Name == "selected")
                .Select(owner => new { owner.Id, owner.Profile!.Name, owner.Profile!.Token }))
            .OrderBy(row => row.Id);

        var actual = query.ToQueryString();

        AssertReferenceColumns(reference, actual);
        Assert.Contains("WHERE", actual, StringComparison.Ordinal);
        Assert.Contains("ORDER BY", actual, StringComparison.Ordinal);
        Assert.Contains("WITH RECURSIVE", actual, StringComparison.Ordinal);
    }

    /// <summary>Filtering and ordering a bound owner preserves its complete owned reference shaper.</summary>
    [Theory]
    [InlineData(false, InlineRequiredModel)]
    [InlineData(true, InlineRequiredModel)]
    [InlineData(false, InlineOptionalModel)]
    [InlineData(true, InlineOptionalModel)]
    [InlineData(false, SeparateRequiredModel)]
    [InlineData(true, SeparateRequiredModel)]
    [InlineData(false, SeparateOptionalModel)]
    [InlineData(true, SeparateOptionalModel)]
    public void Bound_ordered_owner_preserves_owned_materialization(
        bool mariaDb,
        string mapping
    )
    {
        using var context = CreateContext(mariaDb, mapping);
        var reference = context.Owners.Where(owner => owner.Id > 0).OrderBy(owner => owner.Id).ToQueryString();
        var query = context.Owners.WithMaterializedSource(source => source
            .Where(owner => owner.Id > 0)
            .OrderBy(owner => owner.Id));

        var actual = query.ToQueryString();

        AssertReferenceColumns(reference, actual);
        Assert.Contains(OwnerLabelColumn, reference, StringComparison.Ordinal);
        Assert.Contains(OwnerLabelColumn, actual, StringComparison.Ordinal);
        Assert.Contains("WHERE", actual, StringComparison.Ordinal);
        Assert.Contains("ORDER BY", actual, StringComparison.Ordinal);
        Assert.Contains("WITH RECURSIVE", actual, StringComparison.Ordinal);
    }

    /// <summary>A projected owned entity retains mapped scalar predicates and output as an untracked source.</summary>
    [Theory]
    [InlineData(false, InlineRequiredModel)]
    [InlineData(true, InlineRequiredModel)]
    [InlineData(false, InlineOptionalModel)]
    [InlineData(true, InlineOptionalModel)]
    [InlineData(false, SeparateRequiredModel)]
    [InlineData(true, SeparateRequiredModel)]
    [InlineData(false, SeparateOptionalModel)]
    [InlineData(true, SeparateOptionalModel)]
    public void Projected_owned_reference_preserves_mapped_scalar_output(
        bool mariaDb,
        string mapping
    )
    {
        using var context = CreateContext(mariaDb, mapping);
        // WHY: Ordinary EF forbids tracking an owned result without its owner. Use
        // the same explicitly untracked source for the reference and bound query.
        var source = context.Owners.AsNoTracking().Where(owner => owner.Profile != null)
            .Select(owner => owner.Profile!);

        var reference = source.Where(profile => profile.Name == "selected")
            .OrderBy(profile => profile.Name)
            .Select(profile => new { profile.Name, profile.Token })
            .ToQueryString();

        var query = source.WithMaterializedSource(bound => bound.Where(profile => profile.Name == "selected")
            .OrderBy(profile => profile.Name)
            .Select(profile => new { profile.Name, profile.Token }));

        var actual = query.ToQueryString();

        AssertReferenceColumns(reference, actual);
        Assert.Contains("WHERE", actual, StringComparison.Ordinal);
        Assert.Contains("ORDER BY", actual, StringComparison.Ordinal);
        Assert.Contains("WITH RECURSIVE", actual, StringComparison.Ordinal);
    }

    /// <summary>Checks required payload columns against ordinary EF before assessing the bound shape.</summary>
    private static void AssertReferenceColumns(
        string reference,
        string actual
    )
    {
        Assert.Contains(ProfileNameColumn, reference, StringComparison.Ordinal);
        Assert.Contains(ProfileTokenColumn, reference, StringComparison.Ordinal);
        Assert.Contains(ProfileNameColumn, actual, StringComparison.Ordinal);
        Assert.Contains(ProfileTokenColumn, actual, StringComparison.Ordinal);
    }

    /// <summary>Creates an exact context type for each model without replacing EF's model cache.</summary>
    private static ReferenceContext CreateContext(
        bool mariaDb,
        string mapping
    )
    {
        var version = mariaDb
            ? MySqlServerVersion.MariaDb(new Version(11, 8, 0))
            : MySqlServerVersion.MySql(new Version(8, 4, 0));

        return mapping switch
        {
            InlineRequiredModel => new InlineRequiredContext(CreateOptions<InlineRequiredContext>(version)),
            InlineOptionalModel => new InlineOptionalContext(CreateOptions<InlineOptionalContext>(version)),
            SeparateRequiredModel => new SeparateRequiredContext(CreateOptions<SeparateRequiredContext>(version)),
            SeparateOptionalModel => new SeparateOptionalContext(CreateOptions<SeparateOptionalContext>(version)),
            _ => throw new ArgumentOutOfRangeException(nameof(mapping), mapping, "Unknown owned-reference model."),
        };
    }

    /// <summary>Builds ordinary transient test options for one concrete model context.</summary>
    private static DbContextOptions<TContext> CreateOptions<TContext>(
        MySqlServerVersion version
    )
        where TContext : DbContext => MySqlFunctionalTestOptions.CreateTransientBuilder<TContext>()
        .UseMySql("Server=localhost;Database=owned_binding;User ID=unused;", version)
        .Options;

    /// <summary>Maps one owned reference using the concrete context's storage and nullability contract.</summary>
    private abstract class ReferenceContext(
        DbContextOptions options
    ) : DbContext(options)
    {
        /// <summary>Gets whether the owned reference has its own physical table.</summary>
        protected abstract bool SeparateTable { get; }

        /// <summary>Gets whether an owner may have no owned reference.</summary>
        protected abstract bool OptionalReference { get; }

        /// <summary>Gets owners whose reference belongs to normal entity materialization.</summary>
        public DbSet<ReferenceOwner> Owners => Set<ReferenceOwner>();

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<ReferenceOwner>(entity =>
            {
                entity.ToTable(TableName);
                entity.Property(owner => owner.Label).HasColumnName(OwnerLabelColumn).HasMaxLength(64);
                entity.OwnsOne(owner => owner.Profile, owned =>
                {
                    if (SeparateTable)
                    {
                        owned.ToTable(ProfileTableName);
                    }

                    owned.Property(profile => profile.Name).HasColumnName(ProfileNameColumn).HasMaxLength(64);
                    owned.Property(profile => profile.Token).HasColumnName(ProfileTokenColumn)
                        .HasMySqlGuidFormat(MySqlGuidFormat.Binary16);
                });
                entity.Navigation(owner => owner.Profile).IsRequired(!OptionalReference);
            });
        }
    }

    /// <summary>Identifies the required reference model stored in the owner's table.</summary>
    private sealed class InlineRequiredContext(
        DbContextOptions<InlineRequiredContext> options
    ) : ReferenceContext(options)
    {
        protected override bool SeparateTable => false;

        protected override bool OptionalReference => false;
    }

    /// <summary>Identifies the optional reference model stored in the owner's table.</summary>
    private sealed class InlineOptionalContext(
        DbContextOptions<InlineOptionalContext> options
    ) : ReferenceContext(options)
    {
        protected override bool SeparateTable => false;

        protected override bool OptionalReference => true;
    }

    /// <summary>Identifies the required reference model stored in its own table.</summary>
    private sealed class SeparateRequiredContext(
        DbContextOptions<SeparateRequiredContext> options
    ) : ReferenceContext(options)
    {
        protected override bool SeparateTable => true;

        protected override bool OptionalReference => false;
    }

    /// <summary>Identifies the optional reference model stored in its own table.</summary>
    private sealed class SeparateOptionalContext(
        DbContextOptions<SeparateOptionalContext> options
    ) : ReferenceContext(options)
    {
        protected override bool SeparateTable => true;

        protected override bool OptionalReference => true;
    }

    /// <summary>Provides one owned reference and no collection navigation.</summary>
    private sealed class ReferenceOwner
    {
        /// <summary>Gets or sets the owner identity.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets the owner's own payload.</summary>
        public string Label { get; set; } = string.Empty;

        /// <summary>Gets or sets the optional owned reference.</summary>
        public ReferenceProfile? Profile { get; set; }
    }

    /// <summary>Provides string and native binary GUID payload for the owned shaper.</summary>
    private sealed class ReferenceProfile
    {
        /// <summary>Gets or sets the owned string payload.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Gets or sets the owned native binary GUID payload.</summary>
        public Guid Token { get; set; }
    }
}
