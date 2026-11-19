namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

public sealed partial class MySqlIndexedSelectTests
{
    /// <summary>Builds the real typed LINQ aggregate and collection composition for one independent data row.</summary>
    private static IQueryable GroupAggregateQuery(
        IndexedContext context,
        string aggregate,
        bool indexed
    )
    {
        var groups = context.Rows.GroupBy(row => row.AccountId);

        return (aggregate, indexed) switch
        {
            ("Count", false) => groups.Select(group => new
            {
                group.Key,
                Value = group.Count(),
                Items = group
                    .OrderBy(row => row.Id)
                    .Select(row => row.Id)
                    .ToList(),
            }),
            ("Count", true) => groups.Select(group => new
            {
                group.Key,
                Value = group.Count(),
                Items = group
                    .OrderBy(row => row.Id)
                    .Select((row, index) => new
                    {
                        row.Id,
                        Index = index,
                    })
                    .ToList(),
            }),
            ("LongCount", false) => groups.Select(group => new
            {
                group.Key,
                Value = group.LongCount(),
                Items = group
                    .OrderBy(row => row.Id)
                    .Select(row => row.Id)
                    .ToList(),
            }),
            ("LongCount", true) => groups.Select(group => new
            {
                group.Key,
                Value = group.LongCount(),
                Items = group
                    .OrderBy(row => row.Id)
                    .Select((row, index) => new
                    {
                        row.Id,
                        Index = index,
                    })
                    .ToList(),
            }),
            ("Sum", false) => groups.Select(group => new
            {
                group.Key,
                Value = group.Sum(row => row.Amount),
                Items = group
                    .OrderBy(row => row.Id)
                    .Select(row => row.Id)
                    .ToList(),
            }),
            ("Sum", true) => groups.Select(group => new
            {
                group.Key,
                Value = group.Sum(row => row.Amount),
                Items = group
                    .OrderBy(row => row.Id)
                    .Select((row, index) => new
                    {
                        row.Id,
                        Index = index,
                    })
                    .ToList(),
            }),
            ("Average", false) => groups.Select(group => new
            {
                group.Key,
                Value = group.Average(row => row.Amount),
                Items = group
                    .OrderBy(row => row.Id)
                    .Select(row => row.Id)
                    .ToList(),
            }),
            ("Average", true) => groups.Select(group => new
            {
                group.Key,
                Value = group.Average(row => row.Amount),
                Items = group
                    .OrderBy(row => row.Id)
                    .Select((row, index) => new
                    {
                        row.Id,
                        Index = index,
                    })
                    .ToList(),
            }),
            ("Min", false) => groups.Select(group => new
            {
                group.Key,
                Value = group.Min(row => row.Amount),
                Items = group
                    .OrderBy(row => row.Id)
                    .Select(row => row.Id)
                    .ToList(),
            }),
            ("Min", true) => groups.Select(group => new
            {
                group.Key,
                Value = group.Min(row => row.Amount),
                Items = group
                    .OrderBy(row => row.Id)
                    .Select((row, index) => new
                    {
                        row.Id,
                        Index = index,
                    })
                    .ToList(),
            }),
            ("Max", false) => groups.Select(group => new
            {
                group.Key,
                Value = group.Max(row => row.Amount),
                Items = group
                    .OrderBy(row => row.Id)
                    .Select(row => row.Id)
                    .ToList(),
            }),
            ("Max", true) => groups.Select(group => new
            {
                group.Key,
                Value = group.Max(row => row.Amount),
                Items = group
                    .OrderBy(row => row.Id)
                    .Select((row, index) => new
                    {
                        row.Id,
                        Index = index,
                    })
                    .ToList(),
            }),
            _ => throw new ArgumentOutOfRangeException(nameof(aggregate), aggregate, "Unknown aggregate contract."),
        };
    }

    /// <summary>Uses a separate model whose tenant filter must survive the completed index scope.</summary>
    private static FilteredIndexedContext CreateFilteredContext(
        bool mariaDb
    ) => new(MySqlFunctionalTestOptions
        .CreateTransientBuilder<FilteredIndexedContext>()
        .UseMySql("Server=127.0.0.1;Port=1;Database=filtered_index_offline;User ID=unused;Pooling=false;",
            mariaDb
                ? MySqlServerVersion.MariaDb(new Version(12, 3, 3))
                : MySqlServerVersion.MySql(new Version(8, 4, 0)))
        .Options);

    private sealed class FilteredIndexedContext : DbContext
    {
        /// <summary>Initializes the context with its supplied options and model settings.</summary>
        public FilteredIndexedContext(
            DbContextOptions<FilteredIndexedContext> options
        ) : base(options) { }

        public int TenantId { get; set; } = 1;
        internal DbSet<IndexedRow> Rows => Set<IndexedRow>();

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            ConfigureIndexedRows(modelBuilder);
            modelBuilder
                .Entity<IndexedRow>()
                .HasQueryFilter(row => row.AccountId == TenantId);
        }
    }
}
