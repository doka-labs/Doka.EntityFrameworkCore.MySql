namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlLikeIntegrationTests
{
    private const string TableName = "IntScalarLikeItems";
    private static readonly Guid s_matchingGuid = new("00112233-4455-6677-8899-aabbccddeeff");

    /// <summary>
    /// Lazily owns one immutable scalar dataset per requested engine.
    /// </summary>
    public sealed class LikeFixture : IAsyncLifetime
    {
        private readonly Dictionary<IntegrationDatabaseTarget, Task> _seeds = [];

        /// <inheritdoc />
        public ValueTask InitializeAsync() => ValueTask.CompletedTask;

        /// <summary>
        /// Seeds the selected engine once and returns a fresh context for one case.
        /// </summary>
        internal async Task<LikeContext> CreateContextAsync(
            IntegrationDatabaseTarget target
        )
        {
            // WHY: xUnit serializes this class's cases. Cache the seed task so
            // a failing engine is reported by its rows without retrying partial DDL.
            if (!_seeds.TryGetValue(target, out var seed))
            {
                seed = SeedAsync(target);
                _seeds.Add(target, seed);
            }

            await seed;

            return CreateContext(target);
        }

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            List<Exception>? failures = null;

            foreach (var target in _seeds.Keys)
            {
                try
                {
                    await using var context = CreateContext(target);
                    await context.Database.ExecuteSqlRawAsync($"DROP TABLE IF EXISTS `{TableName}`;");
                }
                catch (Exception exception)
                {
                    // WHY: One unavailable engine must not prevent cleanup of others.
                    (failures ??= []).Add(new InvalidOperationException(
                        $"Could not drop {TableName} on {target}.", exception));
                }
            }

            if (failures is not null)
            {
                throw new AggregateException("Scalar LIKE fixture cleanup failed.", failures);
            }
        }

        private static async Task SeedAsync(
            IntegrationDatabaseTarget target
        )
        {
            await using var context = CreateContext(target);
            await context.Database.ExecuteSqlRawAsync($"DROP TABLE IF EXISTS `{TableName}`;");
            await context.Database.ExecuteSqlRawAsync(context.Database.GenerateCreateScript());

            // WHY: Explicit seed keys need no asynchronous value generation;
            // SaveChangesAsync below owns the database work.
            // ReSharper disable once MethodHasAsyncOverload
            context.Items.AddRange(
                new LikeItem
                {
                    Id = 1,
                    ByteValue = 123,
                    SByteValue = -123,
                    Int16Value = -123,
                    UInt16Value = 123,
                    Int32Value = -123,
                    UInt32Value = 123,
                    Int64Value = -123,
                    UInt64Value = 123,
                    SingleValue = -123.5F,
                    DoubleValue = -123.5,
                    DecimalValue = -123.5M,
                    OptionalByteValue = 123,
                    OptionalSByteValue = -123,
                    OptionalInt16Value = -123,
                    OptionalUInt16Value = 123,
                    OptionalInt32Value = -123,
                    OptionalUInt32Value = 123,
                    OptionalInt64Value = -123,
                    OptionalUInt64Value = 123,
                    OptionalSingleValue = -123.5F,
                    OptionalDoubleValue = -123.5,
                    OptionalDecimalValue = -123.5M,
                    CreatedAt = new DateTime(2026, 8, 25, 12, 34, 56, 123),
                    OptionalCreatedAt = new DateTime(2026, 8, 25, 12, 34, 56, 123),
                    BinaryToken = s_matchingGuid,
                    TextToken = s_matchingGuid,
                    OptionalBinaryToken = s_matchingGuid,
                    OptionalTextToken = s_matchingGuid,
                    Text = "sale_25%!",
                    OptionalText = "sale_25%!",
                    Pattern = "00112233-%-aabbccddeeff",
                },
                new LikeItem
                {
                    Id = 2,
                    CreatedAt = new DateTime(2000, 1, 1),
                    Text = "different",
                });

            await context.SaveChangesAsync(CancellationToken.None);
        }

        /// <summary>
        /// Retains one source-file schema owner across all topic partials.
        /// </summary>
        private static LikeContext CreateContext(
            IntegrationDatabaseTarget target
        )
        {
            // WHY: Resolving the connection here keeps all partial files on the
            // same schema despite IntegrationTestEnvironment's CallerFilePath key.
            var builder = IntegrationTestDbContextOptions.Create<LikeContext>();
            builder.UseMySql(
                IntegrationTestEnvironment.GetConnectionString(target),
                IntegrationTestEnvironment.GetServerVersion(target));

            return new LikeContext(builder.Options);
        }
    }

    /// <summary>
    /// Defines required and nullable scalar mappings for LIKE probes.
    /// </summary>
    internal sealed class LikeContext : DbContext
    {
        /// <summary>Uses engine-specific options owned by the fixture.</summary>
        public LikeContext(
            DbContextOptions<LikeContext> options
        ) : base(options) { }

        /// <summary>Provides the shared immutable scalar rows.</summary>
        public DbSet<LikeItem> Items => Set<LikeItem>();

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<LikeItem>(entity =>
            {
                entity.ToTable(TableName);
                entity.HasKey(item => item.Id);
                entity
                    .Property(item => item.Id)
                    .ValueGeneratedNever();
                entity
                    .Property(item => item.BinaryToken)
                    .HasMySqlGuidFormat(MySqlGuidFormat.Binary16);
                entity
                    .Property(item => item.OptionalBinaryToken)
                    .HasMySqlGuidFormat(MySqlGuidFormat.Binary16);
                entity
                    .Property(item => item.TextToken)
                    .HasMySqlGuidFormat(MySqlGuidFormat.Char36);
                entity
                    .Property(item => item.OptionalTextToken)
                    .HasMySqlGuidFormat(MySqlGuidFormat.Char36);
                entity
                    .Property(item => item.Text)
                    .HasMaxLength(100);
                entity
                    .Property(item => item.OptionalText)
                    .HasMaxLength(100);
                entity
                    .Property(item => item.Pattern)
                    .HasMaxLength(100);
            });
        }
    }

    internal sealed class LikeItem
    {
        public int Id { get; set; }
        public byte ByteValue { get; set; }
        public sbyte SByteValue { get; set; }
        public short Int16Value { get; set; }
        public ushort UInt16Value { get; set; }
        public int Int32Value { get; set; }
        public uint UInt32Value { get; set; }
        public long Int64Value { get; set; }
        public ulong UInt64Value { get; set; }
        public float SingleValue { get; set; }
        public double DoubleValue { get; set; }
        public decimal DecimalValue { get; set; }
        public byte? OptionalByteValue { get; set; }
        public sbyte? OptionalSByteValue { get; set; }
        public short? OptionalInt16Value { get; set; }
        public ushort? OptionalUInt16Value { get; set; }
        public int? OptionalInt32Value { get; set; }
        public uint? OptionalUInt32Value { get; set; }
        public long? OptionalInt64Value { get; set; }
        public ulong? OptionalUInt64Value { get; set; }
        public float? OptionalSingleValue { get; set; }
        public double? OptionalDoubleValue { get; set; }
        public decimal? OptionalDecimalValue { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? OptionalCreatedAt { get; set; }
        public Guid BinaryToken { get; set; }
        public Guid TextToken { get; set; }
        public Guid? OptionalBinaryToken { get; set; }
        public Guid? OptionalTextToken { get; set; }
        public string Text { get; set; } = string.Empty;
        public string? OptionalText { get; set; }
        public string Pattern { get; set; } = "not-a-match";
    }
}
