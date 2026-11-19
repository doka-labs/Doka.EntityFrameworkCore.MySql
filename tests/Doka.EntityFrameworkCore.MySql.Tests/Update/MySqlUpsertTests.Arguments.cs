namespace Doka.EntityFrameworkCore.MySql.Tests;

public sealed partial class MySqlUpsertTests
{
    /// <summary>Reports one invalid public argument and its exact parameter name per test execution.</summary>
    public static TheoryData<string, string> NullArguments => new()
    {
        { "context", "context" },
        { "entity", "entity" },
        { "range", "entities" },
        { "selection", "updatePropertyNames" },
    };

    /// <summary>Null public arguments retain precise local exceptions without creating database work.</summary>
    [Theory]
    [MemberData(nameof(NullArguments))]
    public async Task Null_public_argument_is_rejected_precisely(
        string argument,
        string parameter
    )
    {
        // Arrange
        var capture = new Opens();
        await using var context = CreateContext(capture);
        var input = new Row
        {
            Id = 1,
            Name = "value",
        };

        Func<Task<long>> invocation = argument switch
        {
            "context" => ()
                => MySqlDbContextExtensions.ExecuteUpsertAsync(null!,
                    input,
                    new[] { nameof(Row.Name) },
                    CancellationToken.None),
            "entity" => ()
                => context.ExecuteUpsertAsync<Row>(null!, new[] { nameof(Row.Name) }, CancellationToken.None),
            "range" => ()
                => context.ExecuteUpsertRangeAsync<Row>(null!, new[] { nameof(Row.Name) }, CancellationToken.None),
            "selection" => () => context.ExecuteUpsertAsync(input, null!, CancellationToken.None),
            _ => throw new ArgumentOutOfRangeException(nameof(argument)),
        };

        // Act
        var error = await Record.ExceptionAsync(invocation);

        // Assert
        Assert.Equal(parameter,
            Assert.IsType<ArgumentNullException>(error)
                .ParamName);

        Assert.Equal(0, capture.Count);
    }

    /// <summary>An unmapped late range element cannot cause the earlier valid element to be written.</summary>
    [Fact]
    public async Task Unmapped_late_input_type_rejects_before_any_binding_or_open()
    {
        // Arrange
        var capture = new Opens();
        await using var context = CreateContext(capture);
        object[] rows =
        [
            new Row
            {
                Id = 1,
                Name = "value",
            },
            new(),
        ];

        // Act
        var error = await Record.ExceptionAsync(()
            => context.ExecuteUpsertRangeAsync(rows, new[] { nameof(Row.Name) }, CancellationToken.None));

        // Assert
        Assert.Contains("not mapped",
            Assert.IsType<InvalidOperationException>(error)
                .Message,
            StringComparison.Ordinal);

        Assert.Equal(0, capture.Count);
    }

    /// <summary>Absent Doka options reject before a foreign provider's relational services or model can be consulted.</summary>
    [Fact]
    public async Task Foreign_options_reject_before_any_relational_service_is_requested()
    {
        // Arrange
        var services = new ForeignServices();
        await using var context = new ForeignContext(services);

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
        Assert.Contains("requires a Doka",
            Assert.IsType<InvalidOperationException>(error)
                .Message,
            StringComparison.Ordinal);

        Assert.Equal(1, services.OptionsRequests);
        Assert.Equal(0, services.RelationalRequests);
    }

    /// <summary>The chosen initial inheritance guard applies before a same-key cross-discriminator candidate can write anything.</summary>
    /// <param name="strategy">The actual valid EF inheritance mapping strategy.</param>
    [Theory]
    [InlineData("tph")]
    [InlineData("tpt")]
    [InlineData("tpc")]
    public async Task Inheritance_mapping_rejects_before_any_range_candidate_is_written(
        string strategy
    )
    {
        // Arrange
        var capture = new Opens();
        await using var context = CreateContext(capture, strategy);
        _ = context.Model;
        DerivedHierarchyRow[] rows =
        [
            new()
            {
                Id = 1,
                Name = "same-unique-value",
            },
            new()
            {
                Id = 2,
                Name = "same-unique-value",
            },
        ];

        // Act
        var error = await Record.ExceptionAsync(()
            => context.ExecuteUpsertRangeAsync(rows, new[] { nameof(HierarchyRow.Name) }, CancellationToken.None));

        // Assert
        Assert.IsType<InvalidOperationException>(error);
        Assert.Equal(0, capture.Count);
    }

    /// <summary>A declared write procedure is not silently bypassed with a direct table statement.</summary>
    [Fact]
    public async Task Stored_procedure_mapping_rejects_before_open()
    {
        // Arrange
        var capture = new Opens();
        await using var context = CreateContext(capture, "stored-procedure");
        _ = context.Model;

        // Act
        var error = await Record.ExceptionAsync(() => context.ExecuteUpsertAsync(
            new OtherRow
            {
                Id = 1,
                Name = "value",
            },
            new[] { nameof(OtherRow.Name) },
            CancellationToken.None));

        // Assert
        Assert.Contains("stored-procedure",
            Assert.IsType<InvalidOperationException>(error)
                .Message,
            StringComparison.Ordinal);

        Assert.Equal(0, capture.Count);
    }

    /// <summary>A missing HiLo value does not invoke its client-side sequence allocation.</summary>
    [Fact]
    public async Task Missing_hilo_value_rejects_without_sequence_command_or_input_mutation()
    {
        // Arrange
        var capture = new Opens();
        await using var context = CreateContext(capture, "hilo");
        var input = new Row { Name = "value" };

        // Act
        var error = await Record.ExceptionAsync(()
            => context.ExecuteUpsertAsync(input, new[] { nameof(Row.Name) }, CancellationToken.None));

        // Assert
        Assert.Contains("missing permanent",
            Assert.IsType<InvalidOperationException>(error)
                .Message,
            StringComparison.Ordinal);

        Assert.Equal(0, input.Id);
        Assert.Empty(context.ChangeTracker.Entries());
        Assert.Equal(0, capture.Count);
    }

    /// <summary>Older MySQL cannot fall back to the deprecated incoming VALUES syntax when row aliases are unavailable.</summary>
    [Fact]
    public void Unsupported_row_alias_version_rejects_the_write_shape_before_binding()
    {
        // Arrange
        using var context = CreateContext(new Opens());
        var entity = context.Model.FindEntityType(typeof(Row))!;
        var engine = EngineProfileTable.Resolve(EngineFamily.MySql, new Version(8, 0, 18));

        // Act
        var error = Record.Exception(() => MySqlUpsertCommandBuilder.Create(entity,
            new[] { nameof(Row.Name) },
            engine,
            context.GetService<ISqlGenerationHelper>()));

        // Assert
        Assert.Contains("input-reference syntax",
            Assert.IsType<InvalidOperationException>(error)
                .Message,
            StringComparison.Ordinal);
    }

    private sealed class ForeignContext : DbContext, IInfrastructure<IServiceProvider>
    {
        private readonly ForeignServices _services;

        /// <summary>Initializes the context with its supplied options and model settings.</summary>
        public ForeignContext(
            ForeignServices services
        )
        {
            _services = services;
        }

        IServiceProvider IInfrastructure<IServiceProvider>.Instance => _services;
    }

    private sealed class ForeignServices : IServiceProvider
    {
        private readonly IDbContextOptions _options = new DbContextOptionsBuilder().Options;
        internal int OptionsRequests { get; private set; }
        internal int RelationalRequests { get; private set; }

        public object? GetService(
            Type serviceType
        )
        {
            if (serviceType == typeof(IDbContextOptions))
            {
                OptionsRequests++;

                return _options;
            }

            RelationalRequests++;

            throw new InvalidOperationException("A foreign provider's services must not be used for a Doka UPSERT.");
        }
    }
}
