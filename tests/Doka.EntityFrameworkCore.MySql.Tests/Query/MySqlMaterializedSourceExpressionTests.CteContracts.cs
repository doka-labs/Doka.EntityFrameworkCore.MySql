namespace Doka.EntityFrameworkCore.MySql.Tests;

public sealed partial class MySqlMaterializedSourceExpressionTests
{
    /// <summary>The real EF mutation AST requires a physical table node and cannot accept a bound CTE reference as its target.</summary>
    [Theory]
    [InlineData(typeof(UpdateExpression))]
    [InlineData(typeof(DeleteExpression))]
    public void Mutation_ast_target_is_a_physical_table_expression(
        Type mutation
    )
    {
        // Arrange
        var target = mutation.GetProperty("Table", BindingFlags.Public | BindingFlags.Instance);

        // Act
        var actual = target?.PropertyType;

        // Assert
        Assert.Equal(typeof(TableExpression), actual);
        Assert.False(typeof(TableExpression).IsAssignableFrom(typeof(MySqlMaterializedSourceExpression)));
    }

    /// <summary>CTE-N06 pins each distinct physical mismatch rather than depending on a native truncation error.</summary>
    [Theory]
    [InlineData("arity")]
    [InlineData("clr")]
    [InlineData("size")]
    [InlineData("precision")]
    [InlineData("scale")]
    [InlineData("converter")]
    public void Recursive_schema_mismatch_is_rejected_at_its_actual_paired_ordinal(
        string mismatch
    )
    {
        // Arrange
        var pair = CreateMismatchedCteMappings(mismatch);
        var anchor = CreateCompletedCteSelect(null,
            [],
            [new ProjectionExpression(new SqlConstantExpression(null, pair.Left.ClrType, pair.Left), "Value")]);

        var frontier =
            new MySqlRecursiveCteReferenceExpression("frontier",
                73,
                new SqlConstantExpression(2, IntTypeMapping.Default));

        var stepProjections = new List<ProjectionExpression>
        {
            new(new SqlConstantExpression(null, pair.Right.ClrType, pair.Right), "Value"),
        };

        if (mismatch == "arity")
        {
            stepProjections.Add(new ProjectionExpression(new SqlConstantExpression(2, IntTypeMapping.Default),
                "Extra"));
        }

        var step = CreateCompletedCteSelect(null, [frontier], stepProjections);
        var union = new UnionExpression("union_source", anchor, step, distinct: false);
        var definition = CreateCompletedCteSelect("definition_seed",
            [union],
            [
                new ProjectionExpression(
                    new ColumnExpression("Value", union.Alias, pair.Left.ClrType, pair.Left, nullable: true),
                    "Value"),
            ]);

        var reference = new MySqlMaterializedSourceExpression("rows",
            73,
            new MySqlMaterializedSourceDefinition(definition, MySqlSourceBindingKind.Recursive),
            null);

        // Act
        var failure = Record.Exception(() => MySqlRecursiveCteShape.Create(reference));

        // Assert
        Assert.Contains(mismatch == "arity"
                ? "same visible column schema"
                : "compatible physical types and converters",
            Assert.IsType<InvalidOperationException>(failure)
                .Message,
            StringComparison.Ordinal);
    }

    /// <summary>Changes only the selected schema dimension where EF's mapping API permits independent facets.</summary>
    private static (RelationalTypeMapping Left, RelationalTypeMapping Right) CreateMismatchedCteMappings(
        string mismatch
    )
    {
        if (mismatch == "converter")
        {
            var converter = new ValueConverter<int, int>(value => value + 1, value => value - 1);

            return (IntTypeMapping.Default,
                (RelationalTypeMapping)IntTypeMapping.Default.WithComposedConverter(converter));
        }

        return mismatch switch
        {
            "arity" => (IntTypeMapping.Default, IntTypeMapping.Default),
            "clr" => (IntTypeMapping.Default, LongTypeMapping.Default),
            "size" => (MySqlStringTypeMapping.Default.WithStoreTypeAndSize("varchar", 10),
                MySqlStringTypeMapping.Default.WithStoreTypeAndSize("varchar", 20)),
            "precision" => (new DecimalTypeMapping("decimal", DbType.Decimal, precision: 18, scale: 4),
                new DecimalTypeMapping("decimal", DbType.Decimal, precision: 20, scale: 4)),
            "scale" => (new DecimalTypeMapping("decimal", DbType.Decimal, precision: 18, scale: 4),
                new DecimalTypeMapping("decimal", DbType.Decimal, precision: 18, scale: 5)),
            _ => throw new ArgumentOutOfRangeException(nameof(mismatch)),
        };
    }

    /// <summary>Ordinary self/mutual cycles fail at each actual consumer instead of relying on the legacy fence's quotation test.</summary>
    [Theory]
    [InlineData(false, "prune")]
    [InlineData(false, "sql")]
    [InlineData(false, "quote")]
    [InlineData(true, "prune")]
    [InlineData(true, "sql")]
    [InlineData(true, "quote")]
    [Experimental("EF9100")]
    public void Ordinary_definition_cycles_are_rejected_by_each_completed_graph_consumer(
        bool mutual,
        string consumer
    )
    {
        // Arrange
        using var context = CreateContext();
        var tables = new List<TableExpressionBase>();
        var first = new MySqlMaterializedSourceExpression("first",
            41,
            new MySqlMaterializedSourceDefinition(CreateSource("first_seed", tables, []),
                MySqlSourceBindingKind.Ordinary),
            null);

        var dependency = mutual
            ? new MySqlMaterializedSourceExpression("second",
                42,
                new MySqlMaterializedSourceDefinition(CreateSource("second_seed", [first], []),
                    MySqlSourceBindingKind.Ordinary),
                null)
            : first;

        tables.Add(dependency);
        var root = CreateCompletedCteSelect(null, [first], []);

        // Act
        var failure = Record.Exception(() =>
        {
            _ = consumer switch
            {
                "prune" => (object)PruneCteRoot(root),
                "sql" => context
                    .GetService<IQuerySqlGeneratorFactory>()
                    .Create()
                    .GetCommand(root),
                "quote" => first.Quote(),
                _ => throw new ArgumentOutOfRangeException(nameof(consumer)),
            };
        });

        // Assert
        Assert.Contains("acyclic",
            Assert.IsType<InvalidOperationException>(failure)
                .Message,
            StringComparison.Ordinal);
    }

    /// <summary>A completed finite command survives actual expression quotation, including its recursive frontier and hidden controls.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Experimental("EF9100")]
    public void Completed_cte_command_quote_preserves_its_exact_generated_sql(
        bool recursive
    )
    {
        // Arrange
        using var context = CreateContext();
        var command = CreateCompletedCteCommand(recursive);
        var expected = context
            .GetService<IQuerySqlGeneratorFactory>()
            .Create()
            .GetCommand(command)
            .CommandText;

        var bound = new TypeMappingSourceBindingExpressionVisitor(context.GetService<IRelationalTypeMappingSource>())
            .Visit(command.Quote());

        // Act
        var reconstructed = Expression
            .Lambda<Func<SelectExpression>>(bound)
            .Compile()();

        var actual = context
            .GetService<IQuerySqlGeneratorFactory>()
            .Create()
            .GetCommand(reconstructed)
            .CommandText;

        // Assert
        Assert.Equal(expected, actual);
        Assert.Equal(recursive
                ? MySqlSourceBindingKind.Recursive
                : MySqlSourceBindingKind.Ordinary,
            Assert.IsType<MySqlMaterializedSourceExpression>(Assert.Single(reconstructed.Tables))
                .Kind);

        Assert.NotSame(command, reconstructed);
    }

    /// <summary>EF-generated C# actually compiles, reconstructs the completed command and produces the same SQL.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Experimental("EF9100")]
    public void Completed_cte_command_generated_csharp_reconstructs_its_exact_sql(
        bool recursive
    )
    {
        // Arrange
        using var context = CreateContext();
        var command = CreateCompletedCteCommand(recursive);
        var expected = context
            .GetService<IQuerySqlGeneratorFactory>()
            .Create()
            .GetCommand(command)
            .CommandText;

        // Act
        var reconstructed = CompileCompletedCteCommand(command, context);
        var actual = context
            .GetService<IQuerySqlGeneratorFactory>()
            .Create()
            .GetCommand(reconstructed)
            .CommandText;

        // Assert
        Assert.Equal(expected, actual);
        Assert.NotSame(command, reconstructed);
    }

    /// <summary>Creates a real ordinary relation or one finite anchor/step relation with an independent final SELECT.</summary>
    private static SelectExpression CreateCompletedCteCommand(
        bool recursive
    )
    {
        var seed = CreateCompletedCteSelect(null,
            [],
            [new ProjectionExpression(new SqlConstantExpression(1, IntTypeMapping.Default), "Value")]);

        SelectExpression definition;

        if (recursive)
        {
            var frontier = new MySqlRecursiveCteReferenceExpression("frontier",
                73,
                new SqlConstantExpression(2, IntTypeMapping.Default));

            var stepValue = new SqlBinaryExpression(ExpressionType.Add,
                new ColumnExpression("Value", "frontier", typeof(int), IntTypeMapping.Default, nullable: true),
                new SqlConstantExpression(1, IntTypeMapping.Default),
                typeof(int),
                IntTypeMapping.Default);

            var step = CreateCompletedCteSelect(null, [frontier], [new ProjectionExpression(stepValue, "Value")]);
            var union = new UnionExpression("union_source", seed, step, distinct: false);
            definition = CreateCompletedCteSelect("definition_seed",
                [union],
                [
                    new ProjectionExpression(new ColumnExpression("Value",
                            union.Alias,
                            typeof(int),
                            IntTypeMapping.Default,
                            nullable: true),
                        "Value"),
                ]);
        }
        else
        {
            definition = CreateSource("definition_seed",
                [],
                [new ProjectionExpression(new SqlConstantExpression(1, IntTypeMapping.Default), "Value")]);
        }

        var reference = new MySqlMaterializedSourceExpression("rows",
            73,
            new MySqlMaterializedSourceDefinition(definition,
                recursive
                    ? MySqlSourceBindingKind.Recursive
                    : MySqlSourceBindingKind.Ordinary),
            null);

        return CreateCompletedCteSelect(null,
            [reference],
            [
                new ProjectionExpression(new ColumnExpression("Value",
                        reference.Alias!,
                        typeof(int),
                        IntTypeMapping.Default,
                        nullable: true),
                    "Value"),
            ]);
    }

    /// <summary>Creates an immutable root with an actual absent alias rather than abusing a nonnullable factory parameter.</summary>
    private static SelectExpression CreateCompletedCteSelect(
        string? alias,
        List<TableExpressionBase> tables,
        List<ProjectionExpression> projections
    )
    {
#pragma warning disable EF1001 // Exact immutable root construction, qualified by the existing supported-EF matrix.
        return new SelectExpression(alias,
            tables,
            null,
            [],
            null,
            projections,
            false,
            [],
            null,
            null,
            new HashSet<string>(),
            null,
            new SqlAliasManager(),
            isMutable: false);
#pragma warning restore EF1001
    }

    /// <summary>Compiles the existing EF translator output without changing provider visibility or promising consumer NativeAOT support.</summary>
    [Experimental("EF9100")]
    private static SelectExpression CompileCompletedCteCommand(
        SelectExpression command,
        QuoteContext context
    )
    {
        var namespaces = new HashSet<string>();
        var accessors = new HashSet<string>();
#pragma warning disable EF1001 // Exercise EF's real late-quotation C# translator, not a replacement serializer.
        var helper = new CSharpHelper(context.GetService<ITypeMappingSource>());
        var code = helper.Expression(Expression.Constant(command), namespaces, accessors, null, null);
#pragma warning restore EF1001
        var source = string.Join(Environment.NewLine,
                namespaces
                    .Order(StringComparer.Ordinal)
                    .Select(name => "using " + name + ";"))
            + "\npublic static class CompletedCteCommand { public static Microsoft.EntityFrameworkCore.Query.SqlExpressions.SelectExpression Read("
            + "Microsoft.EntityFrameworkCore.Storage.IRelationalTypeMappingSource relationalTypeMappingSource) => "
            + code
            + ";\n"
            + string.Join(Environment.NewLine, accessors)
            + "\n}";

        var trusted =
            ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?.Split(Path.PathSeparator,
                StringSplitOptions.RemoveEmptyEntries)
            ?? [];

        var references = trusted
            .Concat(AppDomain
                .CurrentDomain
                .GetAssemblies()
                .Where(assembly => !assembly.IsDynamic && !string.IsNullOrEmpty(assembly.Location))
                .Select(assembly => assembly.Location))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => MetadataReference.CreateFromFile(path));

        // WHY: Provider SQL nodes are intentionally internal. Reuse the existing test-friend
        // identity only for this in-memory proof; do not widen shipping API visibility.
        var compilation = CSharpCompilation.Create(typeof(MySqlMaterializedSourceExpressionTests).Assembly.GetName()
                .Name
            ?? throw new InvalidOperationException("The test-friend assembly must have a name."),
            [CSharpSyntaxTree.ParseText(source)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));

        using var output = new MemoryStream();
        var emitted = compilation.Emit(output);
        Assert.True(emitted.Success,
            string.Join(Environment.NewLine,
                emitted.Diagnostics.Where(item => item.Severity == DiagnosticSeverity.Error)));

        var generated = Assembly.Load(output.ToArray());

        return (SelectExpression)generated.GetType("CompletedCteCommand", throwOnError: true)!.GetMethod("Read")!
            .Invoke(null, [context.GetService<IRelationalTypeMappingSource>()])!;
    }
}
