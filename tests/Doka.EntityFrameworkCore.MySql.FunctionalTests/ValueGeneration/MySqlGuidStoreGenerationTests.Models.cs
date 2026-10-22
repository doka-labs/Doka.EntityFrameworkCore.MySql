using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.ValueGeneration;

namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

public sealed partial class MySqlGuidStoreGenerationTests
{
    private static readonly Guid s_factoryGuid = Guid.Parse("00112233-4455-4677-8899-aabbccddeeff");

    /// <summary>Uses the normal transient-provider policy to isolate configuration-specific models.</summary>
    private static StoreContext CreateStoreContext(
        string configuration,
        MySqlGuidFormat format = MySqlGuidFormat.Char36
    ) => new(
        MySqlFunctionalTestOptions.CreateTransientBuilder<StoreContext>()
            .UseMySql(
                "Server=localhost;Database=doka;User ID=root;Password=unused;",
                MySqlServerVersion.MariaDb(new Version(11, 8, 0)),
                mysql => mysql.DefaultGuidFormat(format))
            .Options,
        configuration,
        format);

    private static string DefaultSql(
        MySqlGuidFormat format
    ) => format == MySqlGuidFormat.Binary16 ? "UNHEX(REPLACE(UUID(), '-', ''))" : "UUID()";

    private sealed class StoreContext(
        DbContextOptions<StoreContext> options,
        string configuration,
        MySqlGuidFormat format
    ) : DbContext(options)
    {
        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            var entity = modelBuilder.Entity<StoreRow>();
            entity.HasKey(value => value.Id);
            entity.Ignore(value => value.Details);
            entity.Property(value => value.Source).HasMaxLength(36);
            var key = entity.Property(value => value.Id);

            switch (configuration)
            {
                case "ordinary":
                    break;
                case "literal":
                    key.HasDefaultValue(s_factoryGuid);
                    break;
                case "on-add":
                    key.ValueGeneratedOnAdd();
                    break;
                case "required-default":
                    entity.Property(value => value.Token).HasDefaultValueSql(DefaultSql(format));
                    break;
                case "nullable-default":
                    entity.Property(value => value.OptionalToken).HasDefaultValueSql(DefaultSql(format));
                    break;
                case "required-computed":
                    entity.Property(value => value.Token).HasComputedColumnSql("`Source`", stored: true);
                    break;
                case "nullable-computed":
                    entity.Property(value => value.OptionalToken).HasComputedColumnSql("`Source`", stored: true);
                    break;
                case "complex-default":
                    entity.ComplexProperty(value => value.Details)
                        .Property(value => value.Token).HasDefaultValueSql(DefaultSql(format));
                    break;
                case "converted-text":
                    key.HasConversion<string>().HasColumnType("varchar(36)").HasDefaultValueSql("UUID()");
                    break;
                case "converted-bytes":
                    key.HasConversion<byte[]>().HasDefaultValueSql("UNHEX(REPLACE(UUID(), '-', ''))");
                    break;
                default:
                    if (configuration.EndsWith("-before", StringComparison.Ordinal))
                    {
                        ApplyExplicitConfiguration(key, configuration);
                    }

                    key.HasDefaultValueSql(DefaultSql(format));

                    if (configuration.EndsWith("-after", StringComparison.Ordinal))
                    {
                        ApplyExplicitConfiguration(key, configuration);
                    }

                    break;
            }
        }
    }

    private static void ApplyExplicitConfiguration(
        PropertyBuilder<Guid> property,
        string configuration
    )
    {
        if (configuration.StartsWith("factory", StringComparison.Ordinal))
        {
            property.HasValueGenerator<FixedGuidGenerator>();

            if (configuration.StartsWith("factory-client", StringComparison.Ordinal))
            {
                property.UseMySqlClientGuidValueGeneration();
            }
        }
        else if (configuration.StartsWith("client", StringComparison.Ordinal))
        {
            property.UseMySqlClientGuidValueGeneration();
        }
        else if (configuration.StartsWith("strategy", StringComparison.Ordinal))
        {
            property.HasMySqlValueGenerationStrategy(MySqlValueGenerationStrategy.ClientGuid);
        }
        else if (configuration.StartsWith("never", StringComparison.Ordinal))
        {
            property.ValueGeneratedNever();
        }
        else if (configuration.StartsWith("none", StringComparison.Ordinal))
        {
            property.HasMySqlValueGenerationStrategy(MySqlValueGenerationStrategy.None);
        }
    }

    private sealed class FixedGuidGenerator : ValueGenerator<Guid>
    {
        /// <inheritdoc />
        public override bool GeneratesTemporaryValues => false;

        /// <inheritdoc />
        public override Guid Next(
            Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry entry
        ) => s_factoryGuid;
    }

    private sealed class StoreRow
    {
        public Guid Id { get; set; }

        public Guid Token { get; set; }

        public Guid? OptionalToken { get; set; }

        public string Source { get; set; } = s_factoryGuid.ToString("D", CultureInfo.InvariantCulture);

        public StoreDetails Details { get; set; } = new();
    }

    private sealed class StoreDetails
    {
        public Guid Token { get; set; }
    }
}
