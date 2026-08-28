namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

/// <summary>
/// Verifies that EF Core scaffold defaults do not masquerade as application-owned data migrations.
/// </summary>
public sealed class MySqlMigrationBackfillContractTests
{
    /// <summary>
    /// Nullable-to-required scaffolding removes CLR defaults invented by EF Core
    /// while retaining a default explicitly configured in the target model.
    /// </summary>
    [Fact]
    public void Scaffolded_nullability_backfills_require_application_owned_values()
    {
        using var source = new OptionalBackfillContext(CreateOptions<OptionalBackfillContext>());
        using var target = new RequiredBackfillContext(CreateOptions<RequiredBackfillContext>());

        var operations = GetDifferences(source, target);
        var alters = operations
            .OfType<AlterColumnOperation>()
            .ToDictionary(operation => operation.Name, StringComparer.Ordinal);

        Assert.Null(alters["ImplicitCount"].DefaultValue);
        Assert.Null(alters["ImplicitOwnerId"].DefaultValue);
        Assert.Equal(typeof(Guid), alters["ImplicitOwnerId"].ClrType);
        Assert.Equal(typeof(System.Text.Json.JsonDocument), alters["ImplicitDocument"].ClrType);
        Assert.Equal(typeof(byte[]), alters["ImplicitVersion"].ClrType);
        Assert.Equal(7, alters["ExplicitCount"].DefaultValue);
        AssertRequiresExplicitBackfill(alters["ImplicitCount"]);
        AssertRequiresExplicitBackfill(alters["ImplicitOwnerId"]);
        AssertRequiresExplicitBackfill(alters["ImplicitDocument"]);
        AssertRequiresExplicitBackfill(alters["ImplicitVersion"]);
        Assert.Null(alters["ExplicitCount"].FindAnnotation(MySqlAnnotationNames.RequiresExplicitBackfill));

        AssertSqlGenerationRejected(target, alters["ImplicitCount"]);
        AssertSqlGenerationRejected(target, alters["ImplicitOwnerId"]);
        AssertSqlGenerationRejected(target, alters["ImplicitDocument"]);
        AssertSqlGenerationRejected(target, alters["ImplicitVersion"]);

        var explicitSql = JoinSql(GenerateSql(target, alters["ExplicitCount"]));

        Assert.Contains(
            "UPDATE `BackfillRows` SET `ExplicitCount` = 7 WHERE `ExplicitCount` IS NULL;",
            explicitSql,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Standalone required-column additions require an application backfill;
    /// nullable and explicitly defaulted additions retain normal EF behavior.
    /// </summary>
    [Fact]
    public void Scaffolded_required_column_additions_require_application_owned_values()
    {
        using var source = new AdditionSourceContext(CreateOptions<AdditionSourceContext>());
        using var target = new AdditionTargetContext(CreateOptions<AdditionTargetContext>());

        var operations = GetDifferences(source, target);
        var additions = operations
            .OfType<AddColumnOperation>()
            .ToDictionary(operation => operation.Name, StringComparer.Ordinal);

        Assert.Null(additions["ImplicitCount"].DefaultValue);
        Assert.Equal(7, additions["ExplicitCount"].DefaultValue);
        Assert.True(additions["OptionalCount"].IsNullable);
        Assert.Null(additions["OptionalCount"].DefaultValue);
        Assert.Equal(typeof(System.Text.Json.JsonDocument), additions["ImplicitDocument"].ClrType);
        Assert.Equal(typeof(byte[]), additions["GeneratedVersion"].ClrType);
        AssertRequiresExplicitBackfill(additions["ImplicitCount"]);
        AssertRequiresExplicitBackfill(additions["ImplicitDocument"]);
        AssertRequiresExplicitBackfill(additions["GeneratedVersion"]);
        Assert.Null(additions["ExplicitCount"].FindAnnotation(MySqlAnnotationNames.RequiresExplicitBackfill));
        Assert.Null(additions["OptionalCount"].FindAnnotation(MySqlAnnotationNames.RequiresExplicitBackfill));

        AssertSqlGenerationRejected(target, additions["ImplicitCount"]);
        AssertSqlGenerationRejected(target, additions["ImplicitDocument"]);
        Assert.Contains(
            "DEFAULT 7",
            JoinSql(GenerateSql(target, additions["ExplicitCount"])),
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "DEFAULT",
            JoinSql(GenerateSql(target, additions["OptionalCount"])),
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "`GeneratedVersion` timestamp(6)",
            JoinSql(GenerateSql(target, additions["GeneratedVersion"])),
            StringComparison.Ordinal);
    }

    private static void AssertRequiresExplicitBackfill(
        MigrationOperation operation
    ) => Assert.True(
        operation.FindAnnotation(MySqlAnnotationNames.RequiresExplicitBackfill)?.Value as bool?);

    private static void AssertSqlGenerationRejected(
        DbContext target,
        MigrationOperation operation
    )
    {
        var exception = Assert.Throws<InvalidOperationException>(() => GenerateSql(target, operation));

        Assert.Contains("explicit DefaultValue or DefaultValueSql", exception.Message, StringComparison.Ordinal);
        Assert.Contains("application contract", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("BackfillRows", exception.Message, StringComparison.Ordinal);
    }

    private static IReadOnlyList<MigrationCommand> GenerateSql(
        DbContext target,
        MigrationOperation operation
    ) => target
        .GetService<IMigrationsSqlGenerator>()
        .Generate(
            [operation],
            target.GetService<IDesignTimeModel>().Model);

    private static string JoinSql(
        IReadOnlyList<MigrationCommand> commands
    ) => string.Join("\n", commands.Select(command => command.CommandText));

    private static IReadOnlyList<MigrationOperation> GetDifferences(
        DbContext source,
        DbContext target
    ) => target
        .GetService<IMigrationsModelDiffer>()
        .GetDifferences(
            source.GetService<IDesignTimeModel>().Model.GetRelationalModel(),
            target.GetService<IDesignTimeModel>().Model.GetRelationalModel());

    private static DbContextOptions<TContext> CreateOptions<TContext>()
        where TContext : DbContext => MySqlFunctionalTestOptions.CreateTransientBuilder<TContext>().UseMySql(
            "Server=localhost;Database=doka;User ID=root;Password=password;",
            MySqlServerVersion.MySql(new Version(8, 4, 0)),
            options => options.DefaultGuidFormat(MySqlGuidFormat.Char36))
        .Options;

    private abstract class BackfillContext(DbContextOptions options) : DbContext(options)
    {
        protected abstract bool IsRequired { get; }

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.SharedTypeEntity<Dictionary<string, object>>("BackfillRow", entity =>
            {
                entity.ToTable("BackfillRows");
                entity.IndexerProperty<int>("Id");
                entity.HasKey("Id");

                if (IsRequired)
                {
                    entity.IndexerProperty<int>("ImplicitCount");
                    entity.IndexerProperty<int>("ExplicitCount").HasDefaultValue(7);
                    entity.IndexerProperty<Guid>("ImplicitOwnerId");
                }
                else
                {
                    entity.IndexerProperty<int?>("ImplicitCount");
                    entity.IndexerProperty<int?>("ExplicitCount");
                    entity.IndexerProperty<Guid?>("ImplicitOwnerId");
                }

                entity
                    .IndexerProperty<System.Text.Json.JsonDocument>("ImplicitDocument")
                    .IsRequired(IsRequired);
                entity
                    .IndexerProperty<byte[]>("ImplicitVersion")
                    .IsRowVersion()
                    .IsRequired(IsRequired);
            });
        }
    }

    private sealed class OptionalBackfillContext(
        DbContextOptions<OptionalBackfillContext> options
    ) : BackfillContext(options)
    {
        protected override bool IsRequired => false;
    }

    private sealed class RequiredBackfillContext(
        DbContextOptions<RequiredBackfillContext> options
    ) : BackfillContext(options)
    {
        protected override bool IsRequired => true;
    }

    private abstract class AdditionContext(DbContextOptions options) : DbContext(options)
    {
        protected abstract bool IncludeAdditions { get; }

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.SharedTypeEntity<Dictionary<string, object>>("BackfillRow", entity =>
            {
                entity.ToTable("BackfillRows");
                entity.IndexerProperty<int>("Id");
                entity.HasKey("Id");

                if (!IncludeAdditions)
                {
                    return;
                }

                entity.IndexerProperty<int>("ImplicitCount");
                entity.IndexerProperty<int>("ExplicitCount").HasDefaultValue(7);
                entity.IndexerProperty<int?>("OptionalCount");
                entity
                    .IndexerProperty<System.Text.Json.JsonDocument>("ImplicitDocument")
                    .IsRequired();
                entity
                    .IndexerProperty<byte[]>("GeneratedVersion")
                    .IsRowVersion()
                    .IsRequired();
            });
        }
    }

    private sealed class AdditionSourceContext(
        DbContextOptions<AdditionSourceContext> options
    ) : AdditionContext(options)
    {
        protected override bool IncludeAdditions => false;
    }

    private sealed class AdditionTargetContext(
        DbContextOptions<AdditionTargetContext> options
    ) : AdditionContext(options)
    {
        protected override bool IncludeAdditions => true;
    }
}
