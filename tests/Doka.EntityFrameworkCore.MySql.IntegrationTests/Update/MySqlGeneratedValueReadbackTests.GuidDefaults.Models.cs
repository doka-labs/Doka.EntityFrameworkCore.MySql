namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlGeneratedValueReadbackTests
{
    private const string GuidDefaultPrincipalTable = "GuidDefaultReadbackPrincipals";
    private const string GuidDefaultDependentTable = "GuidDefaultReadbackDependents";
    private const string GuidDefaultValueTable = "GuidDefaultReadbackValues";
    private const string GuidComputedValueTable = "GuidComputedReadbackValues";
    private const string GuidDefaultLiteralKeyTable = "GuidDefaultLiteralKeyRows";
    private const string GuidDefaultSharedTable = "GuidDefaultSharedRows";
    private const string GuidSplitGeneratedPrincipalTable = "GuidSplitGeneratedPrincipals";
    private const string GuidSplitGeneratedSecondaryTable = "GuidSplitGeneratedDetails";

    private static DbContext CreateGuidDefaultsContext<TMode>(
        IntegrationDatabaseTarget target,
        MySqlGuidFormat format,
        ReadbackCapture? capture = null
    ) => format switch
    {
        MySqlGuidFormat.Char36 => CreateGuidDefaultsContext<GuidDefaultsCharMapping, TMode>(target, capture),
        MySqlGuidFormat.Binary16 => CreateGuidDefaultsContext<GuidDefaultsBinaryMapping, TMode>(target, capture),
        MySqlGuidFormat.Uuid => CreateGuidDefaultsContext<GuidDefaultsNativeMapping, TMode>(target, capture),
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unsupported GUID readback mapping."),
    };

    private static GuidDefaultsContext<TMapping, TMode> CreateGuidDefaultsContext<TMapping, TMode>(
        IntegrationDatabaseTarget target,
        ReadbackCapture? capture
    )
    {
        var connectionString = new MySqlConnectionStringBuilder(
            IntegrationTestEnvironment.GetConnectionString(target))
        {
            GuidFormat = MySqlConnector.MySqlGuidFormat.Binary16,
        }.ConnectionString;

        var builder = IntegrationTestDbContextOptions.Create<GuidDefaultsContext<TMapping, TMode>>();
        builder.UseMySql(connectionString, IntegrationTestEnvironment.GetServerVersion(target),
            mysql => mysql.MaxBatchSize(1000).DefaultGuidFormat(GetGuidDefaultsFormat<TMapping>()));

        if (capture is not null)
        {
            builder.AddInterceptors(capture);
        }

        return new GuidDefaultsContext<TMapping, TMode>(builder.Options);
    }

    private static MySqlGuidFormat GetGuidDefaultsFormat<TMapping>() => typeof(TMapping) switch
    {
        var mapping when mapping == typeof(GuidDefaultsCharMapping) => MySqlGuidFormat.Char36,
        var mapping when mapping == typeof(GuidDefaultsBinaryMapping) => MySqlGuidFormat.Binary16,
        _ => MySqlGuidFormat.Uuid,
    };

    private static async Task RecreateGuidDefaultsTablesAsync(
        DbContext context
    )
    {
        // WHY: Dependents must be removed first because the previous case can
        // leave a real FK constraint, even when the next model has no relationship.
        await context.Database.ExecuteSqlRawAsync(
            $"DROP TABLE IF EXISTS `{GuidSplitGeneratedSecondaryTable}`, `{GuidSplitGeneratedPrincipalTable}`, "
            + $"`{GuidDefaultDependentTable}`, `{GuidDefaultPrincipalTable}`, "
            + $"`{GuidDefaultValueTable}`, `{GuidComputedValueTable}`, `{GuidDefaultLiteralKeyTable}`, "
            + $"`{GuidDefaultSharedTable}`;",
            CancellationToken.None);
        await context.Database.ExecuteSqlRawAsync(context.Database.GenerateCreateScript(), CancellationToken.None);
    }

    private static string GuidDefaultSql(
        MySqlGuidFormat format
    ) => format == MySqlGuidFormat.Binary16 ? "UNHEX(REPLACE(UUID(), '-', ''))" : "UUID()";

    private static string GuidComputedSql(
        MySqlGuidFormat format
    )
    {
        const string text = "CONCAT('00112233-4455-6677-8899-', LPAD(CAST(`Input` AS CHAR), 12, '0'))";

        return format == MySqlGuidFormat.Binary16 ? $"UNHEX(REPLACE({text}, '-', ''))" : text;
    }

    private static Guid ExpectedComputedGuid(
        int input
    ) => Guid.Parse("00112233-4455-6677-8899-" + input.ToString("D12", CultureInfo.InvariantCulture));

    private sealed class GuidDefaultsContext<TMapping, TMode>(
        DbContextOptions<GuidDefaultsContext<TMapping, TMode>> options
    ) : DbContext(options)
    {
        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            var format = GetGuidDefaultsFormat<TMapping>();

            if (typeof(TMode) == typeof(GuidDefaultsKeyMode))
            {
                var principal = modelBuilder.Entity<GuidDefaultPrincipal>();
                principal.ToTable(GuidDefaultPrincipalTable);
                principal.UseStorageEngine("InnoDB");
                principal.HasKey(row => row.Id);
                principal.HasIndex(row => row.Marker).IsUnique();
                principal.Property(row => row.Id).HasDefaultValueSql(GuidDefaultSql(format));

                var dependent = modelBuilder.Entity<GuidDefaultDependent>();
                dependent.ToTable(GuidDefaultDependentTable);
                dependent.UseStorageEngine("InnoDB");
                dependent.HasKey(row => row.Id);
                dependent.Property(row => row.Id).ValueGeneratedNever();
                dependent.HasOne(row => row.Principal)
                    .WithMany(row => row.Dependents)
                    .HasForeignKey(row => row.PrincipalId);
            }
            else if (typeof(TMode) == typeof(GuidDefaultsValueMode))
            {
                var entity = modelBuilder.Entity<GuidDefaultValueRow>();
                entity.ToTable(GuidDefaultValueTable);
                entity.UseStorageEngine("InnoDB");
                entity.HasKey(row => row.Id);
                entity.Property(row => row.Id).ValueGeneratedNever();
                entity.Property(row => row.RequiredValue).HasDefaultValueSql(GuidDefaultSql(format));
                entity.Property(row => row.NullableValue).HasDefaultValueSql(GuidDefaultSql(format));
                // WHY: Application conversions must survive generated readback
                // independently of the provider's default GUID storage mapping.
                entity.Property(row => row.ConvertedValue)
                    .HasConversion(value => value.ToString(), value => Guid.Parse(value))
                    .HasColumnType("char(36)")
                    .HasDefaultValueSql("UUID()");
                entity.Property(row => row.ConvertedNullableValue)
                    .HasConversion<string>()
                    .HasColumnType("char(36)")
                    .HasDefaultValueSql("UUID()");
            }
            else if (typeof(TMode) == typeof(GuidDefaultsLiteralKeyMode))
            {
                var entity = modelBuilder.Entity<GuidDefaultLiteralKeyRow>();
                entity.ToTable(GuidDefaultLiteralKeyTable);
                entity.UseStorageEngine("InnoDB");
                entity.HasKey(row => new { row.Id, row.Flag });
                entity.Property(row => row.Id).ValueGeneratedNever();
                entity.Property(row => row.Flag).HasDefaultValue(true).HasSentinel(false);
                entity.Property(row => row.Stamp).HasComputedColumnSql("`Marker` * 17 + 3", stored: true);
            }
            else if (typeof(TMode) == typeof(GuidDefaultsSharedTableMode))
            {
                var principal = modelBuilder.Entity<GuidDefaultSharedPrincipal>();
                principal.ToTable(GuidDefaultSharedTable);
                principal.UseStorageEngine("InnoDB");
                principal.HasKey(row => row.Id);
                principal.Property(row => row.Id).HasDefaultValueSql(GuidDefaultSql(format));
                principal.Property(row => row.Payload).HasColumnName("PrincipalPayload").HasMaxLength(64);
                principal.HasOne(row => row.Dependent)
                    .WithOne(row => row.Principal)
                    .HasForeignKey<GuidDefaultSharedDependent>(row => row.Id)
                    .IsRequired();
                principal.Navigation(row => row.Dependent).IsRequired();

                var dependent = modelBuilder.Entity<GuidDefaultSharedDependent>();
                dependent.ToTable(GuidDefaultSharedTable);
                dependent.UseStorageEngine("InnoDB");
                dependent.HasKey(row => row.Id);
                // WHY: Only the shared-column root declares the SQL default;
                // dependent generation must resolve that store-column metadata.
                dependent.Property(row => row.Id).ValueGeneratedOnAdd();
                dependent.Property(row => row.Payload).HasColumnName("DependentPayload").HasMaxLength(64);
            }
            else if (typeof(TMode) == typeof(GuidDefaultsSplitValueMode)
                     || typeof(TMode) == typeof(GuidDefaultsSplitComputedMode))
            {
                var entity = modelBuilder.Entity<GuidSplitGeneratedRow>();
                entity.ToTable(GuidSplitGeneratedPrincipalTable);
                entity.UseStorageEngine("InnoDB");
                entity.HasKey(row => row.Id);
                entity.Property(row => row.Id).ValueGeneratedNever();
                entity.Property(row => row.Payload).HasMaxLength(64);

                // WHY: Generated outputs belong only to the secondary store;
                // integer keys let both engines read them back without guessing UUID keys.
                entity.SplitToTable(GuidSplitGeneratedSecondaryTable, split =>
                {
                    split.Property(row => row.Input);
                    split.Property(row => row.RequiredValue);
                    split.Property(row => row.NullableValue);
                });

                if (typeof(TMode) == typeof(GuidDefaultsSplitValueMode))
                {
                    entity.Property(row => row.RequiredValue).HasDefaultValueSql(GuidDefaultSql(format));
                    entity.Property(row => row.NullableValue).HasDefaultValueSql(GuidDefaultSql(format));
                }
                else
                {
                    var expression = GuidComputedSql(format);
                    entity.Property(row => row.RequiredValue).HasComputedColumnSql(expression, stored: true);
                    entity.Property(row => row.NullableValue).HasComputedColumnSql(
                        $"CASE WHEN MOD(`Input`, 2) = 0 THEN NULL ELSE {expression} END", stored: true);
                }
            }
            else
            {
                var expression = GuidComputedSql(format);
                var entity = modelBuilder.Entity<GuidComputedValueRow>();
                entity.ToTable(GuidComputedValueTable);
                entity.UseStorageEngine("InnoDB");
                entity.HasKey(row => row.Id);
                entity.Property(row => row.Id).ValueGeneratedNever();
                entity.Property(row => row.RequiredValue).HasComputedColumnSql(expression, stored: true);
                entity.Property(row => row.NullableValue)
                    .HasComputedColumnSql(
                        $"CASE WHEN MOD(`Input`, 2) = 0 THEN NULL ELSE {expression} END", stored: true);
            }
        }
    }

    private sealed class GuidDefaultPrincipal
    {
        public Guid Id { get; set; }

        public int Marker { get; set; }

        public ICollection<GuidDefaultDependent> Dependents { get; } = [];
    }

    private sealed class GuidDefaultDependent
    {
        public int Id { get; set; }

        public Guid PrincipalId { get; set; }

        public GuidDefaultPrincipal Principal { get; set; } = null!;
    }

    private sealed class GuidDefaultValueRow
    {
        public int Id { get; set; }

        public Guid RequiredValue { get; set; }

        public Guid? NullableValue { get; set; }

        public Guid ConvertedValue { get; set; }

        public Guid? ConvertedNullableValue { get; set; }
    }

    private sealed class GuidComputedValueRow
    {
        public int Id { get; set; }

        public int Input { get; set; }

        public Guid RequiredValue { get; set; }

        public Guid? NullableValue { get; set; }
    }

    private sealed class GuidDefaultLiteralKeyRow
    {
        public Guid Id { get; set; }

        public bool Flag { get; set; }

        public int Marker { get; set; }

        public long Stamp { get; set; }
    }

    private sealed class GuidSplitGeneratedRow
    {
        /// <summary>Gets or sets the supplied key shared by both fragments.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets the main fragment's independent payload.</summary>
        public string Payload { get; set; } = string.Empty;

        /// <summary>Gets or sets the secondary fragment's computation input.</summary>
        public int Input { get; set; }

        /// <summary>Gets or sets the required generated secondary GUID.</summary>
        public Guid RequiredValue { get; set; }

        /// <summary>Gets or sets the nullable generated secondary GUID.</summary>
        public Guid? NullableValue { get; set; }
    }

    private sealed class GuidDefaultSharedPrincipal
    {
        public Guid Id { get; set; }

        public string Payload { get; set; } = string.Empty;

        public GuidDefaultSharedDependent Dependent { get; set; } = null!;
    }

    private sealed class GuidDefaultSharedDependent
    {
        public Guid Id { get; set; }

        public string Payload { get; set; } = string.Empty;

        public GuidDefaultSharedPrincipal Principal { get; set; } = null!;
    }

    private sealed class GuidDefaultsCharMapping { }

    private sealed class GuidDefaultsBinaryMapping { }

    private sealed class GuidDefaultsNativeMapping { }

    private sealed class GuidDefaultsKeyMode { }

    private sealed class GuidDefaultsValueMode { }

    private sealed class GuidDefaultsComputedMode { }

    private sealed class GuidDefaultsLiteralKeyMode { }

    private sealed class GuidDefaultsSharedTableMode { }

    private sealed class GuidDefaultsSplitValueMode { }

    private sealed class GuidDefaultsSplitComputedMode { }
}
