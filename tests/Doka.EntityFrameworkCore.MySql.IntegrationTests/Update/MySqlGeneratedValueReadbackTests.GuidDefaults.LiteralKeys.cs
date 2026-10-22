namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlGeneratedValueReadbackTests
{
    /// <summary>Correlates a literal-default composite key even when its supplied key component is shared.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(GuidDefaultMappingCases))]
    public async Task Guid_sql_default_literal_composite_key_uses_complete_readback_predicate(
        string targetName,
        MySqlGuidFormat format
    )
    {
        // Arrange
        var target = Enum.Parse<IntegrationDatabaseTarget>(targetName);
        var capture = new ReadbackCapture();
        await using var context = CreateGuidDefaultsContext<GuidDefaultsLiteralKeyMode>(target, format, capture);
        await RecreateGuidDefaultsTablesAsync(context);
        var id = CreateGuid(7001);
        var entity = context.Model.FindEntityType(typeof(GuidDefaultLiteralKeyRow))!;
        var idMapping = entity.FindProperty(nameof(GuidDefaultLiteralKeyRow.Id))!.GetRelationalTypeMapping();
        var seedId = new MySqlParameter("seedId", idMapping.Converter?.ConvertToProvider(id) ?? id);
        var flagLiteral = entity.FindProperty(nameof(GuidDefaultLiteralKeyRow.Flag))!
            .GetRelationalTypeMapping().GenerateSqlLiteral(true);

        const string seedSql = "INSERT INTO `" + GuidDefaultLiteralKeyTable
            + "` (`Id`, `Flag`, `Marker`) VALUES (@seedId, FALSE, 41);";

        // WHY: A row with the same GUID and a different bool key makes a
        // partial readback predicate ambiguous instead of accidentally unique.
        await context.Database.ExecuteSqlRawAsync(seedSql, [seedId], CancellationToken.None);
        var row = new GuidDefaultLiteralKeyRow { Id = id, Flag = false, Marker = 73 };
        context.Add(row);
        capture.Enabled = true;

        // Act
        var written = await context.SaveChangesAsync(CancellationToken.None);

        capture.Enabled = false;
        var persisted = await context.Set<GuidDefaultLiteralKeyRow>().AsNoTracking()
            .OrderBy(item => item.Flag).ToListAsync(CancellationToken.None);

        // Assert
        Assert.Equal(1, written);
        Assert.Equal(id, row.Id);
        Assert.True(row.Flag);
        Assert.Equal(73, row.Marker);
        Assert.Equal((73L * 17) + 3, row.Stamp);
        Assert.Collection(persisted,
            seed =>
            {
                Assert.Equal(id, seed.Id);
                Assert.False(seed.Flag);
                Assert.Equal(41, seed.Marker);
                Assert.Equal((41L * 17) + 3, seed.Stamp);
            },
            inserted =>
            {
                Assert.Equal(id, inserted.Id);
                Assert.True(inserted.Flag);
                Assert.Equal(row.Marker, inserted.Marker);
                Assert.Equal(row.Stamp, inserted.Stamp);
            });
        Assert.NotEmpty(GuidDefaultInsertHeaders(capture.Commands, GuidDefaultLiteralKeyTable));
        Assert.All(GuidDefaultInsertHeaders(capture.Commands, GuidDefaultLiteralKeyTable), header =>
        {
            Assert.Contains("`Id`", header, StringComparison.Ordinal);
            Assert.DoesNotContain("`Flag`", header, StringComparison.Ordinal);
        });
        Assert.DoesNotContain(capture.Commands, sql => sql.Contains("LAST_INSERT_ID", StringComparison.Ordinal));

        if (IsGuidDefaultMySqlTarget(target))
        {
            var command = Assert.Single(capture.Commands);
            var whereOffset = command.IndexOf("WHERE ", StringComparison.Ordinal);

            Assert.True(whereOffset >= 0, command);

            var predicate = command.Substring(whereOffset);

            Assert.Contains($"`Flag` = {flagLiteral}", predicate, StringComparison.Ordinal);
            Assert.Contains("`Id` = @", predicate, StringComparison.Ordinal);
            Assert.Contains(" AND ", predicate, StringComparison.Ordinal);
            Assert.DoesNotContain("RETURNING", command, StringComparison.Ordinal);
        }
        else
        {
            Assert.Contains(capture.Commands, sql => sql.Contains("RETURNING", StringComparison.Ordinal));
        }
    }
}
