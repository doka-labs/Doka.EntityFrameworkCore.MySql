using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;

namespace Doka.EntityFrameworkCore.MySql.Benchmarks;

public partial class QueryEnumerationBenchmark
{
    /// <summary>Preserves an entity through a selected member of a member-initialized projection.</summary>
    private Func<object> CreateMemberInitLineageOperation()
    {
        var wrapped = _context.PlainRows.Select(row => new EntityProjection { Ignored = row.Id, Entity = row });
        var source = wrapped.Select(wrapper => wrapper.Entity);
        var projection = (LambdaExpression)((UnaryExpression)
            ((MethodCallExpression)wrapped.Expression).Arguments[1]).Operand;

        var selectedMember = (LambdaExpression)((UnaryExpression)
            ((MethodCallExpression)source.Expression).Arguments[1]).Operand;

        // WHY: A direct DTO projection has no selected member to resolve. This
        // outer selector pushes Entity before the binder inspects its two bindings.
        if (projection.Body is not MemberInitExpression { Bindings.Count: 2 } initialization
            || selectedMember.Body is not MemberExpression { Member.Name: nameof(EntityProjection.Entity) }
            || initialization.Bindings[1].Member.Name != nameof(EntityProjection.Entity))
        {
            throw new InvalidOperationException(
                "The lineage fixture must select the final member-initialized entity binding.");
        }

        Console.WriteLine($"member-init-bindings={initialization.Bindings.GetType().FullName}; "
            + $"selected-member={nameof(EntityProjection.Entity)}");
        var operation = Compile(source.WithMaterializedSource(shared => shared.Select(row => row.Children.Count)));
        RequireCompiledSql("WITH RECURSIVE", "ParentId");

        return operation;
    }

    /// <summary>Compiles a collection Include on a rehydrated entity joined to a flat bound source.</summary>
    private Func<object> CreateMaterializedIncludeOperation()
    {
        var query = _context.PlainRows.Select(row => row.Id)
            .WithMaterializedSource(shared => shared.Join(
                _context.PlainRows, id => id, row => row.Id, (_, row) => row))
            .Include(row => row.Children)
            .AsSingleQuery();

        // WHY: Including a rehydrated entity reaches the final owner-column scan
        // without asking the bound source to carry a prohibited collection Include.
        var operation = Compile(query);
        RequireCompiledSql("WITH RECURSIVE", "LEFT JOIN", "ParentId");

        return operation;
    }

    /// <summary>Measures the complete UNION ALL physical-table guard without timed reflection.</summary>
    private Func<object> CreateUnionAllGuardOperation()
    {
        var overlap = Scenario == "guard-union-all-overlap";
        var metadata = _context.Model.GetRelationalModel();
        var firstTable = metadata.Tables.Single(table => table.Name == "EnumerationPlainRows");
        var secondTable = overlap
            ? firstTable
            : metadata.Tables.Single(table => table.Name == "EnumerationRows");

        var nested = Select([new ProjectionExpression(Column("Id", "first"), "Id")],
            new List<TableExpressionBase> { new TableExpression("first", firstTable) }, alias: "nested");

        var first = Select([new ProjectionExpression(Column("Id", "nested"), "Id")],
            new List<TableExpressionBase> { nested }, alias: "first_branch");

        var second = Select([new ProjectionExpression(Column("Id", "second"), "Id")],
            new List<TableExpressionBase> { new TableExpression("second", secondTable) }, alias: "second_branch");

        var union = new UnionExpression("union_rows", first, second, distinct: false);
        var validator = typeof(MySqlQueryTranslationPostprocessor).GetNestedType(
            "MySqlCollectionIdentityValidatingExpressionVisitor", BindingFlags.NonPublic)!;

        var inspect = validator.GetMethod("BranchesSharePhysicalTable", BindingFlags.NonPublic | BindingFlags.Static)!
            .CreateDelegate<Func<UnionExpression, bool>>();

        // WHY: A successful UNION query need not reach this guarded APPLY branch.
        // Bind its complete production guard once, rather than inventing an invalid
        // LINQ shape or including reflection in each measured invocation.
        if (union.IsDistinct || inspect(union) != overlap)
        {
            throw new InvalidOperationException("The UNION ALL guard fixture does not match its overlap contract.");
        }

        _input = union;
        Console.WriteLine($"union-all-guard=BranchesSharePhysicalTable; overlap={overlap}; "
            + $"first-tables={first.Tables.GetType().FullName}; "
            + $"nested-tables={nested.Tables.GetType().FullName}; "
            + $"second-tables={second.Tables.GetType().FullName}");

        // WHY: Cache boxed results in setup so the object-returning diagnostic
        // driver does not add one allocation to every boolean guard invocation.
        object matches = true;
        object differs = false;

        return () => inspect(union) ? matches : differs;
    }

    /// <summary>Rejects setup that silently bypasses the intended binding or Include shape.</summary>
    private void RequireCompiledSql(params string[] fragments)
    {
        foreach (var fragment in fragments)
        {
            if (_compiledQuerySql?.Contains(fragment, StringComparison.Ordinal) != true)
            {
                throw new InvalidOperationException(
                    $"The {Scenario} SQL is missing its required shape marker '{fragment}'.");
            }
        }
    }

    private SelectExpression CreateJsonScalar()
    {
        var path = Scenario == "json-static-single"
            ? [new PathSegment("name")]
            : new List<PathSegment>
            {
                new("outer"), new("nested"), new("quoted.name"), new("items"),
                new(Scenario == "json-dynamic-nested"
                    ? new SqlParameterExpression("index", typeof(int), IntTypeMapping.Default)
                    : _sql.Constant(2)),
                new("name"),
            };

        var scalar = new JsonScalarExpression(
            _sql.Constant("{}", MySqlStringTypeMapping.Default), path, typeof(string),
            MySqlStringTypeMapping.Default, nullable: true);

        Console.WriteLine($"json-path={path.GetType().FullName}; count={path.Count}");

        return Select([new ProjectionExpression(scalar, "Value")]);
    }

    private SelectExpression CreateJsonTable()
    {
        var columns = new List<MySqlJsonTableExpression.ColumnInfo>();
        var projections = new List<ProjectionExpression>();

        // WHY: Build exactly 50 paired column/projection descriptors with matching ordinal-derived names.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < 50; index++)
        {
            var name = "Value" + index;
            var path = new List<PathSegment> { new("nested"), new(name) };
            columns.Add(new MySqlJsonTableExpression.ColumnInfo(name, MySqlStringTypeMapping.Default, path,
                ResultTypeMapping: MySqlGuidBinaryTypeMapping.Default));
            projections.Add(new ProjectionExpression(
                new ColumnExpression(name, "j", typeof(Guid), MySqlGuidBinaryTypeMapping.Default, nullable: false),
                name));
        }

        var rowPath = new List<PathSegment> { new("root"), new("items") };
        var table = new MySqlJsonTableExpression(
            "j", _sql.Constant("[]", MySqlStringTypeMapping.Default), rowPath, columns);
        Console.WriteLine($"json-columns={columns.GetType().FullName}; path={rowPath.GetType().FullName}");

        return Select(projections, [table]);
    }

    private Expression CreateApply()
    {
        var tableMetadata = _context.Model.GetRelationalModel().Tables.Single(table => table.Name == "EnumerationRows");
        var outerTable = new TableExpression("o", tableMetadata);
        var innerTable = new TableExpression("i", tableMetadata);
        var innerProjections = new List<ProjectionExpression>();
        var outerProjections = new List<ProjectionExpression>();

        // WHY: Use one ordinal to keep all 50 inner aliases aligned with their outer projections.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < 50; index++)
        {
            var name = "Value" + index;
            innerProjections.Add(new ProjectionExpression(Column("Id", "i"), name));
            outerProjections.Add(new ProjectionExpression(Column(name, "a"), name));
        }

        var inner = Select(innerProjections, [innerTable],
            predicate: _sql.Equal(Column("GroupId", "i"), Column("GroupId", "o")), alias: "a");

        // WHY: The finalized SQL-tree fixture reaches APPLY handling itself; a
        // simple LINQ correlation can be flattened earlier by EF's translator.
        if (Scenario == "apply-delete-50")
        {
            var groupedInner = Select(innerProjections, [innerTable],
                predicate: inner.Predicate, alias: "a", groupBy: [Column("Id", "i")]);

            var localProjections = Enumerable.Range(0, 50)
                .Select(index => new ProjectionExpression(Column("Id", "o"), "Value" + index)).ToList();

            var outer = Select(localProjections, [outerTable, new CrossApplyExpression(groupedInner)],
                groupBy: [Column("Id", "o")], orderings: [new OrderingExpression(Column("Id", "o"), ascending: true)]);

            return new DeleteExpression(outerTable, outer);
        }

        return Select(outerProjections, [outerTable, new CrossApplyExpression(inner)]);
    }

    private SelectExpression CreateDynamicOffset()
    {
        var tableMetadata = _context.Model.GetRelationalModel().Tables.Single(table => table.Name == "EnumerationRows");
        var table = new TableExpression("i", tableMetadata);
        var predicate = _sql.AndAlso(
            _sql.Equal(Column("GroupId", "i"), Column("GroupId", "o")),
            _sql.AndAlso(_sql.GreaterThan(Column("Id", "i"), _sql.Constant(0)),
                _sql.LessThan(Column("Id", "i"), _sql.Constant(1000))));

        return Select([new ProjectionExpression(Column("Id", "i"), "Id")], [table], predicate,
            orderings: [new OrderingExpression(Column("Id", "i"), ascending: true)],
            offset: Column("Id", "o"), limit: _sql.Constant(1));
    }

    private static ColumnExpression Column(string name, string alias)
        => new(name, alias, typeof(int), IntTypeMapping.Default, nullable: false);

    private static SelectExpression Select(
        IReadOnlyList<ProjectionExpression> projections,
        IReadOnlyList<TableExpressionBase>? tables = null,
        SqlExpression? predicate = null,
        string? alias = null,
        IReadOnlyList<SqlExpression>? groupBy = null,
        IReadOnlyList<OrderingExpression>? orderings = null,
        SqlExpression? offset = null,
        SqlExpression? limit = null)
    {
        // WHY: Only EF's finalized tree constructor can prepare these immutable
        // pipeline inputs without timing model creation or mutating shared trees.
#pragma warning disable EF1001
        return new SelectExpression(alias, tables ?? [], predicate, groupBy ?? [], having: null,
            projections, distinct: false, orderings ?? [], offset, limit, new SqlAliasManager());
#pragma warning restore EF1001
    }

    private sealed class QueryContext(DbContextOptions<QueryContext> options) : DbContext(options)
    {
        public DbSet<Row> Rows => Set<Row>();
        public DbSet<PlainRow> PlainRows => Set<PlainRow>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Row>(entity =>
            {
                entity.ToTable("EnumerationRows");
                entity.HasKey(row => row.Id);
                entity.Property(row => row.Name).HasMaxLength(100);
                entity.OwnsOne(row => row.Document, owned =>
                {
                    owned.ToJson();
                    owned.OwnsMany(document => document.Items);
                });
            });

            // WHY: The Include/UNION control isolates relational identity scans;
            // the independent JSON case measures owned-document materialization.
            modelBuilder.Entity<PlainRow>(entity =>
            {
                entity.ToTable("EnumerationPlainRows");
                entity.HasKey(row => row.Id);
                entity.HasMany(row => row.Children).WithOne().HasForeignKey(child => child.ParentId);
            });
        }
    }

    private sealed class Row
    {
        public int Id { get; set; }
        public int GroupId { get; set; }
        public string Name { get; set; } = string.Empty;
        public Document Document { get; set; } = new();
    }

    private sealed class PlainRow
    {
        public int Id { get; set; }
        public List<Child> Children { get; set; } = [];
    }

    private sealed class Child
    {
        public int Id { get; set; }
        public int ParentId { get; set; }
    }

    private sealed class Document
    {
        public List<DocumentItem> Items { get; set; } = [];
    }

    private sealed class DocumentItem
    {
        public Guid Token { get; set; }
    }

    private sealed class SourceProjection
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    private sealed class EntityProjection
    {
        public int Ignored { get; set; }
        public PlainRow Entity { get; set; } = null!;
    }
}
