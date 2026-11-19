namespace Doka.EntityFrameworkCore.MySql.Tests;

public sealed partial class MySqlUpsertTests
{
    /// <summary>A client-only temporary key cannot be persisted as a permanent conflict identity.</summary>
    [Fact]
    public void Temporary_client_only_key_rejects_without_clearing_its_temporary_state()
    {
        // Arrange
        var capture = new Opens();

        // WHY: OnAdd reserves EF's temporary-value slot, while HiLo has no native
        // default. A ValueGenerated.Never property cannot be marked temporary at all.
        using var context = CreateContext(capture, "hilo");
        var input = new Row
        {
            Id = 1,
            Name = "value",
        };

        var entry = context.Attach(input);
        entry.Property(row => row.Id)
            .IsTemporary = true;

        var shape = CreateShape(context, nameof(Row.Name));
        using var parameterFactory = new MySqlConnection().CreateCommand();
        var next = 0;
        MySqlUpsertCommandBuilder.BoundRow? pending = null;

        // Act
        var error = Record.Exception(() => shape.BindNextBatch(context,
            parameterFactory,
            new[] { input },
            ref next,
            ref pending,
            CancellationToken.None));

        // Assert
        Assert.Contains("permanent values",
            Assert.IsType<InvalidOperationException>(error)
                .Message,
            StringComparison.Ordinal);

        Assert.True(entry.Property(row => row.Id)
            .IsTemporary);

        Assert.Equal(1, input.Id);
        Assert.Equal(0, capture.Count);
    }

    /// <summary>Unresolved required shadow values are rejected rather than invented as DBNull.</summary>
    [Fact]
    public async Task Required_shadow_property_rejects_before_open()
    {
        // Arrange
        var capture = new Opens();
        await using var context = CreateContext(capture, "shadow-required");

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
        Assert.Contains("explicit CLR value",
            Assert.IsType<InvalidOperationException>(error)
                .Message,
            StringComparison.Ordinal);

        Assert.Equal(0, capture.Count);
    }

    /// <summary>Read-only update behavior remains effective outside SaveChanges.</summary>
    [Fact]
    public async Task After_save_throw_property_cannot_enter_the_update_selection()
    {
        // Arrange
        var capture = new Opens();
        await using var context = CreateContext(capture, "readonly");

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
        Assert.Contains("read-only",
            Assert.IsType<ArgumentException>(error)
                .Message,
            StringComparison.Ordinal);

        Assert.Equal(0, capture.Count);
    }

    /// <summary>An explicit immutable input value is not silently written by the direct command.</summary>
    [Fact]
    public async Task Before_save_throw_property_rejects_an_explicit_input_value()
    {
        // Arrange
        var capture = new Opens();
        await using var context = CreateContext(capture, "before-throw");

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
        Assert.Contains("before-save behavior forbids",
            Assert.IsType<InvalidOperationException>(error)
                .Message,
            StringComparison.Ordinal);

        Assert.Equal(0, capture.Count);
    }

    /// <summary>A computed column with explicit immutable-input semantics is checked even though its column is omitted.</summary>
    [Fact]
    public async Task Computed_before_save_throw_rejects_an_explicit_value_without_writing_it()
    {
        // Arrange
        var capture = new Opens();
        await using var context = CreateContext(capture, "computed-throw");

        // Act
        var error = await Record.ExceptionAsync(() => context.ExecuteUpsertAsync(
            new Row
            {
                Id = 1,
                Name = "value",
                Computed = 17,
            },
            new[] { nameof(Row.Name) },
            CancellationToken.None));

        // Assert
        Assert.Contains("computed property",
            Assert.IsType<InvalidOperationException>(error)
                .Message,
            StringComparison.Ordinal);

        Assert.Equal(0, capture.Count);
    }

    /// <summary>Tracked temporary identity values select a native default without modifying the entry's temporary state.</summary>
    [Fact]
    public void Tracked_temporary_identity_is_not_bound_as_a_permanent_key()
    {
        // Arrange
        using var context = CreateContext(new Opens(), "identity");
        var input = new Row { Name = "value" };
        context.Add(input);
        var entry = context.Entry(input);
        var key = entry.Property(row => row.Id);
        var wasTemporary = key.IsTemporary;
        var previousCurrent = key.CurrentValue;
        var previousClr = input.Id;
        var shape = CreateShape(context, nameof(Row.Name));
        using var parameterFactory = new MySqlConnection().CreateCommand();
        var next = 0;
        MySqlUpsertCommandBuilder.BoundRow? pending = null;

        // Act
        var batch = shape.BindNextBatch(context,
            parameterFactory,
            new[] { input },
            ref next,
            ref pending,
            CancellationToken.None);

        // Assert
        var parameters = Assert.Single(batch.Rows)
            .Parameters;

        Assert.Null(parameters[Array.FindIndex(context.Model.FindEntityType(typeof(Row))!
                .GetProperties()
                .Where(property => property.Name != nameof(Row.Computed))
                .ToArray(),
            property => property.Name == nameof(Row.Id))]);

        Assert.True(key.IsTemporary);
        Assert.True(wasTemporary);
        Assert.Equal(previousCurrent, key.CurrentValue);
        Assert.Equal(previousClr, input.Id);
        Assert.Equal(EntityState.Added, entry.State);
    }

    /// <summary>A qualified generated shadow property uses its existing tracked value without creating another entry.</summary>
    [Fact]
    public void Existing_tracked_shadow_default_value_is_bound_without_entry_creation()
    {
        // Arrange
        using var context = CreateContext(new Opens(), "shadow-default");
        var input = new Row
        {
            Id = 1,
            Name = "value",
        };

        context.Attach(input);
        context
            .Entry(input)
            .Property("ShadowDefault")
            .CurrentValue = "explicit-shadow";

        var shape = CreateShape(context, "ShadowDefault");
        using var parameterFactory = new MySqlConnection().CreateCommand();
        var next = 0;
        MySqlUpsertCommandBuilder.BoundRow? pending = null;

        // Act
        var batch = shape.BindNextBatch(context,
            parameterFactory,
            new[] { input },
            ref next,
            ref pending,
            CancellationToken.None);

        // Assert
        Assert.Contains(Assert.Single(batch.Rows)
                .Parameters,
            parameter => Equals(parameter?.Value, "explicit-shadow"));

        Assert.Single(context.ChangeTracker.Entries());
        Assert.Equal("explicit-shadow",
            context
                .Entry(input)
                .Property("ShadowDefault")
                .CurrentValue);
    }

    /// <summary>A supplied prepared runtime model keeps its exact physical metadata without rebuilding a context model.</summary>
    [Fact]
    public void Prepared_runtime_model_preserves_target_identity_and_server_defaults()
    {
        // Arrange
        using var original = CreateContext(new Opens());
        var model = original.Model;
        var capture = new Opens();
        using var context = new OfflineContext(new DbContextOptionsBuilder<OfflineContext>()
                .UseInternalServiceProvider(_services)
                .UseModel(model)
                .UseMySql("Server=localhost;Database=unused;User ID=root;Password=unused;",
                    MySqlServerVersion.MySql(new Version(8, 4, 13)))
                .AddInterceptors(capture)
                .Options,
            "supplied-only",
            null);

        var shape = CreateShape(context, nameof(Row.Name), nameof(Row.ServerText));
        using var parameterFactory = new MySqlConnection().CreateCommand();
        var next = 0;
        MySqlUpsertCommandBuilder.BoundRow? pending = null;

        // Act
        var batch = shape.BindNextBatch(context,
            parameterFactory,
            new[]
            {
                new Row
                {
                    Id = 1,
                    Name = "prepared",
                },
            },
            ref next,
            ref pending,
            CancellationToken.None);

        var command = shape.BuildCommand(batch,
            context.GetService<IRelationalCommandBuilderFactory>(),
            context.GetService<ISqlGenerationHelper>());

        // Assert
        Assert.Same(model, context.Model);
        Assert.Same(model, shape.Target.EntityType.Model);
        Assert.Contains("DEFAULT", command.CommandText, StringComparison.Ordinal);
        Assert.Contains("`__doka_incoming`.`ServerText`", command.CommandText, StringComparison.Ordinal);
        Assert.Equal(0, capture.Count);
    }
}
