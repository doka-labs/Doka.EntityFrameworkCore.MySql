namespace Doka.EntityFrameworkCore.MySql.Tests;

public sealed partial class MySqlUpsertTests
{
    /// <summary>A sufficiently wide real mapped shape reaches the parameter limit before the row limit.</summary>
    [Fact]
    public void Actual_bound_parameter_count_splits_before_the_protocol_parameter_limit()
    {
        // Arrange
        using var context = CreateContext(new Opens(), "parameter-limit");
        context.ChangeTracker.AutoDetectChangesEnabled = false;
        var rows = Enumerable
            .Range(1, 1000)
            .Select(id => new Row
            {
                Id = id,
                Name = "value",
            })
            .ToArray();

        foreach (var row in rows)
        {
            var entry = context.Attach(row);

            for (var index = 0; index < 69; index++)
            {
                entry.Property("Parameter" + index.ToString(CultureInfo.InvariantCulture))
                    .CurrentValue = "bound";
            }
        }

        var shape = CreateShape(context, nameof(Row.Name));
        using var parameterFactory = new MySqlConnection().CreateCommand();
        var next = 0;
        MySqlUpsertCommandBuilder.BoundRow? pending = null;

        // Act
        var first = shape.BindNextBatch(context, parameterFactory, rows, ref next, ref pending, CancellationToken.None);
        var second =
            shape.BindNextBatch(context, parameterFactory, rows, ref next, ref pending, CancellationToken.None);

        var firstCommand = shape.BuildCommand(first,
            context.GetService<IRelationalCommandBuilderFactory>(),
            context.GetService<ISqlGenerationHelper>());

        var secondCommand = shape.BuildCommand(second,
            context.GetService<IRelationalCommandBuilderFactory>(),
            context.GetService<ISqlGenerationHelper>());

        // Assert
        var parametersPerRow = first.Rows[0].ParameterCount;
        Assert.True(parametersPerRow
            > MySqlUpsertCommandBuilder.MaximumParameters / MySqlUpsertCommandBuilder.MaximumRows);

        Assert.Equal(MySqlUpsertCommandBuilder.MaximumParameters / parametersPerRow, first.Rows.Count);
        Assert.InRange(first.ParameterCount, 1, MySqlUpsertCommandBuilder.MaximumParameters);
        Assert.True(first.ParameterCount + parametersPerRow > MySqlUpsertCommandBuilder.MaximumParameters);
        Assert.Equal(rows.Length, first.Rows.Count + second.Rows.Count);
        Assert.Equal(first.ParameterCount, firstCommand.Parameters.Count);
        Assert.Equal(second.ParameterCount, secondCommand.Parameters.Count);
        Assert.Null(pending);
    }

    /// <summary>Sentinel detection and payload accounting reuse the value from the single mapped getter invocation.</summary>
    [Fact]
    public void Mapped_getter_is_called_once_per_candidate_property()
    {
        // Arrange
        using var context = CreateContext(new Opens(), "getter");
        var input = new CountingRow
        {
            Id = 1,
            Name = "counted",
        };

        var shape = MySqlUpsertCommandBuilder.Create(context.Model.FindEntityType(typeof(CountingRow))!,
            new[] { nameof(CountingRow.Name) },
            context
                .GetService<IDbContextOptions>()
                .FindExtension<MySqlOptionsExtension>()!.ServerVersion!.Profile.Engine,
            context.GetService<ISqlGenerationHelper>());

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
        Assert.Equal(1, input.ReadCount);
        Assert.Contains(Assert.Single(batch.Rows)
                .Parameters,
            parameter => Equals(parameter?.Value, "counted"));
    }

    /// <summary>A candidate carried across a row boundary is converted once and receives noncolliding final names.</summary>
    [Fact]
    public void Pending_row_is_not_reconverted_when_the_next_batch_consumes_it()
    {
        // Arrange
        var counter = new ConversionCounter();
        using var context = CreateContext(new Opens(), "converter", conversion: counter);
        var shape = CreateShape(context, nameof(Row.Name));
        using var parameterFactory = new MySqlConnection().CreateCommand();
        var rows = Enumerable
            .Range(1, MySqlUpsertCommandBuilder.MaximumRows + 1)
            .Select(id => new Row
            {
                Id = id,
                Name = "row-" + id.ToString(CultureInfo.InvariantCulture),
            })
            .ToArray();

        var next = 0;
        MySqlUpsertCommandBuilder.BoundRow? pending = null;

        // Act
        var first = shape.BindNextBatch(context, parameterFactory, rows, ref next, ref pending, CancellationToken.None);
        var firstCommand = shape.BuildCommand(first,
            context.GetService<IRelationalCommandBuilderFactory>(),
            context.GetService<ISqlGenerationHelper>());

        var second =
            shape.BindNextBatch(context, parameterFactory, rows, ref next, ref pending, CancellationToken.None);

        var secondCommand = shape.BuildCommand(second,
            context.GetService<IRelationalCommandBuilderFactory>(),
            context.GetService<ISqlGenerationHelper>());

        // Assert
        Assert.Equal(rows.Length, counter.Count);
        Assert.Equal(MySqlUpsertCommandBuilder.MaximumRows, first.Rows.Count);
        Assert.True(first.HasMore);
        var secondRow = Assert.Single(second.Rows);
        Assert.False(second.HasMore);
        Assert.Null(pending);
        Assert.Equal(first.ParameterCount,
            firstCommand
                .Parameters
                .Select(parameter => parameter.InvariantName)
                .Distinct(StringComparer.Ordinal)
                .Count());

        Assert.Equal(second.ParameterCount,
            secondCommand
                .Parameters
                .Select(parameter => parameter.InvariantName)
                .Distinct(StringComparer.Ordinal)
                .Count());

        Assert.Contains(secondRow
                .Parameters,
            parameter => parameter?.ParameterName == "p0");
    }

    /// <summary>Actual converted multibyte values, rather than an assumed per-parameter size, stop a batch.</summary>
    [Fact]
    public void Actual_utf8_payload_bounds_the_active_batch_and_one_pending_row()
    {
        // Arrange
        using var context = CreateContext(new Opens());
        var shape = CreateShape(context, nameof(Row.Name));
        using var parameterFactory = new MySqlConnection().CreateCommand();
        var payload = new string('\u4e2d', 100_000);
        var rows = Enumerable
            .Range(1, 20)
            .Select(id => new Row
            {
                Id = id,
                Name = payload,
            })
            .ToArray();

        var next = 0;
        MySqlUpsertCommandBuilder.BoundRow? pending = null;

        // Act
        var batch = shape.BindNextBatch(context, parameterFactory, rows, ref next, ref pending, CancellationToken.None);

        // Assert
        Assert.InRange(batch.Rows.Count, 1, 10);
        Assert.InRange(batch.PayloadBytes, 1, MySqlUpsertCommandBuilder.MaximumPayloadBytes);
        Assert.Equal(batch.Rows.Count + 1, next);
        Assert.NotNull(pending);
        Assert.True(batch.HasMore);
        Assert.True(pending.PayloadBytes >= 2L * Encoding.UTF8.GetByteCount(payload));
    }

    /// <summary>A single oversize candidate fails once instead of entering an unbounded retry/flush loop.</summary>
    [Fact]
    public void Oversize_single_row_rejects_without_opening_a_connection()
    {
        // Arrange
        var capture = new Opens();
        using var context = CreateContext(capture);
        var shape = CreateShape(context, nameof(Row.Name));
        using var parameterFactory = new MySqlConnection().CreateCommand();
        Row[] rows =
        [
            new()
            {
                Id = 1,
                Name = new string('x', checked((int)MySqlUpsertCommandBuilder.MaximumPayloadBytes)),
            },
        ];

        var next = 0;
        MySqlUpsertCommandBuilder.BoundRow? pending = null;

        // Act
        var error = Record.Exception(()
            => shape.BindNextBatch(context, parameterFactory, rows, ref next, ref pending, CancellationToken.None));

        // Assert
        Assert.Contains("One UPSERT candidate exceeds",
            Assert.IsType<InvalidOperationException>(error)
                .Message,
            StringComparison.Ordinal);

        Assert.Equal(1, next);
        Assert.Equal(0, capture.Count);
    }

    /// <summary>A custom sentinel invokes a server default, while an explicit null remains a parameter.</summary>
    /// <param name="value">The actual model value.</param>
    /// <param name="usesDefault">Whether EF's custom sentinel selects generation.</param>
    [Theory]
    [InlineData("missing", true)]
    [InlineData(null, false)]
    [InlineData("explicit", false)]
    public void Custom_sentinel_preserves_default_versus_explicit_null(
        string? value,
        bool usesDefault
    )
    {
        // Arrange
        using var context = CreateContext(new Opens(), "sentinel");
        var shape = CreateShape(context, nameof(Row.ServerText));
        using var parameterFactory = new MySqlConnection().CreateCommand();
        Row[] rows =
        [
            new()
            {
                Id = 1,
                ServerText = value,
            },
        ];

        var next = 0;
        MySqlUpsertCommandBuilder.BoundRow? pending = null;

        // Act
        var batch = shape.BindNextBatch(context, parameterFactory, rows, ref next, ref pending, CancellationToken.None);

        // Assert
        var row = Assert.Single(batch.Rows);
        var serverTextIndex = Array.FindIndex(context.Model.FindEntityType(typeof(Row))!
                .GetProperties()
                .Where(property => property.Name != nameof(Row.Computed))
                .ToArray(),
            property => property.Name == nameof(Row.ServerText));

        Assert.Equal(usesDefault, row.Parameters[serverTextIndex] is null);

        if (!usesDefault)
        {
            Assert.Equal(value is null
                    ? DBNull.Value
                    : value,
                row.Parameters[serverTextIndex]!.Value);
        }
    }

    /// <summary>Unsupported converted parameter representations fail without formatting or invoking arbitrary ToString.</summary>
    [Fact]
    public void Unsupported_parameter_representation_does_not_execute_user_formatting()
    {
        // Arrange
        var value = new ThrowingFormat();

        // Act
        var error = Record.Exception(() => MySqlUpsertCommandBuilder.EstimateParameterBytes(value));

        // Assert
        Assert.Contains("mapped provider parameter representation",
            Assert.IsType<InvalidOperationException>(error)
                .Message,
            StringComparison.Ordinal);

        Assert.False(value.Called);
    }

    private sealed class ThrowingFormat
    {
        public bool Called { get; private set; }

        public override string ToString()
        {
            Called = true;

            throw new InvalidOperationException("User formatting must not execute during payload validation.");
        }
    }
}
