namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlInsertSelectIntegrationTests
{
    /// <summary>A historical source retains its recorded read lineage while insertion writes only the plain archive target.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Temporal_as_of_source_inserts_the_recorded_historical_value(
        string target
    )
    {
        // Arrange
        using var safety = CreateSafetyCancellation();
        await using var store = await InsertStore.CreateAsync(target, safety.Token);
        var options = IntegrationTestDbContextOptions
            .Create<TemporalInsertContext>()
            .UseMySql(store.ConnectionString, store.Version)
            .Options;

        await using var setup = new TemporalInsertContext(options);
        var model = setup.GetService<IDesignTimeModel>()
            .Model;

        var operations = setup
            .GetService<IMigrationsModelDiffer>()
            .GetDifferences(null, model.GetRelationalModel());

        // WHY: Existing temporal fixtures execute generated migration commands individually so native versioning/trigger boundaries stay intact.
        foreach (var command in setup
            .GetService<IMigrationsSqlGenerator>()
            .Generate(operations, model))
        {
            await setup.Database.ExecuteSqlRawAsync(command.CommandText, safety.Token);
        }

        var entity = new TemporalInsertSource
        {
            Id = 1,
            Value = 10,
        };

        setup.Add(entity);
        await setup.SaveChangesAsync(safety.Token);
        entity.Value = 20;
        await setup.SaveChangesAsync(safety.Token);
        var period = await setup
            .Set<TemporalInsertSource>()
            .TemporalAll()
            .Where(row => row.Value == 10)
            .Select(row => new
            {
                From = EF.Property<DateTime>(row, "ValidFrom"),
                To = EF.Property<DateTime>(row, "ValidTo"),
            })
            .SingleAsync(safety.Token);

        if (period.From >= period.To)
        {
            throw new InvalidOperationException("The recorded historical fixture interval must be nonempty.");
        }

        var instant = new DateTime(period.From.Ticks + (period.To.Ticks - period.From.Ticks) / 2, DateTimeKind.Utc);
        var commands = new InsertCommands();
        await using var context = new TemporalInsertContext(new DbContextOptionsBuilder<TemporalInsertContext>(options)
            .AddInterceptors(commands)
            .Options);

        // Act
        var count = await context
            .Set<TemporalInsertSource>()
            .TemporalAsOf(instant)
            .ExecuteInsertFromQueryAsync(context.Set<TemporalArchive>(),
                setters => setters
                    .SetProperty(row => row.Value, row => row.Value),
                safety.Token);

        var actual = await ReadNativeAsync(store.Observer,
            "SELECT `Value`,`Label` FROM InsertTemporalArchive;",
            safety.Token);

        // Assert
        Assert.Equal(1, count);
        Assert.Equal(["10|history"], actual);
        Assert.Single(commands.Statements);
        Assert.Equal(0, commands.Readers);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    private sealed class TemporalInsertContext : DbContext
    {
        /// <summary>Initializes the context with its supplied options and model settings.</summary>
        public TemporalInsertContext(
            DbContextOptions<TemporalInsertContext> options
        ) : base(options) { }

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            modelBuilder.Entity<TemporalInsertSource>(entity =>
            {
                entity.ToTable("InsertTemporalSource",
                    table => table.IsTemporal(temporal =>
                    {
                        temporal.UseHistoryTable("InsertTemporalHistory");
                        temporal.HasPeriodStart("ValidFrom");
                        temporal.HasPeriodEnd("ValidTo");
                    }));

                entity
                    .Property(row => row.Id)
                    .ValueGeneratedNever();
            });

            modelBuilder.Entity<TemporalArchive>(entity =>
            {
                entity
                    .ToTable("InsertTemporalArchive")
                    .UseStorageEngine("InnoDB");

                entity
                    .Property(row => row.Label)
                    .HasMaxLength(24)
                    .HasDefaultValue("history");
            });
        }
    }

    private sealed class TemporalInsertSource
    {
        public int Id { get; set; }

        public int Value { get; set; }
    }

    private sealed class TemporalArchive
    {
        public int Id { get; set; }

        public int Value { get; set; }

        public string Label { get; set; } = string.Empty;
    }
}
