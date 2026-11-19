namespace Doka.EntityFrameworkCore.MySql.Tests;

/// <summary>Verifies direct UPSERT guards and bounded mapped commands without opening a database connection.</summary>
public sealed partial class MySqlUpsertTests : IDisposable
{
    private static readonly string[] s_missingUpdateProperty = { "Missing" };
    private static readonly string[] s_injectedUpdateProperty = { "Name` = 1; DROP TABLE UpsertRows; --" };

    private readonly ServiceProvider _services = new ServiceCollection()
        .AddEntityFrameworkDokaMySql()
        .BuildServiceProvider();

    /// <summary>Reports independently invalid explicit update selections.</summary>
    public static TheoryData<string[]> InvalidUpdateSelections => new()
    {
        Array.Empty<string>(),
        s_missingUpdateProperty,
        new[] { nameof(Row.Name), nameof(Row.Name) },
        new[] { nameof(Row.Id) },
        new[] { nameof(Row.Computed) },
        s_injectedUpdateProperty,
    };

    /// <inheritdoc />
    public void Dispose()
    {
        _services.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>A valid empty range is a no-op without acquiring a physical connection.</summary>
    [Fact]
    public async Task Empty_range_returns_zero_without_opening_a_connection()
    {
        // Arrange
        var capture = new Opens();
        await using var context = CreateContext(capture);

        // Act
        var result =
            await context.ExecuteUpsertRangeAsync(Array.Empty<Row>(),
                new[] { nameof(Row.Name) },
                CancellationToken.None);

        // Assert
        Assert.Equal(0, result);
        Assert.Equal(0, capture.Count);
    }

    /// <summary>Cancellation remains observable even when no input rows need writing.</summary>
    [Fact]
    public async Task Cancelled_empty_range_is_not_reported_as_success()
    {
        // Arrange
        var capture = new Opens();
        await using var context = CreateContext(capture);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        // Act
        var error = await Record.ExceptionAsync(() => context.ExecuteUpsertRangeAsync(
            Array.Empty<Row>(),
            new[] { nameof(Row.Name) },
            cancellation.Token));

        // Assert
        Assert.Equal(cancellation.Token,
            Assert.IsType<OperationCanceledException>(error)
                .CancellationToken);

        Assert.Equal(0, capture.Count);
    }

    /// <summary>Invalid selections fail before opening, including a physically named or injected string.</summary>
    [Theory]
    [MemberData(nameof(InvalidUpdateSelections))]
    public async Task Invalid_update_selection_rejects_before_connection_open(
        string[] properties
    )
    {
        // Arrange
        var capture = new Opens();
        await using var context = CreateContext(capture);

        // Act
        var error = await Record.ExceptionAsync(() => context.ExecuteUpsertAsync(
            new Row
            {
                Id = 1,
                Name = "value",
            },
            properties,
            CancellationToken.None));

        // Assert
        Assert.IsType<ArgumentException>(error);
        Assert.Equal(0, capture.Count);
    }

    /// <summary>Null rows are not silently filtered from a range.</summary>
    [Fact]
    public async Task Null_range_element_rejects_before_connection_open()
    {
        // Arrange
        var capture = new Opens();
        await using var context = CreateContext(capture);
        Row[] rows = [new() { Id = 1 }, null!];

        // Act
        var error = await Record.ExceptionAsync(()
            => context.ExecuteUpsertRangeAsync(rows, new[] { nameof(Row.Name) }, CancellationToken.None));

        // Assert
        Assert.IsType<ArgumentNullException>(error);
        Assert.Equal(0, capture.Count);
    }

    /// <summary>One model does not turn heterogeneous concrete input types into first-row metadata.</summary>
    [Fact]
    public async Task Mixed_concrete_types_reject_before_connection_open()
    {
        // Arrange
        var capture = new Opens();
        await using var context = CreateContext(capture);
        object[] rows = [new Row { Id = 1 }, new OtherRow { Id = 2 }];

        // Act
        var error = await Record.ExceptionAsync(()
            => context.ExecuteUpsertRangeAsync(rows, new[] { nameof(Row.Name) }, CancellationToken.None));

        // Assert
        Assert.Contains("one concrete mapped entity type",
            Assert.IsType<InvalidOperationException>(error)
                .Message,
            StringComparison.Ordinal);

        Assert.Equal(0, capture.Count);
    }

    /// <summary>Model-only mapping guards do not defer their rejection until a database command.</summary>
    /// <param name="mode">The valid EF model shape outside the initial UPSERT contract.</param>
    [Theory]
    [InlineData("concurrency")]
    [InlineData("complex")]
    [InlineData("owned")]
    [InlineData("split")]
    [InlineData("shared")]
    [InlineData("nontransactional")]
    public async Task Unsupported_mapping_rejects_before_connection_open(
        string mode
    )
    {
        // Arrange
        var capture = new Opens();
        await using var context = CreateContext(capture, mode);
        _ = context.Model;

        // Act
        var error = await Record.ExceptionAsync(() => context.ExecuteUpsertAsync(
            new Row
            {
                Id = 1,
                Name = "value",
            },
            new[] { nameof(Row.Name) },
            CancellationToken.None));

        // Assert
        Assert.IsType<InvalidOperationException>(error);
        Assert.Equal(0, capture.Count);
    }

    /// <summary>A generated GUID without a server default is not generated or assigned by the direct operation.</summary>
    [Fact]
    public async Task Missing_client_generated_value_rejects_without_mutating_input()
    {
        // Arrange
        var capture = new Opens();
        await using var context = CreateContext(capture, "client-guid");
        var row = new Row
        {
            Id = 1,
            Name = "value",
        };

        // Act
        var error = await Record.ExceptionAsync(()
            => context.ExecuteUpsertAsync(row, new[] { nameof(Row.Name) }, CancellationToken.None));

        // Assert
        Assert.Contains("generate a missing permanent",
            Assert.IsType<InvalidOperationException>(error)
                .Message,
            StringComparison.Ordinal);

        Assert.Equal(Guid.Empty, row.ClientId);
        Assert.Empty(context.ChangeTracker.Entries());
        Assert.Equal(0, capture.Count);
    }

    /// <summary>The real runtime column metadata retains defaults and computed facets used by direct writes.</summary>
    [Fact]
    public void Runtime_physical_columns_preserve_generated_facets()
    {
        // Arrange
        using var context = CreateContext(new Opens());
        var entity = context.Model.FindEntityType(typeof(Row))!;
        var target = MySqlDirectWriteTarget.Resolve(entity);

        // Act
        var defaultColumn = target.RequireColumn(entity.FindProperty(nameof(Row.ServerText))!);
        var computedColumn = target.RequireColumn(entity.FindProperty(nameof(Row.Computed))!);

        // Assert
        Assert.Equal("'server-default'", defaultColumn.DefaultValueSql);
        Assert.Equal("`Id` + 1", computedColumn.ComputedColumnSql);
        Assert.Same(entity, target.EntityType);
        Assert.Equal("UpsertRows", target.Table.Name);
    }

    /// <summary>Ordinary command text stays parameterized while defaults reference the actual incoming candidate.</summary>
    /// <param name="mariaDb">Whether to select the VALUES dialect rather than MySQL row aliases.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Generated_command_uses_exact_dialect_and_mapped_parameters(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateContext(new Opens(), mariaDb: mariaDb);
        var shape = CreateShape(context, nameof(Row.Name), nameof(Row.ServerText));
        using var factory = new MySqlConnection().CreateCommand();
        Row[] rows =
        [
            new()
            {
                Id = 1,
                Name = "Hallo 'Test' \\ \"quoted\" \u263a",
            },
        ];

        var next = 0;
        MySqlUpsertCommandBuilder.BoundRow? pending = null;

        // Act
        var batch = shape.BindNextBatch(context, factory, rows, ref next, ref pending, CancellationToken.None);
        var command = shape.BuildCommand(batch,
            context.GetService<IRelationalCommandBuilderFactory>(),
            context.GetService<ISqlGenerationHelper>());

        // Assert
        var boundRow = Assert.Single(batch.Rows);
        Assert.False(batch.HasMore);
        Assert.Null(pending);
        Assert.Contains("DEFAULT", command.CommandText, StringComparison.Ordinal);
        Assert.DoesNotContain("'Test'", command.CommandText, StringComparison.Ordinal);
        Assert.DoesNotContain("Computed", command.CommandText, StringComparison.Ordinal);
        Assert.Contains(mariaDb
                ? "VALUES(`ServerText`)"
                : "`__doka_incoming`.`ServerText`",
            command.CommandText,
            StringComparison.Ordinal);

        Assert.Equal(batch.ParameterCount, command.Parameters.Count);
        Assert.Contains(boundRow
                .Parameters,
            parameter => Equals(parameter?.Value, rows[0].Name));
    }

    private static MySqlUpsertCommandBuilder CreateShape(
        OfflineContext context,
        params string[] updates
    ) => MySqlUpsertCommandBuilder.Create(
        context.Model.FindEntityType(typeof(Row))!,
        updates,
        context
            .GetService<IDbContextOptions>()
            .FindExtension<MySqlOptionsExtension>()!.ServerVersion!.Profile.Engine,
        context.GetService<ISqlGenerationHelper>());

    private OfflineContext CreateContext(
        Opens capture,
        string mode = "ordinary",
        bool mariaDb = false,
        ConversionCounter? conversion = null
    ) => new(new DbContextOptionsBuilder<OfflineContext>()

            // WHY: xUnit owns one test instance per case. Its explicit graph isolates each
            // model variant and avoids EF's global-count diagnostic even when caching is disabled.
            .UseInternalServiceProvider(_services)
            .UseMySql("Server=localhost;Database=unused;User ID=root;Password=unused;",
                mariaDb
                    ? MySqlServerVersion.MariaDb(new Version(12, 3, 3))
                    : MySqlServerVersion.MySql(new Version(8, 4, 13)))
            .AddInterceptors(capture)
            .Options,
        mode,
        conversion);

    private sealed class Opens : DbConnectionInterceptor
    {
        public int Count { get; private set; }

        public override ValueTask<InterceptionResult> ConnectionOpeningAsync(
            DbConnection connection,
            ConnectionEventData eventData,
            InterceptionResult result,
            CancellationToken cancellationToken = default
        )
        {
            Count++;

            throw new InvalidOperationException("The offline UPSERT test must not open a connection.");
        }
    }

    private sealed class OfflineContext : DbContext
    {
        private readonly ConversionCounter? _conversion;
        private readonly string _mode;

        /// <summary>Initializes the context with its supplied options and model settings.</summary>
        public OfflineContext(
            DbContextOptions<OfflineContext> options,
            string mode,
            ConversionCounter? conversion
        ) : base(options)
        {
            _mode = mode;
            _conversion = conversion;
        }

        protected override void OnModelCreating(
            ModelBuilder builder
        )
        {
            if (_mode == "supplied-only")
            {
                throw new InvalidOperationException("The prepared runtime model must be used without rebuilding it.");
            }

            var row = builder.Entity<Row>();
            row.ToTable("UpsertRows");
            row.HasKey(value => value.Id);
            row
                .Property(value => value.Id)
                .ValueGeneratedNever();

            row
                .Property(value => value.ClientId)
                .ValueGeneratedNever();

            row
                .Property(value => value.Name)
                .IsRequired();

            row
                .Property(value => value.ServerText)
                .HasDefaultValueSql("'server-default'");

            row
                .Property(value => value.Computed)
                .HasComputedColumnSql("`Id` + 1", stored: true);

            row.Ignore(value => value.Details);
            var other = builder.Entity<OtherRow>();
            other.HasKey(value => value.Id);
            other
                .Property(value => value.Id)
                .ValueGeneratedNever();

            switch (_mode)
            {
                case "parameter-limit":
                    for (var index = 0; index < 69; index++)
                    {
                        row
                            .Property<string>("Parameter" + index.ToString(CultureInfo.InvariantCulture))
                            .HasDefaultValueSql("'server-default'");
                    }

                    break;
                case "getter":
                    var counted = builder.Entity<CountingRow>();
                    counted.HasKey(value => value.Id);
                    counted
                        .Property(value => value.Id)
                        .ValueGeneratedNever();

                    counted
                        .Property(value => value.Name)
                        .UsePropertyAccessMode(PropertyAccessMode.Property);

                    break;
                case "concurrency":
                    row
                        .Property(value => value.Name)
                        .IsConcurrencyToken();

                    break;
                case "complex":
                    row
                        .ComplexProperty(value => value.Details)
                        .IsRequired();

                    break;
                case "owned":
                    row.OwnsOne(value => value.Details);
                    break;
                case "split":
                    row.SplitToTable("UpsertExtra", split => split.Property(value => value.Name));
                    break;
                case "shared":
                    other.ToTable("UpsertRows");
                    row
                        .HasOne<OtherRow>()
                        .WithOne()
                        .HasForeignKey<OtherRow>(value => value.Id);

                    break;
                case "nontransactional":
                    row.HasAnnotation(MySqlAnnotationNames.StorageEngine, "MyISAM");
                    break;
                case "client-guid":
                    row
                        .Property(value => value.ClientId)
                        .ValueGeneratedOnAdd();

                    break;
                case "converter":
                    var counter = _conversion
                        ?? throw new InvalidOperationException("The converter fixture requires its counter.");

                    row
                        .Property(value => value.Name)
                        .HasConversion(new ValueConverter<string, string>(
                            value => counter.Convert(value),
                            value => value));

                    break;
                case "sentinel":
                    row
                        .Property(value => value.ServerText)
                        .HasSentinel("missing");

                    break;
                case "readonly":
                    row
                        .Property(value => value.Name)
                        .Metadata
                        .SetAfterSaveBehavior(PropertySaveBehavior.Throw);

                    break;
                case "before-throw":
                    row
                        .Property(value => value.Name)
                        .Metadata
                        .SetBeforeSaveBehavior(PropertySaveBehavior.Throw);

                    break;
                case "computed-throw":
                    row
                        .Property(value => value.Computed)
                        .Metadata
                        .SetBeforeSaveBehavior(PropertySaveBehavior.Throw);

                    break;
                case "identity":
                    row
                        .Property(value => value.Id)
                        .ValueGeneratedOnAdd();

                    break;
                case "shadow-default":
                    row
                        .Property<string>("ShadowDefault")
                        .HasDefaultValueSql("'shadow-default'");

                    break;
                case "shadow-required":
                    row
                        .Property<string>("RequiredShadow")
                        .IsRequired();

                    break;
                case "hilo":
                    row
                        .Property(value => value.Id)
                        .UseHiLo("UpsertSequence");

                    break;
                case "stored-procedure":
                    other.InsertUsingStoredProcedure("UpsertOtherInsert",
                        procedure =>
                        {
                            procedure.HasParameter(value => value.Id);
                            procedure.HasParameter(value => value.Name);
                        });

                    break;
                case "tph":
                case "tpt":
                case "tpc":
                    var hierarchy = builder.Entity<HierarchyRow>();
                    hierarchy.HasKey(value => value.Id);
                    hierarchy
                        .Property(value => value.Id)
                        .ValueGeneratedNever();

                    hierarchy
                        .Property(value => value.Name)
                        .HasMaxLength(120);

                    hierarchy
                        .HasIndex(value => value.Name)
                        .IsUnique();

                    var derived = builder.Entity<DerivedHierarchyRow>();

                    if (_mode == "tph")
                    {
                        hierarchy
                            .HasDiscriminator<string>("Kind")
                            .HasValue<HierarchyRow>("base")
                            .HasValue<DerivedHierarchyRow>("derived");
                    }
                    else if (_mode == "tpt")
                    {
                        hierarchy.ToTable("UpsertHierarchyBase");
                        derived.ToTable("UpsertHierarchyDerived");
                    }
                    else
                    {
                        hierarchy
                            .UseTpcMappingStrategy()
                            .ToTable("UpsertHierarchyBase");

                        derived.ToTable("UpsertHierarchyDerived");
                    }

                    break;
            }
        }
    }

    private sealed class Row
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public Guid ClientId { get; set; }
        public string? ServerText { get; set; }
        public int Computed { get; set; }
        public Detail Details { get; set; } = new();
    }

    private sealed class CountingRow
    {
        private string _name = string.Empty;

        public int Id { get; set; }
        internal int ReadCount { get; private set; }

        public string Name
        {
            get
            {
                ReadCount++;

                return _name;
            }
            set => _name = value;
        }
    }

    private sealed class OtherRow
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    private class HierarchyRow
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    private sealed class DerivedHierarchyRow : HierarchyRow
    {
        public string Detail { get; set; } = string.Empty;
    }

    private sealed class Detail
    {
        public string Text { get; set; } = string.Empty;
    }

    private sealed class ConversionCounter
    {
        public int Count { get; private set; }

        public string Convert(
            string value
        )
        {
            Count++;

            return value;
        }
    }
}
