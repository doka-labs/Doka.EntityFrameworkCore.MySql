namespace Doka.EntityFrameworkCore.MySql.Tests;

public sealed partial class MySqlWindowExpressionTests
{
    /// <summary>Actual EF pregeneration rejects the expression-only marker receiver while ordinary pregeneration remains available.</summary>
    [Theory]
    [InlineData("ordinary")]
    [InlineData("constant")]
    [InlineData("parameter")]
    [Experimental("EF9100")]
    public void Pregenerated_window_boundary_preserves_the_ordinary_provider_contract(
        string form
    )
    {
        // Arrange
        using var context = new WindowPregenerationContext(new DbContextOptionsBuilder<WindowPregenerationContext>()
            .UseInternalServiceProvider(_services)
            .UseMySql("Server=127.0.0.1;Port=1;Database=window_pregeneration;User ID=unused;Pooling=false;",
                MySqlServerVersion.MySql(new Version(8, 4)))
            .Options);

        var buckets = 2L;
        var query = form switch
        {
            "ordinary" => context.Rows.Select(row => (long)row.Id),
            "constant" => context.Rows.Select(row => EF
                .Functions
                .Window()
                .OrderBy(row.Id)
                .Ntile(2)),
            "parameter" => context.Rows.Select(row => EF
                .Functions
                .Window()
                .OrderBy(row.Id)
                .Ntile(EF.Parameter(buckets))),
            _ => throw new ArgumentOutOfRangeException(nameof(form)),
        };

        // Act
        // WHY: The real compiler must extract parameters before generation; calling the
        // translation visitor alone could fail for an unrelated unextracted closure instead.
#pragma warning disable EF1001 // This exact EF10 compiler boundary is the subject of the regression, not a replacement query pipeline.
        var error = Record.Exception(() => context
            .GetService<IQueryCompiler>()
            .PrecompileQuery<IEnumerable<long>>(query.Expression, async: false));
#pragma warning restore EF1001

        // Assert
        if (form == "ordinary")
        {
            Assert.Null(error);
        }
        else
        {
            // WHY: EF10 rejects the nonconstant structural receiver before SQL generation.
            // This records the actual framework boundary, not a new blanket provider restriction.
            Assert.Equal(CoreStrings.NotParameterizedAttributeWithNonConstantNotSupportedInPrecompiledQueries(
                    "functions",
                    nameof(MySqlDbFunctionsExtensions.Window)),
                Assert.IsType<InvalidOperationException>(error)
                    .Message);
        }

        Assert.True(context
            .GetService<IQueryCompilationContextFactory>()
            .Create(async: false)
            .SupportsPrecompiledQuery);

        Assert.Equal(ConnectionState.Closed,
            context.Database.GetDbConnection()
                .State);
    }

    private sealed class WindowPregenerationRow
    {
        public int Id { get; set; }
    }

    private sealed class WindowPregenerationContext
        : DbContext
    {
        /// <summary>Initializes the context with its supplied options and model settings.</summary>
        public WindowPregenerationContext(
            DbContextOptions<WindowPregenerationContext> options
        ) : base(options) { }

        public DbSet<WindowPregenerationRow> Rows => Set<WindowPregenerationRow>();
    }
}
