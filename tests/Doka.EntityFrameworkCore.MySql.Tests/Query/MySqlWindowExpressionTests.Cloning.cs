namespace Doka.EntityFrameworkCore.MySql.Tests;

public sealed partial class MySqlWindowExpressionTests
{
    /// <summary>EF's actual SelectExpression clone retains typed window children, frame parameters and generated SQL.</summary>
    [Fact]
    public void Framework_select_clone_preserves_the_window_mapping_parameters_and_sql_binding()
    {
        // Arrange
        using var context = new WindowPregenerationContext(new DbContextOptionsBuilder<WindowPregenerationContext>()
            .UseInternalServiceProvider(_services)
            .UseMySql("Server=127.0.0.1;Port=1;Database=window_clone;User ID=unused;Pooling=false;",
                MySqlServerVersion.MySql(new Version(8, 4)))
            .Options);

        var mappings = context.GetService<IRelationalTypeMappingSource>();
        var integer = mappings.FindMapping(typeof(int))!;
        var wide = mappings.FindMapping(typeof(long))!;
        var table = context
            .Model
            .GetRelationalModel()
            .FindTable("Rows", schema: null)!;

        // WHY: Construct the actual completed framework SELECT so Clone, rather than a
        // stand-in visitor or quotation, must retain the window's relational bindings.
#pragma warning disable EF1001 // Direct EF11 relational-node construction is the specific versioned clone contract under test.
        var source = new TableExpression("r", table);
        var column = new ColumnExpression("Id", source.Alias, typeof(int), integer, nullable: false);
        var width = new SqlParameterExpression("width", typeof(long), wide);
        var window = new MySqlWindowExpression(MySqlWindowFunction.Sum,
            [column],
            [column],
            [new OrderingExpression(column, ascending: false)],
            new MySqlWindowFrame(true, MySqlWindowBoundKind.Preceding, width, MySqlWindowBoundKind.CurrentRow, null),
            typeof(int),
            integer,
            nullable: true);

        var original = new SelectExpression(alias: null,
            [source],
            predicate: null,
            groupBy: [],
            having: null,
            [new ProjectionExpression(window, "Value")],
            distinct: false,
            orderings: [],
            offset: null,
            limit: null,
            new SqlAliasManager());
#pragma warning restore EF1001
        var generator = context.GetService<IQuerySqlGeneratorFactory>();
        var originalCommand = generator
            .Create()
            .GetCommand(original);

        // Act
#pragma warning disable EF1001 // The regression intentionally exercises EF11's actual framework cloning API.
        var cloned = original.Clone();
#pragma warning restore EF1001
        var clonedCommand = generator
            .Create()
            .GetCommand(cloned);

        // Assert
        Assert.NotSame(original, cloned);
        var clonedSource = Assert.IsType<TableExpression>(Assert.Single(cloned.Tables));
        var clonedWindow = Assert.IsType<MySqlWindowExpression>(Assert.Single(cloned.Projection)
            .Expression);

        Assert.Equal(MySqlWindowFunction.Sum, clonedWindow.Function);
        Assert.True(clonedWindow.IsNullable);
        Assert.Same(integer, clonedWindow.TypeMapping);
        Assert.Same(integer,
            Assert.Single(clonedWindow.Arguments)
                .TypeMapping);

        Assert.Equal(clonedSource.Alias,
            Assert.IsType<ColumnExpression>(Assert.Single(clonedWindow.Arguments))
                .TableAlias);

        Assert.Equal(clonedSource.Alias,
            Assert.IsType<ColumnExpression>(Assert.Single(clonedWindow.Partitions))
                .TableAlias);

        Assert.False(Assert.Single(clonedWindow.Orderings)
            .IsAscending);

        Assert.Equal(clonedSource.Alias,
            Assert.IsType<ColumnExpression>(Assert.Single(clonedWindow.Orderings)
                    .Expression)
                .TableAlias);

        var parameter = Assert.IsType<SqlParameterExpression>(clonedWindow.Frame!.StartOffset);
        Assert.Equal("width", parameter.Name);
        Assert.Same(wide, parameter.TypeMapping);
        Assert.True(clonedWindow.Frame.Rows);
        Assert.Equal(MySqlWindowBoundKind.CurrentRow, clonedWindow.Frame.EndKind);
        Assert.Contains("SUM(", clonedCommand.CommandText, StringComparison.Ordinal);
        Assert.Contains("ROWS BETWEEN @width PRECEDING AND CURRENT ROW",
            clonedCommand.CommandText,
            StringComparison.Ordinal);

        Assert.Equal(originalCommand.Parameters.Select(value => value.InvariantName),
            clonedCommand.Parameters.Select(value => value.InvariantName));

        Assert.Equal("width",
            Assert.Single(clonedCommand.Parameters)
                .InvariantName);
    }
}
