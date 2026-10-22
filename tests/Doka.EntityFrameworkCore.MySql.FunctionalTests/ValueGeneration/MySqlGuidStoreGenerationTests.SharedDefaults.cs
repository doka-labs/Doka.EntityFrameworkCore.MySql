namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

public sealed partial class MySqlGuidStoreGenerationTests
{
    /// <summary>The dependent's own generator resolves its shared-root default, not only propagated keys.</summary>
    /// <param name="format">The physical Guid representation.</param>
    [Theory]
    [InlineData(MySqlGuidFormat.Char36)]
    [InlineData(MySqlGuidFormat.Binary16)]
    [InlineData(MySqlGuidFormat.Uuid)]
    public void Shared_guid_sql_default_selects_temporary_generator_for_dependent_metadata(
        MySqlGuidFormat format
    )
    {
        // Arrange
        using var context = CreateSharedGuidDefaultContext(format);
        var property = context.Model.FindEntityType(typeof(SharedGuidDefaultDependent))!
            .FindProperty(nameof(SharedGuidDefaultDependent.Id))!;

        var selector = context.GetService<Microsoft.EntityFrameworkCore.ValueGeneration.IValueGeneratorSelector>();

        // Act
        var selected = selector.TrySelect(property, property.DeclaringType, out var generator);

        // Assert
        Assert.Null(property.GetDefaultValueSql());
        Assert.Null(property.GetMySqlValueGenerationStrategy());
        Assert.True(selected);
        Assert.NotNull(generator);
        Assert.True(generator.GeneratesTemporaryValues);
    }

    /// <summary>Shared Guid keys inherit the physical SQL default and propagate a temporary tracking value.</summary>
    /// <param name="format">The physical Guid representation.</param>
    [Theory]
    [InlineData(MySqlGuidFormat.Char36)]
    [InlineData(MySqlGuidFormat.Binary16)]
    [InlineData(MySqlGuidFormat.Uuid)]
    public void Shared_guid_sql_default_preserves_store_generation_on_both_properties(
        MySqlGuidFormat format
    )
    {
        // Arrange
        using var context = CreateSharedGuidDefaultContext(format);
        var principal = new SharedGuidDefaultPrincipal();
        var dependent = principal.Dependent;
        var dependentProperty = context.Model.FindEntityType(typeof(SharedGuidDefaultDependent))!
            .FindProperty(nameof(SharedGuidDefaultDependent.Id))!;

        var storeObject = StoreObjectIdentifier.Table("SharedGuidSqlDefaults", schema: null);
        var propertyDefaultSql = dependentProperty.GetDefaultValueSql();
        var physicalDefaultSql = dependentProperty.GetDefaultValueSql(storeObject);

        // Act
        var principalEntry = context.Add(principal);
        var dependentEntry = context.Entry(dependent);
        var principalKey = principalEntry.Property(row => row.Id);
        var dependentKey = dependentEntry.Property(row => row.Id);

        // Assert
        Assert.Null(propertyDefaultSql);
        Assert.Equal(DefaultSql(format), physicalDefaultSql);
        Assert.Null(principalKey.Metadata.GetMySqlValueGenerationStrategy());
        Assert.Null(dependentKey.Metadata.GetMySqlValueGenerationStrategy());
        Assert.Equal(ValueGenerated.OnAdd, principalKey.Metadata.ValueGenerated);
        Assert.Equal(ValueGenerated.OnAdd, dependentKey.Metadata.ValueGenerated);
        Assert.True(principalKey.IsTemporary);
        Assert.True(dependentKey.IsTemporary);
        Assert.NotEqual(Guid.Empty, principalKey.CurrentValue);
        Assert.Equal(principalKey.CurrentValue, dependentKey.CurrentValue);
        Assert.Equal(Guid.Empty, principal.Id);
        Assert.Equal(Guid.Empty, dependent.Id);
        Assert.Same(principal, dependent.Principal);
    }

    /// <summary>An explicit root client strategy still owns generation of the shared Guid key.</summary>
    [Fact]
    public void Shared_guid_sql_default_preserves_explicit_root_client_strategy()
    {
        // Arrange
        using var context = CreateSharedGuidDefaultContext(MySqlGuidFormat.Char36, explicitClient: true);
        var principal = new SharedGuidDefaultPrincipal();
        var dependent = principal.Dependent;

        // Act
        var principalEntry = context.Add(principal);
        var dependentEntry = context.Entry(dependent);
        var principalKey = principalEntry.Property(row => row.Id);
        var dependentKey = dependentEntry.Property(row => row.Id);

        // Assert
        Assert.Equal(
            MySqlValueGenerationStrategy.ClientGuid,
            principalKey.Metadata.GetMySqlValueGenerationStrategy());
        Assert.Null(dependentKey.Metadata.GetMySqlValueGenerationStrategy());
        Assert.Equal("UUID()", principalKey.Metadata.GetDefaultValueSql());
        Assert.False(principalKey.IsTemporary);
        Assert.False(dependentKey.IsTemporary);
        Assert.NotEqual(Guid.Empty, principal.Id);
        Assert.Equal(7, principal.Id.Version);
        Assert.Equal(principal.Id, dependent.Id);
    }

    /// <summary>Isolates shared-key model variants without entering EF's global provider cache.</summary>
    private static SharedGuidDefaultContext CreateSharedGuidDefaultContext(
        MySqlGuidFormat format,
        bool explicitClient = false
    ) => new(
        MySqlFunctionalTestOptions.CreateTransientBuilder<SharedGuidDefaultContext>()
            .UseMySql(
                "Server=localhost;Database=doka;User ID=root;Password=unused;",
                MySqlServerVersion.MariaDb(new Version(11, 8, 0)),
                mysql => mysql.DefaultGuidFormat(format))
            .Options,
        format,
        explicitClient);

    /// <summary>Maps a Guid primary key to one physical column shared by principal and dependent.</summary>
    private sealed class SharedGuidDefaultContext : DbContext
    {
        private readonly MySqlGuidFormat _format;
        private readonly bool _explicitClient;

        /// <summary>Creates a context for the requested shared-key generation contract.</summary>
        public SharedGuidDefaultContext(
            DbContextOptions<SharedGuidDefaultContext> options,
            MySqlGuidFormat format,
            bool explicitClient
        ) : base(options)
        {
            _format = format;
            _explicitClient = explicitClient;
        }

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<SharedGuidDefaultPrincipal>(entity =>
            {
                entity.ToTable("SharedGuidSqlDefaults");
                entity.HasKey(row => row.Id);

                var key = entity.Property(row => row.Id).HasDefaultValueSql(DefaultSql(_format));

                if (_explicitClient)
                {
                    key.UseMySqlClientGuidValueGeneration();
                }
            });
            modelBuilder.Entity<SharedGuidDefaultDependent>(entity =>
            {
                entity.ToTable("SharedGuidSqlDefaults");
                entity.HasKey(row => row.Id);
                entity.Property(row => row.Id).ValueGeneratedOnAdd();
                entity.HasOne(row => row.Principal).WithOne(row => row.Dependent)
                    .HasForeignKey<SharedGuidDefaultDependent>(row => row.Id);
            });
            modelBuilder.Entity<SharedGuidDefaultPrincipal>().Navigation(row => row.Dependent).IsRequired();
        }
    }

    /// <summary>Owns the default-bearing physical key and the dependent graph.</summary>
    private sealed class SharedGuidDefaultPrincipal
    {
        /// <summary>Gets or sets the shared key.</summary>
        public Guid Id { get; set; }

        /// <summary>Gets or sets the required dependent mapped to the same row.</summary>
        public SharedGuidDefaultDependent Dependent { get; set; } = new();
    }

    /// <summary>Uses the root's physical default without configuring a property-level SQL default.</summary>
    private sealed class SharedGuidDefaultDependent
    {
        /// <summary>Gets or sets the propagated shared key.</summary>
        public Guid Id { get; set; }

        /// <summary>Gets or sets the dependent payload.</summary>
        public int Detail { get; set; }

        /// <summary>Gets or sets the principal established by relationship fixup.</summary>
        public SharedGuidDefaultPrincipal? Principal { get; set; }
    }
}
