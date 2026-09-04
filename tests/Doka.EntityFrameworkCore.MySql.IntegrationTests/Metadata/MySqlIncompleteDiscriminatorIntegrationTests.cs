using System.Reflection;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>
/// Verifies complete and incomplete TPH discriminator mappings against live servers.
/// </summary>
[Collection(IntegrationDatabaseTestGroup.Name)]
public sealed class MySqlIncompleteDiscriminatorIntegrationTests
{
    private const string StringTableName = "IncompleteDiscriminatorEntities";
    private const string IntTableName = "IncompleteIntDiscriminatorEntities";
    private const string ConvertedTableName = "IncompleteConvertedDiscriminatorEntities";

    /// <summary>
    /// An incomplete mapping filters rows whose discriminator is unknown to the model.
    /// </summary>
    [RequiresDatabaseTargetFact(
        IntegrationDatabaseTarget.MySql84,
        IntegrationDatabaseTarget.MySql97,
        IntegrationDatabaseTarget.MariaDb1011,
        IntegrationDatabaseTarget.MariaDb114,
        IntegrationDatabaseTarget.MariaDb118,
        IntegrationDatabaseTarget.MariaDb123)]
    public async Task Incomplete_mapping_filters_unknown_discriminator_values()
    {
        foreach (var target in IntegrationTestEnvironment.GetSelectedTargets())
        {
            await AssertIncompleteMappingAsync(target)
                .ConfigureAwait(true);
        }
    }

    /// <summary>
    /// A complete mapping rejects a row whose discriminator is unknown to the model.
    /// </summary>
    [RequiresDatabaseTargetFact(
        IntegrationDatabaseTarget.MySql84,
        IntegrationDatabaseTarget.MySql97,
        IntegrationDatabaseTarget.MariaDb1011,
        IntegrationDatabaseTarget.MariaDb114,
        IntegrationDatabaseTarget.MariaDb118,
        IntegrationDatabaseTarget.MariaDb123)]
    public async Task Complete_mapping_rejects_unknown_discriminator_values()
    {
        foreach (var target in IntegrationTestEnvironment.GetSelectedTargets())
        {
            await AssertCompleteMappingAsync(target)
                .ConfigureAwait(true);
        }
    }

    /// <summary>
    /// An incomplete mapping filters unknown values for a non-string discriminator property.
    /// </summary>
    [RequiresDatabaseTargetFact(
        IntegrationDatabaseTarget.MySql84,
        IntegrationDatabaseTarget.MySql97,
        IntegrationDatabaseTarget.MariaDb1011,
        IntegrationDatabaseTarget.MariaDb114,
        IntegrationDatabaseTarget.MariaDb118,
        IntegrationDatabaseTarget.MariaDb123)]
    public async Task Incomplete_mapping_filters_unknown_int_discriminator_values()
    {
        foreach (var target in IntegrationTestEnvironment.GetSelectedTargets())
        {
            await AssertIncompleteIntMappingAsync(target)
                .ConfigureAwait(true);
        }
    }

    /// <summary>
    /// A complete mapping rejects an unknown value for a non-string discriminator property.
    /// </summary>
    [RequiresDatabaseTargetFact(
        IntegrationDatabaseTarget.MySql84,
        IntegrationDatabaseTarget.MySql97,
        IntegrationDatabaseTarget.MariaDb1011,
        IntegrationDatabaseTarget.MariaDb114,
        IntegrationDatabaseTarget.MariaDb118,
        IntegrationDatabaseTarget.MariaDb123)]
    public async Task Complete_mapping_rejects_unknown_int_discriminator_values()
    {
        foreach (var target in IntegrationTestEnvironment.GetSelectedTargets())
        {
            await AssertCompleteIntMappingAsync(target)
                .ConfigureAwait(true);
        }
    }

    /// <summary>
    /// An incomplete mapping converts known values before filtering provider values.
    /// </summary>
    [RequiresDatabaseTargetFact(
        IntegrationDatabaseTarget.MySql84,
        IntegrationDatabaseTarget.MySql97,
        IntegrationDatabaseTarget.MariaDb1011,
        IntegrationDatabaseTarget.MariaDb114,
        IntegrationDatabaseTarget.MariaDb118,
        IntegrationDatabaseTarget.MariaDb123)]
    public async Task Incomplete_mapping_filters_unknown_converted_discriminator_values()
    {
        foreach (var target in IntegrationTestEnvironment.GetSelectedTargets())
        {
            await AssertIncompleteConvertedMappingAsync(target)
                .ConfigureAwait(true);
        }
    }

    /// <summary>
    /// A complete mapping converts an unknown provider value before rejecting it.
    /// </summary>
    [RequiresDatabaseTargetFact(
        IntegrationDatabaseTarget.MySql84,
        IntegrationDatabaseTarget.MySql97,
        IntegrationDatabaseTarget.MariaDb1011,
        IntegrationDatabaseTarget.MariaDb114,
        IntegrationDatabaseTarget.MariaDb118,
        IntegrationDatabaseTarget.MariaDb123)]
    public async Task Complete_mapping_rejects_unknown_converted_discriminator_values()
    {
        foreach (var target in IntegrationTestEnvironment.GetSelectedTargets())
        {
            await AssertCompleteConvertedMappingAsync(target)
                .ConfigureAwait(true);
        }
    }

    private static async Task AssertIncompleteMappingAsync(
        IntegrationDatabaseTarget target
    )
    {
        await using var context = new IncompleteDiscriminatorContext(
            CreateOptions<IncompleteDiscriminatorContext>(target));

        await RecreateStringTableWithUnknownDiscriminatorAsync(context)
            .ConfigureAwait(false);

        try
        {
            var query = context.Entities
                .AsNoTracking()
                .OrderBy(entity => entity.Id);
            var sql = query.ToQueryString();

            Assert.Contains("WHERE", sql, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Known", sql, StringComparison.Ordinal);

            var syncEntities = query.ToList();
            var asyncEntities = await query
                .ToListAsync(CancellationToken.None)
                .ConfigureAwait(false);

            AssertKnownStringEntity(syncEntities);
            AssertKnownStringEntity(asyncEntities);
        }
        finally
        {
            await DropTableAsync(context, StringTableName)
                .ConfigureAwait(false);
        }
    }

    private static async Task AssertCompleteMappingAsync(
        IntegrationDatabaseTarget target
    )
    {
        await using var context = new CompleteDiscriminatorContext(
            CreateOptions<CompleteDiscriminatorContext>(target));

        await RecreateStringTableWithUnknownDiscriminatorAsync(context)
            .ConfigureAwait(false);

        try
        {
            var query = context.Entities
                .AsNoTracking()
                .OrderBy(entity => entity.Id);
            var sql = query.ToQueryString();

            Assert.DoesNotContain("WHERE", sql, StringComparison.OrdinalIgnoreCase);

            var syncException = Assert.Throws<InvalidOperationException>(() => query.ToList());
            var asyncException = await Assert.ThrowsAsync<InvalidOperationException>(
                    async () => await query
                        .ToListAsync(CancellationToken.None)
                        .ConfigureAwait(false))
                .ConfigureAwait(false);

            AssertUnknownDiscriminatorException(syncException, "Future");
            AssertUnknownDiscriminatorException(asyncException, "Future");
        }
        finally
        {
            await DropTableAsync(context, StringTableName)
                .ConfigureAwait(false);
        }
    }

    private static async Task AssertIncompleteIntMappingAsync(
        IntegrationDatabaseTarget target
    )
    {
        await using var context = new IncompleteIntDiscriminatorContext(
            CreateOptions<IncompleteIntDiscriminatorContext>(target));

        await RecreateIntTableWithUnknownDiscriminatorAsync(context)
            .ConfigureAwait(false);

        try
        {
            var query = context.Entities
                .AsNoTracking()
                .OrderBy(entity => entity.Id);
            var sql = query.ToQueryString();

            Assert.Contains("WHERE", sql, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(nameof(IntDiscriminatorEntity.Discriminator), sql, StringComparison.Ordinal);

            var syncEntities = query.ToList();
            var asyncEntities = await query
                .ToListAsync(CancellationToken.None)
                .ConfigureAwait(false);

            AssertKnownIntEntity(syncEntities);
            AssertKnownIntEntity(asyncEntities);
        }
        finally
        {
            await DropTableAsync(context, IntTableName)
                .ConfigureAwait(false);
        }
    }

    private static async Task AssertCompleteIntMappingAsync(
        IntegrationDatabaseTarget target
    )
    {
        await using var context = new CompleteIntDiscriminatorContext(
            CreateOptions<CompleteIntDiscriminatorContext>(target));

        await RecreateIntTableWithUnknownDiscriminatorAsync(context)
            .ConfigureAwait(false);

        try
        {
            var query = context.Entities
                .AsNoTracking()
                .OrderBy(entity => entity.Id);
            var sql = query.ToQueryString();

            Assert.DoesNotContain("WHERE", sql, StringComparison.OrdinalIgnoreCase);

            var syncException = Assert.Throws<InvalidOperationException>(() => query.ToList());
            var asyncException = await Assert.ThrowsAsync<InvalidOperationException>(
                    async () => await query
                        .ToListAsync(CancellationToken.None)
                        .ConfigureAwait(false))
                .ConfigureAwait(false);

            AssertUnknownDiscriminatorException(syncException, "2");
            AssertUnknownDiscriminatorException(asyncException, "2");
        }
        finally
        {
            await DropTableAsync(context, IntTableName)
                .ConfigureAwait(false);
        }
    }

    private static async Task AssertIncompleteConvertedMappingAsync(
        IntegrationDatabaseTarget target
    )
    {
        await using var context = new IncompleteConvertedDiscriminatorContext(
            CreateOptions<IncompleteConvertedDiscriminatorContext>(target));

        await RecreateConvertedTableWithUnknownDiscriminatorAsync(context)
            .ConfigureAwait(false);

        try
        {
            var query = context.Entities
                .AsNoTracking()
                .OrderBy(entity => entity.Id);
            var sql = query.ToQueryString();

            Assert.Contains("WHERE", sql, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("'K'", sql, StringComparison.Ordinal);

            var syncEntities = query.ToList();
            var asyncEntities = await query
                .ToListAsync(CancellationToken.None)
                .ConfigureAwait(false);

            AssertKnownConvertedEntity(syncEntities);
            AssertKnownConvertedEntity(asyncEntities);
        }
        finally
        {
            await DropTableAsync(context, ConvertedTableName)
                .ConfigureAwait(false);
        }
    }

    private static async Task AssertCompleteConvertedMappingAsync(
        IntegrationDatabaseTarget target
    )
    {
        await using var context = new CompleteConvertedDiscriminatorContext(
            CreateOptions<CompleteConvertedDiscriminatorContext>(target));

        await RecreateConvertedTableWithUnknownDiscriminatorAsync(context)
            .ConfigureAwait(false);

        try
        {
            var query = context.Entities
                .AsNoTracking()
                .OrderBy(entity => entity.Id);
            var sql = query.ToQueryString();

            Assert.DoesNotContain("WHERE", sql, StringComparison.OrdinalIgnoreCase);

            var syncException = Assert.Throws<InvalidOperationException>(() => query.ToList());
            var asyncException = await Assert.ThrowsAsync<InvalidOperationException>(
                    async () => await query
                        .ToListAsync(CancellationToken.None)
                        .ConfigureAwait(false))
                .ConfigureAwait(false);

            AssertUnknownDiscriminatorException(syncException, nameof(ConvertedDiscriminator.Future));
            AssertUnknownDiscriminatorException(asyncException, nameof(ConvertedDiscriminator.Future));
        }
        finally
        {
            await DropTableAsync(context, ConvertedTableName)
                .ConfigureAwait(false);
        }
    }

    private static DbContextOptions<TContext> CreateOptions<TContext>(
        IntegrationDatabaseTarget target
    )
        where TContext : DbContext
    {
        var builder = IntegrationTestDbContextOptions.Create<TContext>();
        builder.UseMySql(
            IntegrationTestEnvironment.GetConnectionString(target),
            IntegrationTestEnvironment.GetServerVersion(target));

        return builder.Options;
    }

    private static void AssertKnownStringEntity(
        IReadOnlyCollection<DiscriminatorEntity> entities
    )
    {
        var entity = Assert.Single(entities);
        Assert.IsType<KnownDiscriminatorEntity>(entity);
        Assert.Equal(1, entity.Id);
    }

    private static void AssertKnownIntEntity(
        IReadOnlyCollection<IntDiscriminatorEntity> entities
    )
    {
        var entity = Assert.Single(entities);
        Assert.IsType<KnownIntDiscriminatorEntity>(entity);
        Assert.Equal(1, entity.Id);
    }

    private static void AssertKnownConvertedEntity(
        IReadOnlyCollection<ConvertedDiscriminatorEntity> entities
    )
    {
        var entity = Assert.Single(entities);
        Assert.IsType<KnownConvertedDiscriminatorEntity>(entity);
        Assert.Equal(1, entity.Id);
        Assert.Equal(ConvertedDiscriminator.Known, entity.Discriminator);
    }

    private static void AssertUnknownDiscriminatorException(
        InvalidOperationException exception,
        string unknownValue
    )
    {
        Assert.Contains("discriminator", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(unknownValue, exception.Message, StringComparison.Ordinal);
    }

    private static async Task RecreateStringTableWithUnknownDiscriminatorAsync(
        DbContext context
    )
    {
        await DropTableAsync(context, StringTableName)
            .ConfigureAwait(false);
        await context.Database.ExecuteSqlRawAsync(
                $"""
                CREATE TABLE `{StringTableName}` (
                    `Id` int NOT NULL,
                    `Discriminator` varchar(64) NOT NULL,
                    `Name` varchar(100) NOT NULL,
                    `KnownValue` varchar(100) NULL,
                    CONSTRAINT `PK_{StringTableName}` PRIMARY KEY (`Id`)
                ) CHARACTER SET utf8mb4;

                INSERT INTO `{StringTableName}` (`Id`, `Discriminator`, `Name`, `KnownValue`)
                VALUES
                    (1, 'Known', 'known', 'mapped'),
                    (2, 'Future', 'future', NULL);
                """,
                CancellationToken.None)
            .ConfigureAwait(false);
    }

    private static async Task RecreateIntTableWithUnknownDiscriminatorAsync(
        DbContext context
    )
    {
        await DropTableAsync(context, IntTableName)
            .ConfigureAwait(false);
        await context.Database.ExecuteSqlRawAsync(
                $"""
                CREATE TABLE `{IntTableName}` (
                    `Id` int NOT NULL,
                    `Discriminator` int NOT NULL,
                    `Name` varchar(100) NOT NULL,
                    `KnownValue` varchar(100) NULL,
                    CONSTRAINT `PK_{IntTableName}` PRIMARY KEY (`Id`)
                ) CHARACTER SET utf8mb4;

                INSERT INTO `{IntTableName}` (`Id`, `Discriminator`, `Name`, `KnownValue`)
                VALUES
                    (1, 1, 'known', 'mapped'),
                    (2, 2, 'future', NULL);
                """,
                CancellationToken.None)
            .ConfigureAwait(false);
    }

    private static async Task RecreateConvertedTableWithUnknownDiscriminatorAsync(
        DbContext context
    )
    {
        await DropTableAsync(context, ConvertedTableName)
            .ConfigureAwait(false);
        await context.Database.ExecuteSqlRawAsync(
                $"""
                CREATE TABLE `{ConvertedTableName}` (
                    `Id` int NOT NULL,
                    `Discriminator` char(1) NOT NULL,
                    `Name` varchar(100) NOT NULL,
                    `KnownValue` varchar(100) NULL,
                    CONSTRAINT `PK_{ConvertedTableName}` PRIMARY KEY (`Id`)
                ) CHARACTER SET utf8mb4;

                INSERT INTO `{ConvertedTableName}` (`Id`, `Discriminator`, `Name`, `KnownValue`)
                VALUES
                    (1, 'K', 'known', 'mapped'),
                    (2, 'F', 'future', NULL);
                """,
                CancellationToken.None)
            .ConfigureAwait(false);
    }

    private static async Task DropTableAsync(
        DbContext context,
        string tableName
    ) => await context.Database.ExecuteSqlRawAsync(
            $"DROP TABLE IF EXISTS `{tableName}`;",
            CancellationToken.None)
        .ConfigureAwait(false);

    private abstract class DiscriminatorContext(DbContextOptions options) : DbContext(options)
    {
        public DbSet<DiscriminatorEntity> Entities => Set<DiscriminatorEntity>();

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<DiscriminatorEntity>(entity =>
            {
                entity.ToTable(StringTableName);
                entity.HasKey(item => item.Id);
                entity
                    .Property(item => item.Name)
                    .HasMaxLength(100);
            });

            modelBuilder.Entity<KnownDiscriminatorEntity>(entity => entity
                .Property(item => item.KnownValue)
                .HasMaxLength(100));

            ConfigureDiscriminator(modelBuilder);
        }

        protected abstract void ConfigureDiscriminator(
            ModelBuilder modelBuilder
        );
    }

    private sealed class IncompleteDiscriminatorContext(
        DbContextOptions<IncompleteDiscriminatorContext> options
    ) : DiscriminatorContext(options)
    {
        protected override void ConfigureDiscriminator(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder
                .Entity<DiscriminatorEntity>()
                .HasDiscriminator<string>("Discriminator")
                .IsComplete(false);
            modelBuilder
                .Entity<KnownDiscriminatorEntity>()
                .Metadata.SetDiscriminatorValue("Known");
        }
    }

    private sealed class CompleteDiscriminatorContext(
        DbContextOptions<CompleteDiscriminatorContext> options
    ) : DiscriminatorContext(options)
    {
        protected override void ConfigureDiscriminator(
            ModelBuilder modelBuilder
        ) => modelBuilder
            .Entity<DiscriminatorEntity>()
            .HasDiscriminator()
            .HasValue<KnownDiscriminatorEntity>("Known")
            .IsComplete(true);
    }

    private abstract class IntDiscriminatorContext(DbContextOptions options) : DbContext(options)
    {
        public DbSet<IntDiscriminatorEntity> Entities => Set<IntDiscriminatorEntity>();

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<IntDiscriminatorEntity>(entity =>
            {
                entity.ToTable(IntTableName);
                entity.HasKey(item => item.Id);
                entity
                    .Property(item => item.Name)
                    .HasMaxLength(100);
            });

            modelBuilder.Entity<KnownIntDiscriminatorEntity>(entity => entity
                .Property(item => item.KnownValue)
                .HasMaxLength(100));

            ConfigureDiscriminator(modelBuilder);
        }

        protected abstract void ConfigureDiscriminator(
            ModelBuilder modelBuilder
        );
    }

    private sealed class IncompleteIntDiscriminatorContext(
        DbContextOptions<IncompleteIntDiscriminatorContext> options
    ) : IntDiscriminatorContext(options)
    {
        protected override void ConfigureDiscriminator(
            ModelBuilder modelBuilder
        ) => modelBuilder
            .Entity<IntDiscriminatorEntity>()
            .HasDiscriminator(entity => entity.Discriminator)
            .HasValue<KnownIntDiscriminatorEntity>(1)
            .IsComplete(false);
    }

    private sealed class CompleteIntDiscriminatorContext(
        DbContextOptions<CompleteIntDiscriminatorContext> options
    ) : IntDiscriminatorContext(options)
    {
        protected override void ConfigureDiscriminator(
            ModelBuilder modelBuilder
        ) => modelBuilder
            .Entity<IntDiscriminatorEntity>()
            .HasDiscriminator(entity => entity.Discriminator)
            .HasValue<KnownIntDiscriminatorEntity>(1)
            .IsComplete(true);
    }

    private abstract class ConvertedDiscriminatorContext(DbContextOptions options) : DbContext(options)
    {
        public DbSet<ConvertedDiscriminatorEntity> Entities => Set<ConvertedDiscriminatorEntity>();

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<ConvertedDiscriminatorEntity>(entity =>
            {
                entity.ToTable(ConvertedTableName);
                entity.HasKey(item => item.Id);
                entity
                    .Property(item => item.Name)
                    .HasMaxLength(100);
                entity
                    .Property(item => item.Discriminator)
                    .HasConversion<TestEnumValueConverter<ConvertedDiscriminator>>()
                    .HasMaxLength(1)
                    .IsFixedLength();
            });

            modelBuilder.Entity<KnownConvertedDiscriminatorEntity>(entity => entity
                .Property(item => item.KnownValue)
                .HasMaxLength(100));

            ConfigureDiscriminator(modelBuilder);
        }

        protected abstract void ConfigureDiscriminator(
            ModelBuilder modelBuilder
        );
    }

    private sealed class IncompleteConvertedDiscriminatorContext(
        DbContextOptions<IncompleteConvertedDiscriminatorContext> options
    ) : ConvertedDiscriminatorContext(options)
    {
        protected override void ConfigureDiscriminator(
            ModelBuilder modelBuilder
        ) => modelBuilder
            .Entity<ConvertedDiscriminatorEntity>()
            .HasDiscriminator(entity => entity.Discriminator)
            .HasValue<KnownConvertedDiscriminatorEntity>(ConvertedDiscriminator.Known)
            .IsComplete(false);
    }

    private sealed class CompleteConvertedDiscriminatorContext(
        DbContextOptions<CompleteConvertedDiscriminatorContext> options
    ) : ConvertedDiscriminatorContext(options)
    {
        protected override void ConfigureDiscriminator(
            ModelBuilder modelBuilder
        ) => modelBuilder
            .Entity<ConvertedDiscriminatorEntity>()
            .HasDiscriminator(entity => entity.Discriminator)
            .HasValue<KnownConvertedDiscriminatorEntity>(ConvertedDiscriminator.Known)
            .IsComplete(true);
    }

    private abstract class DiscriminatorEntity
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;
    }

    private sealed class KnownDiscriminatorEntity : DiscriminatorEntity
    {
        public string? KnownValue { get; set; }
    }

    private abstract class IntDiscriminatorEntity
    {
        public int Id { get; set; }

        public int Discriminator { get; set; }

        public string Name { get; set; } = string.Empty;
    }

    private sealed class KnownIntDiscriminatorEntity : IntDiscriminatorEntity
    {
        public string? KnownValue { get; set; }
    }

    private abstract class ConvertedDiscriminatorEntity
    {
        public int Id { get; set; }

        public ConvertedDiscriminator Discriminator { get; set; }

        public string Name { get; set; } = string.Empty;
    }

    private sealed class KnownConvertedDiscriminatorEntity : ConvertedDiscriminatorEntity
    {
        public string? KnownValue { get; set; }
    }

    private enum ConvertedDiscriminator
    {
        [JsonStringEnumMemberName("K")]
        Known,

        [EnumMember(Value = "F")]
        Future,
    }

    private sealed class TestEnumValueConverter<TEnum> : ValueConverter<TEnum, string>
        where TEnum : struct, Enum
    {
        public TestEnumValueConverter()
            : base(
                value => GetName(value),
                value => GetValue(value)) { }

        private static string GetName(
            TEnum value
        )
        {
            var member = typeof(TEnum)
                .GetMember(value.ToString())
                .Single();
            var jsonName = member.GetCustomAttribute<JsonStringEnumMemberNameAttribute>()?.Name;
            var enumMemberName = member.GetCustomAttribute<EnumMemberAttribute>()?.Value;

            return jsonName ?? enumMemberName ?? value.ToString();
        }

        private static TEnum GetValue(
            string value
        ) => Enum
            .GetValues<TEnum>()
            .Single(candidate => string.Equals(GetName(candidate), value, StringComparison.OrdinalIgnoreCase));
    }
}
