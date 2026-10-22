namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlMaterializedSourceIntegrationTests
{
    private static async Task AssertVolatileValuesSharedAsync(
        IntegrationDatabaseTarget target
    )
    {
        // Arrange
        await using var fixture = await CreateFixtureAsync(target);

        // Act
        var query = fixture.Context.Items
            .Where(item => item.Id <= 8)
            .Select(item => new BoundSample
            {
                Id = item.Id,
                Stamp = Guid.NewGuid(),
                Sample = EF.Functions.Random(),
            })
            .WithMaterializedSource(source => source.Join(
                source,
                left => left.Id,
                right => right.Id,
                (left, right) => new BoundPair
                {
                    LeftId = left.Id,
                    RightId = right.Id,
                    LeftStamp = left.Stamp,
                    RightStamp = right.Stamp,
                    LeftSample = left.Sample,
                    RightSample = right.Sample,
                }));

        var actual = await query.ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Equal(8, actual.Length);
        Assert.Equal(8, actual.Select(pair => pair.LeftStamp).Distinct().Count());
        Assert.All(actual, pair =>
        {
            Assert.Equal(pair.LeftId, pair.RightId);
            Assert.NotEqual(Guid.Empty, pair.LeftStamp);
            Assert.Equal(pair.LeftStamp, pair.RightStamp);
            Assert.Equal(pair.LeftSample, pair.RightSample);
        });
        Assert.Equal(1, fixture.Probe.Count);
    }

    private static async Task AssertDisjointArmsShareRandomPageAsync(
        IntegrationDatabaseTarget target
    )
    {
        // Arrange
        await using var fixture = await CreateFixtureAsync(target);
        // WHY: The second join makes differing random pages or recomputed source values observable
        // even when the two UNION ALL arms select disjoint rows. Nine guaranteed candidates
        // keep the page size deterministic while the remaining eligibility is volatile.

        // Act
        var query = fixture.Context.Items
            .Where(item => item.Id <= 9 || EF.Functions.Random() < 0.5)
            .OrderBy(_ => EF.Functions.Random())
            .Skip(2)
            .Take(7)
            .Select(item => new BoundSample
            {
                Id = item.Id,
                Stamp = Guid.NewGuid(),
                Sample = EF.Functions.Random(),
            })
            .WithMaterializedSource(source => source.Where(item => item.Id % 2 == 0)
                .Concat(source.Where(item => item.Id % 2 != 0))
                .Join(
                    source,
                    left => left.Id,
                    right => right.Id,
                    (left, right) => new BoundPair
                    {
                        LeftId = left.Id,
                        RightId = right.Id,
                        LeftStamp = left.Stamp,
                        RightStamp = right.Stamp,
                        LeftSample = left.Sample,
                        RightSample = right.Sample,
                    }));

        var actual = await query.ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Equal(7, actual.Length);
        Assert.Equal(7, actual.Select(pair => pair.LeftId).Distinct().Count());
        Assert.All(actual, pair =>
        {
            Assert.Equal(pair.LeftStamp, pair.RightStamp);
            Assert.Equal(pair.LeftSample, pair.RightSample);
        });
        Assert.Equal(1, fixture.Probe.Count);
    }

    private static async Task AssertDuplicateOccurrencesAsync(
        IntegrationDatabaseTarget target
    )
    {
        // Arrange
        await using var fixture = await CreateFixtureAsync(target);
        var occurrence = fixture.Context.Items.Where(item => item.Id == 1).Select(_ => 7);

        // Act
        var query = occurrence.Concat(occurrence)
            .WithMaterializedSource(source => source.Join(source, left => left, right => right, (left, _) => left));

        var actual = await query.ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Equal([7, 7, 7, 7], actual);
        Assert.Equal(1, fixture.Probe.Count);
    }

    private static async Task AssertReferenceFiltersIndependentAsync(
        IntegrationDatabaseTarget target
    )
    {
        // Arrange
        await using var fixture = await CreateFixtureAsync(target);

        // Act
        var query = fixture.Context.Items.Where(item => item.Id <= 4)
            .WithMaterializedSource(source => source.Where(left => left.Id <= 2)
                .SelectMany(_ => source.Where(right => right.Id >= 2), (left, right) => new BoundPair
                {
                    LeftId = left.Id,
                    RightId = right.Id,
                }))
            .OrderBy(pair => pair.LeftId)
            .ThenBy(pair => pair.RightId);

        var actual = await query.ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Equal([1, 1, 1, 2, 2, 2], actual.Select(pair => pair.LeftId));
        Assert.Equal([2, 3, 4, 2, 3, 4], actual.Select(pair => pair.RightId));
        Assert.Equal(1, fixture.Probe.Count);
    }

    private static async Task AssertEmptyEnvelopeAsync(
        IntegrationDatabaseTarget target
    )
    {
        // Arrange
        await using var fixture = await CreateFixtureAsync(target);
        var context = fixture.Context;

        // Act
        var query = context.Items.Where(item => item.Id < 0)
            .WithMaterializedSource(source => context.Items.Where(item =>
                item.Id >= source.Min(row => (int?)row.Id)
                && item.Id <= source.Max(row => (int?)row.Id)));

        var actual = await query.ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Empty(actual);
        Assert.Equal(1, fixture.Probe.Count);
    }

    private static async Task AssertIndependentBindingsAsync(
        IntegrationDatabaseTarget target
    )
    {
        // Arrange
        await using var fixture = await CreateFixtureAsync(target);
        var context = fixture.Context;

        // Act
        var query = context.Items.Where(item => item.Id == 1)
            .Select(item => new BoundSample { Id = item.Id, Stamp = Guid.NewGuid() })
            .WithMaterializedSource(left => context.Items.Where(item => item.Id == 1)
                .Select(item => new BoundSample { Id = item.Id, Stamp = Guid.NewGuid() })
                .WithMaterializedSource(right => left.Join(
                    right,
                    first => first.Id,
                    second => second.Id,
                    (first, second) => new BoundPair
                    {
                        LeftStamp = first.Stamp,
                        RightStamp = second.Stamp,
                    })));

        var actual = await query.SingleAsync(CancellationToken.None);

        // Assert
        Assert.NotEqual(Guid.Empty, actual.LeftStamp);
        Assert.NotEqual(Guid.Empty, actual.RightStamp);
        Assert.NotEqual(actual.LeftStamp, actual.RightStamp);
        Assert.Equal(1, fixture.Probe.Count);
    }

    private static async Task AssertNativeValuesAsync(
        IntegrationDatabaseTarget target
    )
    {
        // Arrange
        await using var fixture = await CreateFixtureAsync(target);
        var expectedCode = new BoundCode("code-1");

        // Act
        var query = fixture.Context.Items
            .Select(item => new
            {
                item.Id,
                item.Name,
                item.Token,
                item.OptionalScore,
                item.Code,
            })
            .WithMaterializedSource(source => source.Where(item =>
                item.Token == s_binaryToken
                && item.OptionalScore == null
                && item.Name == "alpha"
                && item.Code == expectedCode));

        var actual = await query.SingleAsync(CancellationToken.None);

        // Assert
        Assert.Equal(1, actual.Id);
        Assert.Equal("ALPHA", actual.Name);
        Assert.Equal(s_binaryToken, actual.Token);
        Assert.Null(actual.OptionalScore);
        Assert.Equal(expectedCode.Value, actual.Code.Value);
        Assert.Equal(1, fixture.Probe.Count);
        Assert.DoesNotContain("JSON_TABLE", fixture.Probe.CommandText, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task AssertDependentBindingAsync(
        IntegrationDatabaseTarget target
    )
    {
        // Arrange
        await using var fixture = await CreateFixtureAsync(target);

        // Act
        var query = fixture.Context.Items.Where(item => item.Id <= 4)
            .Select(item => new BoundSample { Id = item.Id, Stamp = Guid.NewGuid() })
            .WithMaterializedSource(outer => outer.Where(item => item.Id <= 2)
                .WithMaterializedSource(inner => outer.Join(
                    inner,
                    left => left.Id,
                    right => right.Id,
                    (left, right) => new BoundPair
                    {
                        LeftId = left.Id,
                        RightId = right.Id,
                        LeftStamp = left.Stamp,
                        RightStamp = right.Stamp,
                    })))
            .OrderBy(pair => pair.LeftId);

        var actual = await query.ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Equal([1, 2], actual.Select(pair => pair.LeftId));
        Assert.All(actual, pair =>
        {
            Assert.Equal(pair.LeftId, pair.RightId);
            Assert.NotEqual(Guid.Empty, pair.LeftStamp);
            Assert.Equal(pair.LeftStamp, pair.RightStamp);
        });
        Assert.Equal(1, fixture.Probe.Count);
    }
}
