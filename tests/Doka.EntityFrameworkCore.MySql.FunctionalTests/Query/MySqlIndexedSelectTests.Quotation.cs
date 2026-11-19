namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

public sealed partial class MySqlIndexedSelectTests
{
    /// <summary>Quotation retains the actual framework node and the standard checked integer reader contract.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Experimental("EF9100")]
    public void Index_conversion_quotation_preserves_the_complete_native_expression(
        bool partitioned
    )
    {
        // Arrange
        using var context = CreateContext(false);
        var source = context.GetService<IRelationalTypeMappingSource>();
        var original = CreateIndexExpression(source, partitioned);

        // Act
        var bound = new IndexMappingSourceBinding(source).Visit(original.Quote());
        var reconstructed = Expression
            .Lambda<Func<SqlUnaryExpression>>(bound)
            .Compile()();

        // Assert
        Assert.Equal(original, reconstructed);
        Assert.Equal(original.GetHashCode(), reconstructed.GetHashCode());
        Assert.Equal(typeof(int), reconstructed.TypeMapping?.ClrType);
        Assert.Equal(typeof(int),
            reconstructed.TypeMapping?.GetDataReaderMethod()
                .ReturnType);

        Assert.Null(reconstructed.TypeMapping?.Converter);
        var originalOrdinal = Assert.IsType<MySqlQuotableRowNumberExpression>(
            Assert.IsType<SqlBinaryExpression>(original.Operand)
                .Left);

        var reconstructedOrdinal = Assert.IsType<RowNumberExpression>(
            Assert.IsType<SqlBinaryExpression>(reconstructed.Operand)
                .Left);

        Assert.Equal(originalOrdinal.Partitions, reconstructedOrdinal.Partitions);
        Assert.Equal(originalOrdinal.Orderings, reconstructedOrdinal.Orderings);
        Assert.Equal(originalOrdinal.TypeMapping, reconstructedOrdinal.TypeMapping);
        Assert.Equal(partitioned
                ? 1
                : 0,
            reconstructedOrdinal.Partitions.Count);
    }

    /// <summary>Generated C# in a separate assembly can reconstruct the index without referring to an internal provider mapping.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Experimental("EF9100")]
    public void Index_conversion_generated_csharp_preserves_the_public_reconstruction_path(
        bool partitioned
    )
    {
        // Arrange
        using var context = CreateContext(false);
        var source = context.GetService<IRelationalTypeMappingSource>();
        var original = CreateIndexExpression(source, partitioned);
        var namespaces = new HashSet<string>(StringComparer.Ordinal);
        var unsafeAccessors = new HashSet<string>(StringComparer.Ordinal);

        // Act
#pragma warning disable EF1001 // Qualify EF's actual C# expression reconstruction, not only Expression.Compile inside the test assembly.
        var helper = new CSharpHelper(context.GetService<ITypeMappingSource>());
        var expression = helper.Expression(original.Quote(), namespaces, unsafeAccessors, null, null);
#pragma warning restore EF1001
        var code = "#nullable enable\n"
            + string.Join("\n",
                namespaces
                    .Order(StringComparer.Ordinal)
                    .Select(name => "using " + name + ";"))
            + "\nnamespace Doka.GeneratedIndexQuote;\npublic static class IndexFactory\n{\n"
            + "public static Microsoft.EntityFrameworkCore.Query.SqlExpressions.SqlUnaryExpression Create("
            + "Microsoft.EntityFrameworkCore.Storage.RelationalTypeMappingSource relationalTypeMappingSource) => "
            + expression
            + ";\n"
            + string.Join("\n", unsafeAccessors)
            + "\n}\n";

        var assembly = CompileIndexFactory(code);
        var factory = assembly.GetType("Doka.GeneratedIndexQuote.IndexFactory", throwOnError: true)!
            .GetMethod("Create", BindingFlags.Public | BindingFlags.Static)!;

        var reconstructed = Assert.IsType<SqlUnaryExpression>(factory.Invoke(null, [source]));

        // Assert
        Assert.Equal(original, reconstructed);
        Assert.DoesNotContain("Doka.EntityFrameworkCore.MySql.", expression, StringComparison.Ordinal);
        Assert.Equal(typeof(int),
            reconstructed.TypeMapping?.GetDataReaderMethod()
                .ReturnType);

        Assert.Null(reconstructed.TypeMapping?.Converter);
        var reconstructedOrdinal = Assert.IsType<RowNumberExpression>(
            Assert.IsType<SqlBinaryExpression>(reconstructed.Operand)
                .Left);

        Assert.Equal(partitioned
                ? 1
                : 0,
            reconstructedOrdinal.Partitions.Count);

        Assert.Equal(source.FindMapping(typeof(long)), reconstructedOrdinal.TypeMapping);
    }

    /// <summary>Updating either child group preserves the adapter, mapping, and untouched original expression.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Row_number_update_preserves_the_adapter_when_children_change(
        bool replacePartition
    )
    {
        // Arrange
        using var context = CreateContext(false);
        var source = context.GetService<IRelationalTypeMappingSource>();
        var original = CreateRowNumberExpression(source, partitioned: true);
        var replacement = new ColumnExpression("Changed",
            "other",
            typeof(int),
            source.FindMapping(typeof(int)),
            nullable: true);

        var partitions = replacePartition
            ? new SqlExpression[] { replacement }
            : original.Partitions;

        var orderings = replacePartition
            ? original.Orderings
            : new[] { new OrderingExpression(replacement, ascending: false) };

        // Act
        var updated = original.Update(partitions, orderings);

        // Assert
        Assert.IsType<MySqlQuotableRowNumberExpression>(updated);
        Assert.NotSame(original, updated);
        Assert.Equal(partitions, updated.Partitions);
        Assert.Equal(orderings, updated.Orderings);
        Assert.Same(original.TypeMapping, updated.TypeMapping);
        Assert.Equal("AccountId",
            Assert.IsType<ColumnExpression>(Assert.Single(original.Partitions))
                .Name);

        Assert.Equal("Id",
            Assert.IsType<ColumnExpression>(Assert.Single(original.Orderings)
                    .Expression)
                .Name);
    }

    /// <summary>Equivalent child lists do not allocate a replacement row-number node.</summary>
    [Fact]
    public void Row_number_update_retains_identity_for_unchanged_children()
    {
        // Arrange
        using var context = CreateContext(false);
        var original = CreateRowNumberExpression(context.GetService<IRelationalTypeMappingSource>(), partitioned: true);
        var partitions = original.Partitions.ToArray();
        var orderings = original.Orderings.ToArray();

        // Act
        var updated = original.Update(partitions, orderings);

        // Assert
        Assert.Same(original, updated);
    }

    /// <summary>A null partition update removes the partition without losing the quotation adapter.</summary>
    [Fact]
    public void Row_number_update_can_clear_partitions_without_changing_the_original()
    {
        // Arrange
        using var context = CreateContext(false);
        var original = CreateRowNumberExpression(context.GetService<IRelationalTypeMappingSource>(), partitioned: true);

        // Act
        var updated = original.Update(null, original.Orderings);

        // Assert
        Assert.IsType<MySqlQuotableRowNumberExpression>(updated);
        Assert.Empty(updated.Partitions);
        Assert.Equal(original.Orderings, updated.Orderings);
        Assert.Same(original.TypeMapping, updated.TypeMapping);
        Assert.Single(original.Partitions);
    }

    /// <summary>A visitor changes only its selected child while retaining adapter behavior and ordering direction.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Row_number_visitor_preserves_the_adapter_when_children_change(
        bool replacePartition
    )
    {
        // Arrange
        using var context = CreateContext(false);
        var source = context.GetService<IRelationalTypeMappingSource>();
        var original = CreateRowNumberExpression(source, partitioned: true);
        var selected = replacePartition
            ? original.Partitions[0]
            : original.Orderings[0].Expression;

        var replacement = new ColumnExpression("Changed",
            "other",
            typeof(int),
            source.FindMapping(typeof(int)),
            nullable: true);

        var visitor = new IndexChildReplacingVisitor(selected, replacement);

        // Act
        var visited = visitor.Visit(original);

        // Assert
        var updated = Assert.IsType<MySqlQuotableRowNumberExpression>(visited);
        Assert.NotSame(original, updated);
        Assert.Same(replacePartition
                ? replacement
                : original.Partitions[0],
            Assert.Single(updated.Partitions));

        Assert.Same(replacePartition
                ? original.Orderings[0].Expression
                : replacement,
            Assert.Single(updated.Orderings)
                .Expression);

        Assert.Equal(original.Orderings[0].IsAscending, updated.Orderings[0].IsAscending);
        Assert.Same(original.TypeMapping, updated.TypeMapping);
        Assert.Equal("AccountId",
            Assert.IsType<ColumnExpression>(Assert.Single(original.Partitions))
                .Name);

        Assert.Equal("Id",
            Assert.IsType<ColumnExpression>(Assert.Single(original.Orderings)
                    .Expression)
                .Name);
    }

    /// <summary>A visitor whose replacement is outside this expression keeps the original node and child lists.</summary>
    [Fact]
    public void Row_number_visitor_retains_identity_for_unchanged_children()
    {
        // Arrange
        using var context = CreateContext(false);
        var source = context.GetService<IRelationalTypeMappingSource>();
        var original = CreateRowNumberExpression(source, partitioned: true);
        var unrelated = new ColumnExpression("Other",
            "other",
            typeof(int),
            source.FindMapping(typeof(int)),
            nullable: false);

        var visitor = new IndexChildReplacingVisitor(unrelated, original.Partitions[0]);

        // Act
        var visited = visitor.Visit(original);

        // Assert
        Assert.Same(original, visited);
        Assert.Same(original.Partitions,
            Assert.IsType<MySqlQuotableRowNumberExpression>(visited)
                .Partitions);

        Assert.Same(original.Orderings,
            Assert.IsType<MySqlQuotableRowNumberExpression>(visited)
                .Orderings);
    }

    /// <summary>Uses the same public framework AST and numeric mappings as the real indexed-selector translation.</summary>
    private static SqlUnaryExpression CreateIndexExpression(
        IRelationalTypeMappingSource source,
        bool partitioned
    )
    {
        var integer = source.FindMapping(typeof(int))!;
        var wide = source.FindMapping(typeof(long))!;
        var ordinal = CreateRowNumberExpression(source, partitioned);

        var zeroBased = new SqlBinaryExpression(ExpressionType.Subtract,
            ordinal,
            new SqlConstantExpression(1L, wide),
            typeof(long),
            wide);

        return new SqlUnaryExpression(ExpressionType.Convert, zeroBased, typeof(int), integer);
    }

    /// <summary>Creates the provider's EF10 ordinal adapter with independently distinguishable partition and ordering columns.</summary>
    private static MySqlQuotableRowNumberExpression CreateRowNumberExpression(
        IRelationalTypeMappingSource source,
        bool partitioned
    )
    {
        var integer = source.FindMapping(typeof(int))!;

        return new MySqlQuotableRowNumberExpression(
            partitioned
                ? [new ColumnExpression("AccountId", "row", typeof(int), integer, nullable: false)]
                : [],
            [
                new OrderingExpression(new ColumnExpression("Id", "row", typeof(int), integer, nullable: false),
                    ascending: true),
            ],
            source.FindMapping(typeof(long))!);
    }

    /// <summary>Reuses the existing EF test compiler for one generated source file.</summary>
    private static Assembly CompileIndexFactory(
        string code
    )
    {
        var build = new BuildSource
        {
            NullableReferenceTypes = true,
            Sources = new Dictionary<string, string>(StringComparer.Ordinal) { ["IndexFactory.cs"] = code },
        };

        var platform = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            ?? [];

        var paths = platform
            .Concat(AppDomain
                .CurrentDomain
                .GetAssemblies()
                .Where(assembly => !assembly.IsDynamic && !string.IsNullOrEmpty(assembly.Location))
                .Select(assembly => assembly.Location))
            .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var path in paths)
        {
            build.References.Add(BuildReference.ByPath(path));
        }

        return build.BuildInMemory();
    }

    private sealed class IndexMappingSourceBinding : ExpressionVisitor
    {
        private readonly IRelationalTypeMappingSource _source;

        /// <summary>Initializes the test helper with its supplied dependencies and values.</summary>
        public IndexMappingSourceBinding(
            IRelationalTypeMappingSource source
        )
        {
            _source = source;
        }

        protected override Expression VisitParameter(
            ParameterExpression node
        ) => node.Name == "relationalTypeMappingSource"
            ? Expression.Constant(_source, node.Type)
            : base.VisitParameter(node);
    }

    /// <summary>Targets one actual child identity so unrelated partitions or orderings remain untouched.</summary>
    private sealed class IndexChildReplacingVisitor : ExpressionVisitor
    {
        private readonly SqlExpression _original;
        private readonly SqlExpression _replacement;

        /// <summary>Initializes the visitor with the mappings and expressions used by this test.</summary>
        public IndexChildReplacingVisitor(
            SqlExpression original,
            SqlExpression replacement
        )
        {
            _original = original;
            _replacement = replacement;
        }

        /// <inheritdoc />
        protected override Expression VisitExtension(
            Expression node
        ) => ReferenceEquals(node, _original)
            ? _replacement
            : base.VisitExtension(node);
    }
}
