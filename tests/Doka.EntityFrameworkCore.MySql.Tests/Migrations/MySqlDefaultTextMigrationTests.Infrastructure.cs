namespace Doka.EntityFrameworkCore.MySql.Tests;

public sealed partial class MySqlDefaultTextMigrationTests
{
    private const string UnicodeCollation = "utf8mb4_unicode_ci";
    private const string BinaryCollation = "utf8mb4_bin";
    private const string TableName = "DefaultTextRecords";

    /// <summary>Creates an isolated model without requiring an available database.</summary>
    private static DefaultTextMigrationContext CreateContext(
        TextShape? shape = null,
        string? defaultCharSet = "utf8mb4",
        string? defaultCollation = UnicodeCollation,
        bool mariaDb = false,
        Assembly? migrationsAssembly = null
    )
    {
        var options = new DbContextOptionsBuilder<DefaultTextMigrationContext>()
            .EnableServiceProviderCaching(false)

            // WHY: These offline comparisons deliberately own transient providers. EF still
            // checks the process-wide cached count; log only this expected isolation diagnostic.
            .ConfigureWarnings(warnings => warnings.Log(CoreEventId.ManyServiceProvidersCreatedWarning))
            .UseMySql(
                "Server=127.0.0.1;Port=1;Database=default_text_offline;User ID=unused;Pooling=false;",
                mariaDb
                    ? MySqlServerVersion.MariaDb(new Version(12, 3, 0))
                    : MySqlServerVersion.MySql(new Version(8, 4, 0)),
                provider =>
                {
                    provider.DefaultCharSet(defaultCharSet);
                    provider.DefaultCollation(defaultCollation);
                    provider.DefaultGuidFormat(MySqlGuidFormat.Char36);

                    if (migrationsAssembly is not null)
                    {
                        provider.MigrationsAssembly(migrationsAssembly);
                    }
                });

        return new DefaultTextMigrationContext(options.Options, shape ?? new TextShape());
    }

    /// <summary>Returns canonical design-time metadata, including annotations removed from runtime models.</summary>
    private static IModel DesignModel(
        DbContext context
    ) => context.GetService<IDesignTimeModel>()
        .Model;

    /// <summary>Captures one model comparison and its generated SQL without live I/O.</summary>
    private static TextDifference Compare(
        DefaultTextMigrationContext source,
        DefaultTextMigrationContext target
    )
    {
        var sourceModel = DesignModel(source)
            .GetRelationalModel();

        var targetModel = DesignModel(target)
            .GetRelationalModel();

        var differ = target.GetService<IMigrationsModelDiffer>();
        var operations = differ.GetDifferences(sourceModel, targetModel);
        var sql = GenerateSql(target, operations);

        return new TextDifference(differ.HasDifferences(sourceModel, targetModel), operations, sql);
    }

    /// <summary>Renders captured operations through the production migration SQL generator.</summary>
    private static string GenerateSql(
        DbContext context,
        IReadOnlyList<MigrationOperation> operations
    ) => string.Concat(context
        .GetService<IMigrationsSqlGenerator>()
        .Generate(operations, DesignModel(context))
        .Select(command => command.CommandText));

    private sealed record TextDifference(
        bool HasDifferences,
        IReadOnlyList<MigrationOperation> Operations,
        string Sql
    );

    /// <summary>Separates explicit column intent from physically inherited defaults in comparison fixtures.</summary>
    public sealed record TextShape
    {
        /// <summary>Whether model defaults come from the fluent model API instead of provider options.</summary>
        public bool ExplicitModel { get; init; }

        /// <summary>The table's independent default, if one is deliberately configured.</summary>
        public string? TableCollation { get; init; }

        /// <summary>Whether the selected property explicitly fixes its collation.</summary>
        public bool ColumnCollation { get; init; }

        /// <summary>Whether the selected property is a Char36 Guid rather than ordinary text.</summary>
        public bool GuidColumn { get; init; }

        /// <summary>The selected text column's maximum length.</summary>
        public int Length { get; init; } = 64;

        /// <summary>Whether the selected ordinary string column permits null.</summary>
        public bool Nullable { get; init; }

        /// <summary>The selected property's SQL default, if deliberately configured.</summary>
        public string? DefaultSql { get; init; }
    }

    /// <summary>Builds one small model for real differ and generated-code tests.</summary>
    public sealed class DefaultTextMigrationContext : DbContext
    {
        private readonly TextShape _shape;

        /// <summary>Creates the fixture context with explicit model intent.</summary>
        /// <param name="options">The isolated provider configuration.</param>
        /// <param name="shape">The independently varied model facets.</param>
        public DefaultTextMigrationContext(
            DbContextOptions<DefaultTextMigrationContext> options,
            TextShape shape
        ) : base(options)
        {
            _shape = shape;
        }

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            if (_shape.ExplicitModel)
            {
                modelBuilder.HasCharSet("utf8mb4");
                modelBuilder.UseCollation(UnicodeCollation);
            }

            var entity = modelBuilder.Entity<TextRecord>();
            entity.ToTable(TableName);
            entity.HasKey(row => row.Id);
            entity
                .Property(row => row.Value)
                .HasMaxLength(_shape.Length)
                .IsRequired(!_shape.Nullable);

            entity
                .Property(row => row.ExternalId)
                .HasMySqlGuidFormat(MySqlGuidFormat.Char36);

            if (_shape.TableCollation is not null)
            {
                entity.Metadata.SetAnnotation(RelationalAnnotationNames.Collation, _shape.TableCollation);
            }

            var selected = _shape.GuidColumn
                ? entity.Property(row => row.ExternalId)
                    .Metadata
                : entity.Property(row => row.Value)
                    .Metadata;

            if (_shape.ColumnCollation)
            {
                selected.SetCollation(UnicodeCollation);
            }

            if (_shape.DefaultSql is not null)
            {
                selected.SetDefaultValueSql(_shape.DefaultSql);
            }
        }
    }

    private sealed class TextRecord
    {
        public int Id { get; set; }

        public string? Value { get; set; }

        public Guid ExternalId { get; set; }
    }
}
