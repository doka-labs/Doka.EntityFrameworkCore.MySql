namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

public sealed partial class MySqlCteTranslationTests
{
    /// <summary>A foreign context cannot become an implicit second execution owner inside a CTE statement.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Foreign_context_is_rejected_before_database_io(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);
        using var other = CreateContext(mariaDb);
        var query = context.Rows.WithCte(rows =>
            rows.Join(other.Rows, left => left.Id, right => right.Id, (left, _) => left.Id));

        // Act
        var exception = Record.Exception(query.ToQueryString);

        // Assert
        Assert.Contains("context",
            Assert.IsType<InvalidOperationException>(exception)
                .Message,
            StringComparison.OrdinalIgnoreCase);

        Assert.Empty(context.ChangeTracker.Entries());
        Assert.Empty(other.ChangeTracker.Entries());
    }

    /// <summary>A client-only source value cannot be silently deferred to each ordinary CTE reference.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Client_only_source_projection_is_rejected(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);
        var query = context
            .Rows
            .Select(row => new
            {
                row.Id,
                Name = ClientOnly(row.Name),
            })
            .WithCte(rows => rows);

        // Act
        var exception = Record.Exception(query.ToQueryString);

        // Assert
        Assert.IsType<InvalidOperationException>(exception);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>A collection inside the source cannot become an unadvertised client buffer.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Deferred_collection_source_projection_is_rejected(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateContext(mariaDb);
        var query = context
            .Rows
            .Select(row => new
            {
                row.Id,
                Values = context
                    .Rows
                    .Where(other => other.TargetId == row.TargetId)
                    .Select(other => other.Id)
                    .ToList(),
            })
            .WithCte(rows => rows);

        // Act
        var exception = Record.Exception(query.ToQueryString);

        // Assert
        Assert.IsType<InvalidOperationException>(exception);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Provides a deliberate client-only method rather than relying on an unsupported fixture shape.</summary>
    private static string ClientOnly(
        string value
    ) => "client:" + value;
}
