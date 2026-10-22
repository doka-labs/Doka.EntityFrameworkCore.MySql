namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlGeneratedValueReadbackTests
{
    /// <summary>Resolves a shared-column root SQL default for both entries in a new table-split graph.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(GuidDefaultMappingCases))]
    public async Task Guid_sql_default_shared_table_keys_follow_root_store_generation(
        string targetName,
        MySqlGuidFormat format
    )
    {
        // Arrange
        var target = Enum.Parse<IntegrationDatabaseTarget>(targetName);
        var capture = new ReadbackCapture();
        await using var context = CreateGuidDefaultsContext<GuidDefaultsSharedTableMode>(target, format, capture);
        await RecreateGuidDefaultsTablesAsync(context);
        var principal = new GuidDefaultSharedPrincipal { Payload = "principal-marker" };
        var dependent = new GuidDefaultSharedDependent
        {
            Payload = "dependent-marker",
            Principal = principal,
        };

        principal.Dependent = dependent;
        context.Add(principal);
        var principalKey = context.Entry(principal).Property(row => row.Id);
        var dependentKey = context.Entry(dependent).Property(row => row.Id);
        var principalWasTemporary = principalKey.IsTemporary;
        var dependentWasTemporary = dependentKey.IsTemporary;
        var provisionalPrincipalId = principalKey.CurrentValue;
        var provisionalDependentId = dependentKey.CurrentValue;
        capture.Enabled = true;

        // Act
        var exception = await Record.ExceptionAsync(() => context.SaveChangesAsync(CancellationToken.None));

        capture.Enabled = false;
        var persisted = await context.Set<GuidDefaultSharedPrincipal>().AsNoTracking()
            .Include(row => row.Dependent).ToListAsync(CancellationToken.None);

        // Assert
        Assert.True(principalWasTemporary);
        Assert.True(dependentWasTemporary);
        Assert.Equal(provisionalPrincipalId, provisionalDependentId);

        if (IsGuidDefaultMySqlTarget(target))
        {
            var failure = Assert.IsType<InvalidOperationException>(exception);

            Assert.Contains("cannot retrieve", failure.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(GuidDefaultSharedTable, failure.Message, StringComparison.Ordinal);
            Assert.Contains(nameof(GuidDefaultSharedPrincipal.Id), failure.Message, StringComparison.Ordinal);
            Assert.Empty(persisted);
            Assert.DoesNotContain(capture.Commands, sql => sql.Contains("INSERT INTO", StringComparison.Ordinal));
        }
        else
        {
            Assert.Null(exception);

            var stored = Assert.Single(persisted);

            Assert.NotEqual(Guid.Empty, principal.Id);
            Assert.NotEqual(provisionalPrincipalId, principal.Id);
            Assert.Equal(principal.Id, dependent.Id);
            Assert.Equal(principal.Id, stored.Id);
            Assert.NotNull(stored.Dependent);
            Assert.Equal(dependent.Id, stored.Dependent.Id);
            Assert.Equal(principal.Payload, stored.Payload);
            Assert.Equal(dependent.Payload, stored.Dependent.Payload);
            Assert.False(principalKey.IsTemporary);
            Assert.False(dependentKey.IsTemporary);
            Assert.Equal(EntityState.Unchanged, context.Entry(principal).State);
            Assert.Equal(EntityState.Unchanged, context.Entry(dependent).State);
            Assert.Contains(capture.Commands, sql => sql.Contains("RETURNING `Id`", StringComparison.Ordinal));
            Assert.Equal(1, capture.Commands.Sum(sql => CountOccurrences(sql, "INSERT INTO")));
            Assert.NotEmpty(GuidDefaultInsertHeaders(capture.Commands, GuidDefaultSharedTable));
            Assert.All(GuidDefaultInsertHeaders(capture.Commands, GuidDefaultSharedTable), header =>
            {
                Assert.DoesNotContain("`Id`", header, StringComparison.Ordinal);
                Assert.Contains("`PrincipalPayload`", header, StringComparison.Ordinal);
                Assert.Contains("`DependentPayload`", header, StringComparison.Ordinal);
            });
        }
    }
}
