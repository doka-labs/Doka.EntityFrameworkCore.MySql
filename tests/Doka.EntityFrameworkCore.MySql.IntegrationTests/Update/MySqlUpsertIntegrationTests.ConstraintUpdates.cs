namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlUpsertIntegrationTests
{
    /// <summary>Reports independent update-side foreign-key, check and converted-null constraints.</summary>
    public static TheoryData<string, string> ConstraintUpdateCases
    {
        get
        {
            var data = new TheoryData<string, string>();

            foreach (var target in SelectedTargets)
            {
                data.Add(target, "foreign");
                data.Add(target, "check");
                data.Add(target, "null-converter");
            }

            return data;
        }
    }

    /// <summary>Native constraints also apply to the update half and leave the original conflicted row intact.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(ConstraintUpdateCases))]
    public async Task Native_constraint_violation_during_duplicate_update_preserves_the_original_row(
        string target,
        string constraint
    )
    {
        // Arrange
        await using var store = await UpsertStore.CreateAsync(target,
            constraint == "foreign"
                ? "ordinary"
                : constraint,
            CancellationToken.None);

        if (constraint == "foreign")
        {
            await ExecuteNativeAsync(store.Observer,
                "CREATE TABLE `UpsertParent` (`Id` int NOT NULL PRIMARY KEY) ENGINE=InnoDB; INSERT INTO `UpsertParent` VALUES (1); ALTER TABLE `UpsertRows` ADD CONSTRAINT `FK_Upsert_Number` FOREIGN KEY (`Number`) REFERENCES `UpsertParent` (`Id`);",
                CancellationToken.None);
        }

        await using (var seed = store.CreateContext())
        {
            seed.Add(NewRow(1, "original"));
            await seed.SaveChangesAsync(CancellationToken.None);
        }

        var capture = new Writes();
        await using var context = store.CreateContext(capture);
        var input = NewRow(1,
            constraint == "null-converter"
                ? "force-null"
                : "must-not-persist");

        input.Number = constraint == "check"
            ? -1
            : 2;

        var expectedCode = constraint switch
        {
            "foreign" => 1452,
            "check" => store.Version.IsMariaDb
                ? 4025
                : 3819,
            "null-converter" => 1048,
            _ => throw new ArgumentOutOfRangeException(nameof(constraint)),
        };

        // Act
        var error = await Record.ExceptionAsync(() => context.ExecuteUpsertAsync(input,
            new[] { nameof(UpsertRow.Name), nameof(UpsertRow.Number) },
            CancellationToken.None));

        var persisted = await context
            .Rows
            .AsNoTracking()
            .SingleAsync(CancellationToken.None);

        // Assert
        Assert.Equal(expectedCode,
            Assert.IsType<MySqlException>(error)
                .Number);

        Assert.Equal("original", persisted.Name);
        Assert.Equal(1, persisted.Number);
        Assert.Equal(2, persisted.Computed);
        Assert.Single(capture.Statements);
    }

    /// <summary>A tenant-aware alternate unique key does not make an additional tenantless primary key safe.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Tenant_scoped_unique_index_does_not_authorize_a_tenantless_primary_key_conflict(
        string target
    )
    {
        // Arrange
        await using var store = await UpsertStore.CreateAsync(target, "tenant-unique-only", CancellationToken.None);
        var existing = NewRow(1, "other-tenant");
        existing.TenantId = 8;
        await using (var seed = store.CreateContext())
        {
            seed.Add(existing);
            await seed.SaveChangesAsync(CancellationToken.None);
        }

        await using var context = store.CreateContext();
        var entityType = context.Model.FindEntityType(typeof(UpsertRow))!;
        var input = NewRow(1, "native-primary-key-conflict");

        // Act
        var affected =
            await context.ExecuteUpsertAsync(input, new[] { nameof(UpsertRow.Name) }, CancellationToken.None);

        var persisted = await context
            .Rows
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleAsync(CancellationToken.None);

        var visibleCount = await context.Rows.CountAsync(CancellationToken.None);

        // Assert
        Assert.Equal(nameof(UpsertRow.Id),
            Assert.Single(entityType.FindPrimaryKey()!.Properties)
                .Name);

        Assert.Contains(entityType.GetIndexes(),
            index => index.IsUnique
                && index.Properties.Count == 2
                && index.Properties[0].Name == nameof(UpsertRow.TenantId));

        Assert.Equal(2, affected);
        Assert.Equal(8, persisted.TenantId);
        Assert.Equal("native-primary-key-conflict", persisted.Name);
        Assert.Equal(0, visibleCount);
        Assert.Equal(7, input.TenantId);
    }
}
