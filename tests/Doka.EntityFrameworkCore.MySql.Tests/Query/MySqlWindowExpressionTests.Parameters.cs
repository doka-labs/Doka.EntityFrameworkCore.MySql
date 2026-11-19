namespace Doka.EntityFrameworkCore.MySql.Tests;

public sealed partial class MySqlWindowExpressionTests
{
    /// <summary>Supplies independent current-invocation controls for positive and nonnegative positions.</summary>
    public static TheoryData<bool, object?, bool> NumericParameterCases
    {
        get
        {
            var cases = new TheoryData<bool, object?, bool>();

            foreach (var positive in new[] { false, true })
            {
                cases.Add(positive, 1L, true);
                cases.Add(positive, 0L, !positive);
                cases.Add(positive, -1L, false);
                cases.Add(positive, 1.5m, false);
                cases.Add(positive, decimal.MaxValue, false);
                cases.Add(positive, null, false);
                cases.Add(positive, "1", false);
            }

            return cases;
        }
    }

    /// <summary>Cached metadata validates each execution's actual argument, including NULL and wrong runtime types.</summary>
    /// <param name="positive">Whether zero is forbidden by NTILE rather than permitted by LAG.</param>
    /// <param name="currentValue">The current dictionary value, independent of the cached template.</param>
    /// <param name="valid">Whether this invocation has a permitted integer.</param>
    [Theory]
    [MemberData(nameof(NumericParameterCases))]
    public void Parameter_policy_validates_current_values_without_rebuilding_the_template(
        bool positive,
        object? currentValue,
        bool valid
    )
    {
        // Arrange
        var policy = CreateNumericPolicy(positive);
        var parameters = new Dictionary<string, object?> { ["position"] = currentValue };

        // Act
        var exception = Record.Exception(() => policy.Validate(parameters));

        // Assert
        if (valid)
        {
            Assert.Null(exception);
        }
        else
        {
            Assert.IsType<ArgumentOutOfRangeException>(exception);
        }
    }

    /// <summary>Two parameterized bounds must be checked together after their current values are known.</summary>
    /// <param name="start">The preceding start distance.</param>
    /// <param name="end">The preceding end distance.</param>
    /// <param name="valid">Whether the inclusive start precedes the end.</param>
    [Theory]
    [InlineData(2, 1, true)]
    [InlineData(1, 2, false)]
    [InlineData(0, 1, false)]
    [InlineData(1, 1, true)]
    public void Frame_policy_validates_current_cross_bound_order(
        int start,
        int end,
        bool valid
    )
    {
        // Arrange
        var policy = CreateFramePolicy();
        var parameters = new Dictionary<string, object?>
        {
            ["start"] = (decimal)start,
            ["end"] = (decimal)end,
        };

        // Act
        var exception = Record.Exception(() => policy.Validate(parameters));

        // Assert
        if (valid)
        {
            Assert.Null(exception);
        }
        else
        {
            Assert.IsType<ArgumentOutOfRangeException>(exception);
        }
    }

    /// <summary>A cached policy fails before connection acquisition on each execution path.</summary>
    /// <param name="asynchronous">Whether to exercise the actual asynchronous command override.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Command_rejects_an_invalid_cached_argument_before_io(
        bool asynchronous
    )
    {
        // Arrange
        using var context = CreateContext();
        var connection = context.GetService<IRelationalConnection>();
        var command =
            new MySqlRelationalCommand(context.GetService<RelationalCommandBuilderDependencies>(),
                "SELECT 1",
                "SELECT 1",
                [])
            {
                WindowParameterConstraints = CreateNumericPolicy(positive: true),
            };

        var parameters = new RelationalCommandParameterObject(connection,
            new Dictionary<string, object?> { ["position"] = 0L },
            null,
            context,
            null);

        // Act
        var exception = asynchronous
            ? await Record.ExceptionAsync(()
                => command.ExecuteReaderAsync(parameters, CancellationToken.None))
            : Record.Exception(() => command.ExecuteReader(parameters));

        // Assert
        Assert.IsType<ArgumentOutOfRangeException>(exception);
        Assert.Equal(ConnectionState.Closed, connection.DbConnection.State);
        Assert.Null(connection.CurrentTransaction);
    }

    /// <summary>Renting a provider command copies or clears the window policy rather than retaining stale metadata.</summary>
    /// <param name="windowTemplate">Whether the incoming template carries a window policy.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Template_population_copies_or_clears_window_constraints(
        bool windowTemplate
    )
    {
        // Arrange
        using var context = CreateContext();
        var dependencies = context.GetService<RelationalCommandBuilderDependencies>();
        var prior = CreateNumericPolicy(positive: false);
        var current = windowTemplate
            ? CreateNumericPolicy(positive: true)
            : null;

        var command =
            new MySqlRelationalCommand(dependencies, "SELECT 1", "SELECT 1", []) { WindowParameterConstraints = prior };

        var template =
            new MySqlRelationalCommand(dependencies, "SELECT 2", "SELECT 2", [])
            {
                WindowParameterConstraints = current,
            };

        // Act
        command.PopulateFrom(template);

        // Assert
        Assert.Same(current, command.WindowParameterConstraints);
        Assert.Equal("SELECT 2", command.CommandText);
    }

    /// <summary>Constant calculations need no per-execution policy object.</summary>
    [Fact]
    public void Constant_window_creates_no_runtime_parameter_policy()
    {
        // Arrange
        var window = CreateWindow();
        MySqlWindowParameterConstraints.Builder? builder = null;
        var engine = EngineProfileTable.Resolve(EngineFamily.MySql, new Version(8, 4));

        // Act
        MySqlWindowParameterConstraints.Collect(window, engine, ref builder);

        // Assert
        Assert.Null(builder);
    }

    /// <summary>Unknown argument shapes cannot silently become engine-coerced offsets.</summary>
    [Fact]
    public void Row_dependent_offset_is_rejected_before_command_generation()
    {
        // Arrange
        var column = new ColumnExpression("Offset", "row", typeof(long), LongTypeMapping.Default, nullable: false);
        var window = new MySqlWindowExpression(MySqlWindowFunction.Lag,
            [Constant(1), column],
            [],
            [new OrderingExpression(Constant(1), ascending: true)],
            null,
            typeof(int),
            IntTypeMapping.Default,
            nullable: true);

        var engine = EngineProfileTable.Resolve(EngineFamily.MySql, new Version(8, 4));
        MySqlWindowParameterConstraints.Builder? builder = null;

        // Act
        var exception = Record.Exception(() => MySqlWindowParameterConstraints.Collect(window, engine, ref builder));

        // Assert
        Assert.IsType<InvalidOperationException>(exception);
        Assert.Null(builder);
    }

    private static MySqlWindowParameterConstraints CreateNumericPolicy(
        bool positive
    )
    {
        var parameter = new SqlParameterExpression("position", typeof(long), LongTypeMapping.Default);
        var arguments = positive
            ? new SqlExpression[] { parameter }
            : [Constant(1), parameter];

        var window = new MySqlWindowExpression(positive
                ? MySqlWindowFunction.Ntile
                : MySqlWindowFunction.Lag,
            arguments,
            [],
            [new OrderingExpression(Constant(1), ascending: true)],
            null,
            typeof(long),
            LongTypeMapping.Default,
            nullable: !positive);

        var engine = EngineProfileTable.Resolve(EngineFamily.MySql, new Version(8, 4));
        MySqlWindowParameterConstraints.Builder? builder = null;
        MySqlWindowParameterConstraints.Collect(window, engine, ref builder);

        return builder!.Build();
    }

    private static MySqlWindowParameterConstraints CreateFramePolicy()
    {
        var start = new SqlParameterExpression("start", typeof(decimal), DecimalTypeMapping.Default);
        var end = new SqlParameterExpression("end", typeof(decimal), DecimalTypeMapping.Default);
        var frame = new MySqlWindowFrame(true,
            MySqlWindowBoundKind.Preceding,
            start,
            MySqlWindowBoundKind.Preceding,
            end);

        var window = new MySqlWindowExpression(MySqlWindowFunction.Sum,
            [Constant(1)],
            [],
            [new OrderingExpression(Constant(1), ascending: true)],
            frame,
            typeof(int),
            IntTypeMapping.Default,
            nullable: true);

        var engine = EngineProfileTable.Resolve(EngineFamily.MySql, new Version(8, 4));
        MySqlWindowParameterConstraints.Builder? builder = null;
        MySqlWindowParameterConstraints.Collect(window, engine, ref builder);

        return builder!.Build();
    }
}
