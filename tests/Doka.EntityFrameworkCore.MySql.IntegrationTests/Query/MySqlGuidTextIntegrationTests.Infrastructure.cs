namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlGuidTextIntegrationTests
{
    private const string TableName = "IntGuidTextItems";
    private const string MatchingText = "00112233-4455-6677-8899-aabbccddeeff";
    private const string UppercasePropertyName = "UppercaseCharColumnToken";
    private static readonly Guid s_matchingGuid = new(MatchingText);
    private static readonly MySqlGuidFormat[] s_defaultFormats = [MySqlGuidFormat.Binary16, MySqlGuidFormat.Char36];
    private static readonly string[] s_properties =
    [
        "DefaultToken",
        "BinaryToken",
        "TextToken",
        "BinaryColumnToken",
        "CharColumnToken",
        "VarCharColumnToken",
        "ConvertedTextToken",
    ];

    private static IEnumerable<IntegrationDatabaseTarget> SelectedTargets() =>
        IntegrationTestEnvironment.GetSupportedTargets().Where(IntegrationTestEnvironment.IsTargetSelected);

    /// <summary>
    /// Lazily owns one immutable GUID dataset per requested engine and default format.
    /// </summary>
    public sealed class GuidFixture : IAsyncLifetime
    {
        private readonly Dictionary<(IntegrationDatabaseTarget Target, MySqlGuidFormat Format), Task> _seeds = [];

        /// <inheritdoc />
        public ValueTask InitializeAsync() => ValueTask.CompletedTask;

        /// <summary>
        /// Seeds one selected engine/format pair once and returns a fresh context.
        /// </summary>
        internal async Task<GuidContext> CreateContextAsync(
            IntegrationDatabaseTarget target,
            MySqlGuidFormat format
        )
        {
            // WHY: xUnit serializes this class's cases. Retain a failed seed task
            // so filtered rows stay independent without retrying partial DDL.
            if (!_seeds.TryGetValue((target, format), out var seed))
            {
                seed = SeedAsync(target, format);
                _seeds.Add((target, format), seed);
            }

            await seed;

            return CreateContext(target, format);
        }

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            List<Exception>? failures = null;

            foreach (var key in _seeds.Keys)
            {
                try
                {
                    await using var context = CreateContext(key.Target, key.Format);
                    await DropTableAsync(context);
                }
                catch (Exception exception)
                {
                    // WHY: Cleanup must still reach the remaining engine/format pairs.
                    (failures ??= []).Add(new InvalidOperationException(
                        $"Could not drop {TableName} on {key.Target} with {key.Format}.", exception));
                }
            }

            if (failures is not null)
            {
                throw new AggregateException("GUID text fixture cleanup failed.", failures);
            }
        }

        private static async Task SeedAsync(
            IntegrationDatabaseTarget target,
            MySqlGuidFormat format
        )
        {
            await using var context = CreateContext(target, format);
            await DropTableAsync(context);
            await context.Database.ExecuteSqlRawAsync(
                context.Database.GenerateCreateScript(),
                CancellationToken.None);

            foreach (var id in new[] { 1, 2 })
            {
                // WHY: Explicit seed keys need no asynchronous value generation;
                // SaveChangesAsync below owns the database work.
                // ReSharper disable once MethodHasAsyncOverload
                var entry = context.Add(new GuidItem
                {
                    Id = id,
                    Pattern = "00112233-%-aabbccddeeff",
                });

                foreach (var propertyName in s_properties)
                {
                    entry.Property(propertyName).CurrentValue = id == 1 ? s_matchingGuid : Guid.Empty;
                    entry.Property($"Optional{propertyName}").CurrentValue = id == 1 ? s_matchingGuid : null;
                }

                entry.Property(UppercasePropertyName).CurrentValue = id == 1 ? s_matchingGuid : Guid.Empty;
            }

            await context.SaveChangesAsync(CancellationToken.None);

            // WHY: A dedicated column keeps the collation probe immutable;
            // independently scheduled mapping tests retain lowercase data.
            await context.Database.ExecuteSqlRawAsync(
                $"UPDATE `{TableName}` SET `{UppercasePropertyName}` = UPPER(`{UppercasePropertyName}`) "
                + "WHERE `Id` = 1;",
                CancellationToken.None);
        }

        /// <summary>
        /// Selects one stable schema for a class and configured default format.
        /// </summary>
        private static GuidContext CreateContext(
            IntegrationDatabaseTarget target,
            MySqlGuidFormat format
        )
        {
            // WHY: Formats have different default column types and need separate
            // schemas. The explicit owner stays stable when topic files move.
            var connectionString = IntegrationTestEnvironment.GetConnectionString(
                target,
                $"{nameof(MySqlGuidTextIntegrationTests)}.{format}");

            var builder = IntegrationTestDbContextOptions.Create<GuidContext>();
            builder.UseMySql(
                connectionString,
                IntegrationTestEnvironment.GetServerVersion(target),
                options => options.DefaultGuidFormat(format));

            return new GuidContext(builder.Options);
        }

        private static Task<int> DropTableAsync(
            GuidContext context
        ) => context.Database.ExecuteSqlRawAsync($"DROP TABLE IF EXISTS `{TableName}`;", CancellationToken.None);
    }

    /// <summary>
    /// Defines explicit and inferred GUID representations under either global default.
    /// </summary>
    internal sealed class GuidContext : DbContext
    {
        /// <summary>Uses fixture options for one engine and GUID default.</summary>
        public GuidContext(
            DbContextOptions<GuidContext> options
        ) : base(options) { }

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.HasCharSet("utf8mb4");
            var entity = modelBuilder.Entity<GuidItem>();
            entity.ToTable(TableName);

            foreach (var prefix in new[] { string.Empty, "Optional" })
            {
                var type = prefix.Length == 0 ? typeof(Guid) : typeof(Guid?);

                entity.Property(type, $"{prefix}DefaultToken");
                entity.Property(type, $"{prefix}BinaryToken")
                    .HasMySqlGuidFormat(MySqlGuidFormat.Binary16);
                entity.Property(type, $"{prefix}TextToken")
                    .HasMySqlGuidFormat(MySqlGuidFormat.Char36);
                entity.Property(type, $"{prefix}BinaryColumnToken")
                    .HasColumnType("binary(16)");
                entity.Property(type, $"{prefix}CharColumnToken")
                    .HasColumnType("char(36)")
                    .UseCollation("utf8mb4_bin");
                entity.Property(type, $"{prefix}VarCharColumnToken")
                    .HasColumnType("varchar(36)");
                entity.Property(type, $"{prefix}ConvertedTextToken")
                    .HasConversion<Microsoft.EntityFrameworkCore.Storage.ValueConversion.GuidToStringConverter>();
            }

            entity.Property<Guid>(UppercasePropertyName)
                .HasColumnType("char(36)")
                .UseCollation("utf8mb4_bin");
        }
    }

    internal sealed class GuidItem
    {
        public int Id { get; set; }
        public string Pattern { get; set; } = string.Empty;
    }
}
