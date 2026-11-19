namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

public sealed partial class MySqlInsertSelectTranslationTests
{
    /// <summary>Lists independent unsupported mapping contracts without combining failures in one invocation.</summary>
    public static IEnumerable<TheoryDataRow<string>> UnsupportedTargetCases =>
    [
        new("tpt"), new("split"), new("view"), new("keyless"),
        new("nonwritable"), new("nontransactional"), new("graph"), new("guid-converter"),
    ];

    /// <summary>Valid EF models with unsupported direct-write targets fail before creating a native command.</summary>
    /// <param name="shape">The actual mapping contract to reject.</param>
    [Theory]
    [MemberData(nameof(UnsupportedTargetCases))]
    public async Task Unsupported_target_mapping_is_rejected_before_io(
        string shape
    )
    {
        // Arrange
        var observation = new InsertObservation();
        var options = MySqlFunctionalTestOptions
            .CreateTransientBuilder<TargetGuardContext>()
            .UseMySql("Server=localhost;Database=insert_target_guard;User ID=unused;",
                MySqlServerVersion.MariaDb(new Version(12, 3, 3)))
            .AddInterceptors(observation, new InsertCommandObservation(observation))
            .Options;

        await using var context = new TargetGuardContext(options, shape);
        _ = context.Model;
        var cancellationToken = TestContext.Current.CancellationToken;
        Func<Task<int>> operation = shape switch
        {
            "tpt" => () => context.Sources.ExecuteInsertFromQueryAsync(context.DerivedTargets,
                setters => setters
                    .SetProperty(row => row.Name, row => row.Name)
                    .SetProperty(row => row.Value, row => row.Value),
                cancellationToken),
            "graph" => () => context.Sources.ExecuteInsertFromQueryAsync(context.Targets,
                setters => setters
                    .SetProperty(row => row.Reference!.Name, row => row.Name)
                    .SetProperty(row => row.Value, row => row.Value),
                cancellationToken),
            "guid-converter" => () => context.Sources.ExecuteInsertFromQueryAsync(context.Targets,
                setters => setters
                    .SetProperty(row => row.Name, row => row.Name)
                    .SetProperty(row => row.Value, row => row.Value)
                    .SetProperty(row => row.Token, row => row.Token),
                cancellationToken),
            _ => () => context.Sources.ExecuteInsertFromQueryAsync(context.Targets,
                setters => setters
                    .SetProperty(row => row.Name, row => row.Name)
                    .SetProperty(row => row.Value, row => row.Value),
                cancellationToken),
        };

        var reason = shape switch
        {
            "tpt" or "split" => "multiple physical tables",
            "view" => "physical table mapping",
            "keyless" => "concrete keyed",
            "nonwritable" => "generated or read-only",
            "nontransactional" => "InnoDB",
            "graph" => "mapped scalar container",
            "guid-converter" => "GUID conversion requires",
            _ => throw new ArgumentOutOfRangeException(nameof(shape)),
        };

        // Act
        var actual = await Record.ExceptionAsync(operation);

        // Assert
        Assert.Contains(reason,
            Assert.IsType<InvalidOperationException>(actual)
                .Message,
            StringComparison.Ordinal);

        Assert.NotSame(observation.OpenFailure, actual);
        Assert.Equal(0, observation.OpenAttempts);
        Assert.Empty(observation.Commands);
    }

    /// <summary>Every null public argument has a stable independent guard before callbacks or database access.</summary>
    /// <param name="argument">The required argument deliberately omitted by this case.</param>
    [Theory]
    [InlineData("source")]
    [InlineData("target")]
    [InlineData("setters")]
    [InlineData("targetProperty")]
    [InlineData("sourceValue")]
    public async Task Null_argument_is_rejected_before_io(
        string argument
    )
    {
        // Arrange
        var observation = new InsertObservation();
        await using var context = CreateContext(mariaDb: true, observation);
        var cancellationToken = TestContext.Current.CancellationToken;
        var source = argument == "source"
            ? null!
            : context.Sources;

        var target = argument == "target"
            ? null!
            : context.Targets;

        Action<MySqlInsertSettersBuilder<InsertSource, InsertTarget>> setters = argument switch
        {
            "setters" => null!,
            "targetProperty" => builder => builder.SetProperty<string>(null!, row => row.Name),
            "sourceValue" => builder => builder.SetProperty<string>(row => row.Name, null!),
            _ => builder => builder.SetProperty(row => row.Name, row => row.Name),
        };

        // Act
        var actual =
            await Record.ExceptionAsync(() => source.ExecuteInsertFromQueryAsync(target, setters, cancellationToken));

        // Assert
        Assert.Equal(argument,
            Assert.IsType<ArgumentNullException>(actual)
                .ParamName);

        Assert.Equal(0, observation.OpenAttempts);
        Assert.Empty(observation.Commands);
    }

    /// <summary>Supplies independently valid model shapes to the real direct-write metadata guards.</summary>
    private sealed class TargetGuardContext : DbContext
    {
        private readonly string _shape;

        /// <summary>Initializes the context with its supplied options and model settings.</summary>
        public TargetGuardContext(
            DbContextOptions<TargetGuardContext> options,
            string shape
        ) : base(options)
        {
            _shape = shape;
        }

        public DbSet<GuardSource> Sources => Set<GuardSource>();
        public DbSet<GuardTarget> Targets => Set<GuardTarget>();
        public DbSet<DerivedGuardTarget> DerivedTargets => Set<DerivedGuardTarget>();

        /// <inheritdoc />
        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            var source = modelBuilder.Entity<GuardSource>();
            source.HasKey(row => row.Id);
            source
                .Property(row => row.Token)
                .HasConversion<byte[]>()
                .HasColumnType("binary(16)");

            var target = modelBuilder.Entity<GuardTarget>();
            if (_shape != "keyless")
            {
                target.HasKey(row => row.Id);
            }

            target
                .Property(row => row.Id)
                .UseMySqlAutoIncrementColumn();

            target
                .Property(row => row.Token)
                .HasColumnType("binary(16)")
                .HasDefaultValueSql("UNHEX(REPLACE(UUID(), '-', ''))");

            target
                .HasOne(row => row.Reference)
                .WithMany()
                .HasForeignKey(row => row.ReferenceId);

            if (_shape != "tpt")
            {
                modelBuilder.Ignore<DerivedGuardTarget>();
            }

            switch (_shape)
            {
                case "tpt":
                    target
                        .UseTptMappingStrategy()
                        .ToTable("GuardBase");

                    modelBuilder
                        .Entity<DerivedGuardTarget>()
                        .ToTable("GuardDerived");

                    break;

                case "split":
                    target
                        .ToTable("GuardMain")
                        .SplitToTable("GuardExtra", split => split.Property(row => row.Value));

                    break;

                case "view":
                    target.ToView("GuardView");
                    break;

                case "keyless":
                    target.HasNoKey();
                    break;

                case "nonwritable":
                    target
                        .Property(row => row.Value)
                        .Metadata
                        .SetBeforeSaveBehavior(PropertySaveBehavior.Throw);

                    break;

                case "nontransactional":
                    target.HasAnnotation(MySqlAnnotationNames.StorageEngine, "MyISAM");
                    break;
            }
        }
    }

    private sealed class GuardSource
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int Value { get; set; }
        public Guid Token { get; set; }
    }

    private class GuardTarget
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int Value { get; set; }
        public Guid Token { get; set; }
        public int? ReferenceId { get; set; }
        public GuardReference? Reference { get; set; }
    }

    private sealed class DerivedGuardTarget : GuardTarget
    {
        public int Detail { get; set; }
    }

    private sealed class GuardReference
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }
}
