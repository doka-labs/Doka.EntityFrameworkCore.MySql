namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

public sealed partial class MySqlMaterializedSourceTranslationTests
{
    private const string NameCollisionUnderscores =
        "________________________________________________________________";

    private const string RawNameCollisionSql = "SELECT * FROM `BindingNodes` /* __doka_source_0"
        + NameCollisionUnderscores + NameCollisionUnderscores
        + NameCollisionUnderscores + NameCollisionUnderscores
        + NameCollisionUnderscores + NameCollisionUnderscores
        + NameCollisionUnderscores + NameCollisionUnderscores + " */";

    /// <summary>A long reserved-name prefix in trusted SQL cannot force an invalid generated identifier.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Raw_sql_name_collision_preserves_identifier_limit(bool mariaDb)
    {
        using var context = CreateContext(mariaDb);
        // WHY: Every underscore-appended prefix already appears in this valid comment.
        // Collision recovery must choose another bounded name rather than grow indefinitely.
        var query = context.Nodes.FromSqlRaw(RawNameCollisionSql)
            .Select(row => row.Id)
            .WithMaterializedSource(source => source.Concat(source));

        var sql = query.ToQueryString();

        const string marker = "WITH RECURSIVE";
        var markerIndex = sql.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(markerIndex >= 0);
        var nameStart = sql.IndexOf('`', markerIndex + marker.Length) + 1;
        Assert.True(nameStart > 0);
        var nameEnd = sql.IndexOf('`', nameStart);
        Assert.True(nameEnd > nameStart);
        var generatedName = sql[nameStart..nameEnd];
        Assert.InRange(generatedName.Length, 1, 64);
        Assert.DoesNotContain(generatedName, RawNameCollisionSql, StringComparison.OrdinalIgnoreCase);
    }
}
