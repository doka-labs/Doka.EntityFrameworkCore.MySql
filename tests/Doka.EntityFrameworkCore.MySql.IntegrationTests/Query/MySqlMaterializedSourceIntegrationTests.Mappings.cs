namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlMaterializedSourceIntegrationTests
{
    private const string MappingTable = "IntBoundMappingItems";
    private const string MappingExtraTable = "IntBoundMappingExtra";

    private static async Task AssertMappingRoundtripAsync<TNode>(
        IntegrationDatabaseTarget target
    )
        where TNode : class
    {
        await using var fixture = await CreateMappingFixtureAsync<TNode>(target);
        var query = CreateMappingQuery(fixture.Context)
            .OrderBy(row => row.Tenant)
            .ThenBy(row => row.Id);

        var actual = await query.ToArrayAsync(CancellationToken.None);

        Assert.Equal(2, actual.Length);
        Assert.Equal(["value-1", "value-2"], actual.Select(row => row.Value));
        Assert.Equal(["detail-1", "detail-2"], actual.Select(row => row.Detail));
        Assert.All(actual, row => Assert.Equal(row.Detail, row.OtherDetail));
        Assert.Equal(1, fixture.Probe.Count);
    }

    private static IQueryable<MappingRow> CreateMappingQuery<TNode>(
        MappingContext<TNode> context
    )
        where TNode : class
    {
        if (typeof(TNode) == typeof(OwnedMappedItem))
        {
            var source = context.Set<OwnedMappedItem>().Select(node => new MappingRow
            {
                Id = node.Id,
                Tenant = string.Empty,
                Value = node.Value,
                Detail = node.Contact.Detail,
            });

            return BindProjectedMapping(source);
        }

        if (typeof(TNode) == typeof(ShadowMappedItem))
        {
            var source = context.Set<ShadowMappedItem>().Select(node => new MappingRow
            {
                Id = EF.Property<int>(node, "Id"),
                Tenant = EF.Property<string>(node, "Tenant"),
                Value = node.Value,
                Detail = node.Detail,
            });

            return BindProjectedMapping(source);
        }

        // WHY: Keeping the inherited/split entity as the bound source exercises EF's
        // structural shaper before the final result is narrowed to ordinary scalars.
        return context.Set<TNode>().WithMaterializedSource(source => source.Join(
            source,
            left => EF.Property<int>(left, "Id"),
            right => EF.Property<int>(right, "Id"),
            (left, right) => new MappingRow
            {
                Id = EF.Property<int>(left, "Id"),
                Tenant = string.Empty,
                Value = EF.Property<string>(left, "Value"),
                Detail = EF.Property<string>(left, "Detail"),
                OtherDetail = EF.Property<string>(right, "Detail"),
            }));
    }

    private static IQueryable<MappingRow> BindProjectedMapping(
        IQueryable<MappingRow> source
    ) => source.WithMaterializedSource(bound => bound.Join(
        bound,
        left => new { left.Tenant, left.Id },
        right => new { right.Tenant, right.Id },
        (left, right) => new MappingRow
        {
            Id = left.Id,
            Tenant = left.Tenant,
            Value = left.Value,
            Detail = left.Detail,
            OtherDetail = right.Detail,
        }));

    private static async Task<MappingFixture<TNode>> CreateMappingFixtureAsync<TNode>(
        IntegrationDatabaseTarget target
    )
        where TNode : class
    {
        var probe = new BindingCommandProbe();
        var options = IntegrationTestDbContextOptions.Create<MappingContext<TNode>>()
            .UseMySql(
                IntegrationTestEnvironment.GetConnectionString(target),
                IntegrationTestEnvironment.GetServerVersion(target))
            .AddInterceptors(probe)
            .Options;

        var context = new MappingContext<TNode>(options);

        try
        {
            await DropMappingTablesAsync(context);
            await context.Database.ExecuteSqlRawAsync(context.Database.GenerateCreateScript(), CancellationToken.None);
            SeedMapping(context);
            await context.SaveChangesAsync(CancellationToken.None);
            context.ChangeTracker.Clear();
            probe.Reset();

            return new MappingFixture<TNode>(context, probe);
        }
        catch
        {
            await context.DisposeAsync();

            throw;
        }
    }

    private static void SeedMapping<TNode>(
        MappingContext<TNode> context
    )
        where TNode : class
    {
        for (var id = 1; id <= 2; id++)
        {
            var value = $"value-{id}";
            var detail = $"detail-{id}";

            if (typeof(TNode) == typeof(TptLeaf))
            {
                context.Add(new TptLeaf { Id = id, Value = value, Detail = detail });
            }
            else if (typeof(TNode) == typeof(TpcRoot))
            {
                TpcRoot item = id == 1 ? new TpcFirst() : new TpcSecond();
                item.Id = id;
                item.Value = value;
                item.Detail = detail;
                context.Add(item);
            }
            else if (typeof(TNode) == typeof(SplitMappedItem))
            {
                context.Add(new SplitMappedItem { Id = id, Value = value, Detail = detail });
            }
            else if (typeof(TNode) == typeof(SharedMappedDetail))
            {
                context.Add(new SharedMappedOwner
                {
                    Id = id,
                    Payload = $"owner-{id}",
                    Details = new SharedMappedDetail { Id = id, Value = value, Detail = detail },
                });
            }
            else if (typeof(TNode) == typeof(OwnedMappedItem))
            {
                context.Add(new OwnedMappedItem
                {
                    Id = id,
                    Value = value,
                    Contact = new OwnedMappedContact { Detail = detail },
                });
            }
            else if (typeof(TNode) == typeof(ShadowMappedItem))
            {
                var entry = context.Entry(new ShadowMappedItem { Value = value, Detail = detail });
                entry.Property("Tenant").CurrentValue = $"tenant-{id}";
                entry.Property("Id").CurrentValue = 1;
                entry.State = EntityState.Added;
            }
        }
    }

    private static Task<int> DropMappingTablesAsync(
        DbContext context
    ) => context.Database.ExecuteSqlRawAsync(
        $"DROP TABLE IF EXISTS `{MappingExtraTable}`; DROP TABLE IF EXISTS `{MappingTable}`;",
        CancellationToken.None);

    private sealed class MappingFixture<TNode>(
        MappingContext<TNode> context,
        BindingCommandProbe probe
    ) : IAsyncDisposable
        where TNode : class
    {
        public MappingContext<TNode> Context { get; } = context;

        public BindingCommandProbe Probe { get; } = probe;

        public async ValueTask DisposeAsync()
        {
            try
            {
                await DropMappingTablesAsync(Context);
            }
            finally
            {
                await Context.DisposeAsync();
            }
        }
    }

    private sealed class MappingRow
    {
        public int Id { get; set; }

        public string Tenant { get; set; } = string.Empty;

        public string Value { get; set; } = string.Empty;

        public string Detail { get; set; } = string.Empty;

        public string OtherDetail { get; set; } = string.Empty;
    }
}
