namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

public sealed partial class MySqlGuidStoreGenerationTests
{
    /// <summary>Isolates model variants using the established transient provider policy.</summary>
    private static FragmentGuidContext CreateFragmentGuidContext(
        bool mariaDb,
        MySqlGuidFormat format,
        bool nullable,
        string expression,
        MySqlValueGenerationStrategy? strategy = null
    ) => new(
        MySqlFunctionalTestOptions
            .CreateTransientBuilder<FragmentGuidContext>()
            .UseMySql(
                "Server=localhost;Database=doka;User ID=root;Password=unused;",
                FragmentServerVersion(mariaDb),
                mysql => mysql.DefaultGuidFormat(format))
            .Options,
        format,
        nullable,
        expression,
        strategy);

    /// <summary>Creates a legal shared-table mapping with a secondary-only physical default owner.</summary>
    private static SharedFragmentGuidContext CreateSharedFragmentGuidContext(
        bool mariaDb,
        bool hasDefault
    ) => new(
        MySqlFunctionalTestOptions
            .CreateTransientBuilder<SharedFragmentGuidContext>()
            .UseMySql(
                "Server=localhost;Database=doka;User ID=root;Password=unused;",
                FragmentServerVersion(mariaDb),
                mysql => mysql.DefaultGuidFormat(MySqlGuidFormat.Char36))
            .Options,
        hasDefault);

    /// <summary>Selects a supported configured engine without opening a connection.</summary>
    private static MySqlServerVersion FragmentServerVersion(
        bool mariaDb
    ) => mariaDb ? MySqlServerVersion.MariaDb(new Version(11, 8, 0)) : MySqlServerVersion.MySql(new Version(8, 4, 0));

    /// <summary>Maps a GUID to a secondary fragment while retaining unrelated data on the primary table.</summary>
    private sealed class FragmentGuidContext(
        DbContextOptions<FragmentGuidContext> options,
        MySqlGuidFormat format,
        bool nullable,
        string expression,
        MySqlValueGenerationStrategy? strategy
    ) : DbContext(options)
    {
        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            var entity = modelBuilder.Entity<FragmentGuidRow>();
            entity.ToTable("FragmentGuidPayload");
            entity.HasKey(row => row.Id);
            entity.Ignore(nullable ? nameof(FragmentGuidRow.Token) : nameof(FragmentGuidRow.OptionalToken));
            if (expression != "computed")
            {
                entity.Ignore(row => row.Source);
            }

            var propertyName = nullable ? nameof(FragmentGuidRow.OptionalToken) : nameof(FragmentGuidRow.Token);
            var property = entity.Property(propertyName);

            if (expression == "computed")
            {
                property.HasComputedColumnSql("`Source`", stored: true);
            }
            else if (expression == "sql")
            {
                property.HasDefaultValueSql(DefaultSql(format));
            }

            if (strategy is { } configured)
            {
                property.HasMySqlValueGenerationStrategy(configured);
            }

            entity.SplitToTable(
                "FragmentGuidValues",
                table =>
                {
                    table.Property(propertyName);
                    if (expression == "computed")
                    {
                        table.Property(row => row.Source);
                    }
                });
        }
    }

    /// <summary>Shares secondary GUID columns without copying the principal's default annotations.</summary>
    private sealed class SharedFragmentGuidContext(
        DbContextOptions<SharedFragmentGuidContext> options,
        bool hasDefault
    ) : DbContext(options)
    {
        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<SharedFragmentGuidPrincipal>(entity =>
            {
                // WHY: EF permits shared secondary fragments only when both
                // entities also share their primary table. Distinct payload names
                // keep the asserted default inheritance limited to the shared columns.
                entity.ToTable("FragmentDependentPayload");
                entity.HasKey(row => row.Id);
                entity
                    .Property(row => row.Name)
                    .HasColumnName("PrincipalName");
                entity.SplitToTable(
                    "FragmentSharedGuid",
                    table =>
                    {
                        table.Property(row => row.Token);
                        table.Property(row => row.ComputedToken);
                        table.Property(row => row.Literal);
                    });

                if (hasDefault)
                {
                    entity
                        .Property(row => row.Token)
                        .HasDefaultValueSql("UUID()");
                    entity
                        .Property(row => row.ComputedToken)
                        .HasComputedColumnSql("'00112233-4455-4677-8899-aabbccddeeff'", stored: true);
                    entity
                        .Property(row => row.Literal)
                        .HasDefaultValue(17);
                }
            });
            modelBuilder.Entity<SharedFragmentGuidDependent>(entity =>
            {
                entity.ToTable("FragmentDependentPayload");
                entity.HasKey(row => row.Id);
                entity
                    .Property(row => row.Token)
                    .ValueGeneratedOnAdd();

                if (hasDefault)
                {
                    entity
                        .Property(row => row.ComputedToken)
                        .ValueGeneratedOnAddOrUpdate();
                }

                entity
                    .Property(row => row.Name)
                    .HasColumnName("DependentName");
                entity.SplitToTable(
                    "FragmentSharedGuid",
                    table =>
                    {
                        table.Property(row => row.Token);
                        table.Property(row => row.ComputedToken);
                        table.Property(row => row.Literal);
                        table.Property(row => row.Detail);
                    });
                entity
                    .HasOne(row => row.Principal)
                    .WithOne(row => row.Dependent)
                    .HasForeignKey<SharedFragmentGuidDependent>(row => row.Id);
            });
            modelBuilder
                .Entity<SharedFragmentGuidPrincipal>()
                .Navigation(row => row.Dependent)
                .IsRequired();
        }
    }

    /// <summary>Supplies separate payload and GUID fragment state.</summary>
    private sealed class FragmentGuidRow
    {
        /// <summary>Gets or sets the key present in both physical tables.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets the primary-table payload.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Gets or sets the required secondary GUID.</summary>
        public Guid Token { get; set; }

        /// <summary>Gets or sets the optional secondary GUID.</summary>
        public Guid? OptionalToken { get; set; }

        /// <summary>Gets or sets the input for a secondary computed GUID.</summary>
        public Guid Source { get; set; }
    }

    /// <summary>Owns the default-bearing physical shared column.</summary>
    private sealed class SharedFragmentGuidPrincipal
    {
        /// <summary>Gets or sets the shared integer key.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets the secondary shared GUID value.</summary>
        public Guid Token { get; set; }

        /// <summary>Gets or sets the computed secondary shared GUID.</summary>
        public Guid ComputedToken { get; set; }

        /// <summary>Gets or sets the literal-default secondary shared value.</summary>
        public int Literal { get; set; }

        /// <summary>Gets or sets payload required to keep the principal independently mapped.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Gets or sets the required shared-row dependent.</summary>
        public SharedFragmentGuidDependent Dependent { get; set; } = new();
    }

    /// <summary>Splits its key between its own payload and a shared secondary fragment.</summary>
    private sealed class SharedFragmentGuidDependent
    {
        /// <summary>Gets or sets the propagated shared key.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets the secondary shared GUID value.</summary>
        public Guid Token { get; set; }

        /// <summary>Gets or sets the computed secondary shared GUID.</summary>
        public Guid ComputedToken { get; set; }

        /// <summary>Gets or sets the literal-default secondary shared value.</summary>
        public int Literal { get; set; }

        /// <summary>Gets or sets the primary-table payload.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Gets or sets the secondary-table payload.</summary>
        public int Detail { get; set; }

        /// <summary>Gets or sets the principal established through relationship fixup.</summary>
        public SharedFragmentGuidPrincipal? Principal { get; set; }
    }
}
