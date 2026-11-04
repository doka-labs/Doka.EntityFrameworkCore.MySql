namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlMaterializedSourceIntegrationTests
{
    private const string NameCollisionUnderscores =
        "________________________________________________________________";

    private const string RawNameCollisionSql = "SELECT * FROM `" + ItemTable + "` /* __doka_source_0"
        + NameCollisionUnderscores + NameCollisionUnderscores
        + NameCollisionUnderscores + NameCollisionUnderscores
        + NameCollisionUnderscores + NameCollisionUnderscores
        + NameCollisionUnderscores + NameCollisionUnderscores + " */";

    /// <summary>Verifies the raw sql collision has bounded identifier contract against the live database.</summary>
    /// <param name="cancellationToken">The cancellation token to observe during database work.</param>
    private static async Task AssertRawSqlCollisionHasBoundedIdentifierAsync(
        IntegrationDatabaseTarget target,
        CancellationToken cancellationToken
    )
    {
        // Arrange
        await using var fixture = await CreateFixtureAsync(target, cancellationToken);
        // WHY: This trusted comment contains every short underscore-appended name
        // and exposes identifier growth without relying on an invalid SQL source.

        // Act
        var query = fixture.Context.Items.FromSqlRaw(RawNameCollisionSql)
            .Select(row => row.Id)
            .WithMaterializedSource(source => source.Concat(source))
            .OrderBy(id => id);

        var actual = await query.ToArrayAsync(cancellationToken);

        var expected = Enumerable.Range(1, 32).SelectMany(id => new[] { id, id });

        // Assert
        Assert.Equal(expected, actual);
        Assert.Equal(1, fixture.Probe.Count);
    }
}
