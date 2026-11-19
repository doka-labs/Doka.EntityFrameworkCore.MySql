namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlUpsertIntegrationTests
{
    /// <summary>Reports conflicts that hit the same or different native unique-index rows.</summary>
    public static TheoryData<string, bool> UniqueConflictCases
    {
        get
        {
            var data = new TheoryData<string, bool>();

            foreach (var target in SelectedTargets)
            {
                data.Add(target, false);
                data.Add(target, true);
            }

            return data;
        }
    }

    /// <summary>BEFORE triggers with a required computed column retain the engine's independently reproduced behavior.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Before_trigger_and_required_computed_column_match_the_independent_native_statement(
        string target
    )
    {
        // Arrange
        await using var store = await UpsertStore.CreateAsync(target, "ordinary", CancellationToken.None);

        // WHY: MariaDB's generated-column grammar does not accept this MySQL NOT NULL
        // clause. Mirror each engine's actual generated-column DDL in the independent control.
        var generatedColumn = store.Version.IsMariaDb
            ? "`Computed` int GENERATED ALWAYS AS (`Number`+1) STORED"
            : "`Computed` int GENERATED ALWAYS AS (`Number`+1) STORED NOT NULL";

        await ExecuteNativeAsync(store.Observer,
            "CREATE TABLE `BeforeAudit` (`Kind` char(1)); CREATE TRIGGER `UpsertBeforeInsert` BEFORE INSERT ON `UpsertRows` FOR EACH ROW INSERT INTO `BeforeAudit` VALUES ('D'); CREATE TABLE `NativeBefore` (`Id` int NOT NULL PRIMARY KEY,`Number` int NOT NULL,"
            + generatedColumn
            + ") ENGINE=InnoDB; CREATE TRIGGER `NativeBeforeInsert` BEFORE INSERT ON `NativeBefore` FOR EACH ROW INSERT INTO `BeforeAudit` VALUES ('N');",
            CancellationToken.None);

        var capture = new Writes();
        await using var context = store.CreateContext(capture);
        var first = NewRow(1);
        var second = NewRow(1);
        second.Number = 2;
        await using var native =
            new MySqlCommand(
                "INSERT INTO `NativeBefore` (`Id`,`Number`) VALUES (1,1),(1,2) ON DUPLICATE KEY UPDATE `Number`=2;",
                store.Observer);

        var nativeAffected = 0;
        long actualAffected = 0;

        // Act
        var expectedError = await Record.ExceptionAsync(async ()
            => nativeAffected = await native.ExecuteNonQueryAsync(CancellationToken.None));

        var actualError = await Record.ExceptionAsync(async () => actualAffected =
            await context.ExecuteUpsertRangeAsync(
                new[] { first, second },
                new[] { nameof(UpsertRow.Number) },
                CancellationToken.None));

        var persisted = await context
            .Rows
            .AsNoTracking()
            .ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Single(capture.Statements);

        if (expectedError is not null)
        {
            // WHY: MySQL also rejects a plain native INSERT for this required-generated-column
            // plus BEFORE-trigger shape. Preserve its constraint error instead of rewriting schema.
            Assert.Equal(1048,
                Assert.IsType<MySqlException>(expectedError)
                    .Number);

            Assert.Equal(1048,
                Assert.IsType<MySqlException>(actualError)
                    .Number);

            Assert.Empty(persisted);
        }
        else
        {
            Assert.Null(actualError);
            Assert.Equal(nativeAffected, actualAffected);
            Assert.Equal(2,
                Assert.Single(persisted)
                    .Number);

            Assert.Equal(3, persisted[0].Computed);
        }
    }

    /// <summary>Native all-unique-index conflict selection agrees with an independent explicit native statement.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(UniqueConflictCases))]
    public async Task Multiple_unique_conflicts_follow_the_native_statement_without_a_named_conflict_target(
        string target,
        bool differentRows
    )
    {
        // Arrange
        await using var store = await UpsertStore.CreateAsync(target, "unique-control", CancellationToken.None);
        await ExecuteNativeAsync(store.Observer,
            "INSERT INTO `UpsertUnique` (`Id`,`First`,`Second`,`Value`) VALUES (1,'a','x','first'),(2,'b','y','second'); CREATE TABLE `UpsertNative` LIKE `UpsertUnique`; INSERT INTO `UpsertNative` SELECT * FROM `UpsertUnique`;",
            CancellationToken.None);

        var input = new UniqueRow
        {
            Id = 99,
            First = "a",
            Second = differentRows
                ? "y"
                : "x",
            Value = "changed",
        };

        var capture = new Writes();
        await using var context = store.CreateContext(capture);
        await using var native = new MySqlCommand(
            "INSERT INTO `UpsertNative` (`Id`,`First`,`Second`,`Value`) VALUES (@id,@first,@second,@value) ON DUPLICATE KEY UPDATE `Value`=@value;",
            store.Observer);

        native.Parameters.AddWithValue("@id", input.Id);
        native.Parameters.AddWithValue("@first", input.First);
        native.Parameters.AddWithValue("@second", input.Second);
        native.Parameters.AddWithValue("@value", input.Value);

        // Act
        var expectedCount = await native.ExecuteNonQueryAsync(CancellationToken.None);
        var actualCount =
            await context.ExecuteUpsertAsync(input, new[] { nameof(UniqueRow.Value) }, CancellationToken.None);

        var actual = await context
            .Set<UniqueRow>()
            .AsNoTracking()
            .OrderBy(row => row.Id)
            .ToArrayAsync(CancellationToken.None);

        var expected = await context
            .Set<UniqueRow>()
            .FromSqlRaw("SELECT * FROM `UpsertNative`")
            .AsNoTracking()
            .OrderBy(row => row.Id)
            .ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Equal(expectedCount, actualCount);
        Assert.Equal(expected.Select(row => (row.Id, row.First, row.Second, row.Value)),
            actual.Select(row => (row.Id, row.First, row.Second, row.Value)));

        Assert.Equal(99, input.Id);
        Assert.Single(capture.Statements);
        Assert.Equal(3, capture.AllStatements.Count);
    }

    /// <summary>Conflicting updates across distinct unique rows retain the native constraint error and unchanged rows.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Updating_both_conflicting_unique_keys_fails_like_the_explicit_native_statement(
        string target
    )
    {
        // Arrange
        await using var store = await UpsertStore.CreateAsync(target, "unique-control", CancellationToken.None);
        await ExecuteNativeAsync(store.Observer,
            "INSERT INTO `UpsertUnique` (`Id`,`First`,`Second`,`Value`) VALUES (1,'a','x','first'),(2,'b','y','second'); CREATE TABLE `UpsertNative` LIKE `UpsertUnique`; INSERT INTO `UpsertNative` SELECT * FROM `UpsertUnique`;",
            CancellationToken.None);

        await using var context = store.CreateContext();
        var input = new UniqueRow
        {
            Id = 99,
            First = "a",
            Second = "y",
            Value = "changed",
        };

        await using var native = new MySqlCommand(
            "INSERT INTO `UpsertNative` (`Id`,`First`,`Second`,`Value`) VALUES (99,'a','y','changed') ON DUPLICATE KEY UPDATE `First`='a',`Second`='y',`Value`='changed';",
            store.Observer);

        // Act
        var expectedError = await Record.ExceptionAsync(() => native.ExecuteNonQueryAsync(CancellationToken.None));
        var actualError = await Record.ExceptionAsync(() => context.ExecuteUpsertAsync(input,
            new[] { nameof(UniqueRow.First), nameof(UniqueRow.Second), nameof(UniqueRow.Value) },
            CancellationToken.None));

        var actual = await context
            .Set<UniqueRow>()
            .AsNoTracking()
            .OrderBy(row => row.Id)
            .ToArrayAsync(CancellationToken.None);

        var expected = await context
            .Set<UniqueRow>()
            .FromSqlRaw("SELECT * FROM `UpsertNative`")
            .AsNoTracking()
            .OrderBy(row => row.Id)
            .ToArrayAsync(CancellationToken.None);

        // Assert
        Assert.Equal(1062,
            Assert.IsType<MySqlException>(expectedError)
                .Number);

        Assert.Equal(1062,
            Assert.IsType<MySqlException>(actualError)
                .Number);

        Assert.Equal(expected.Select(row => (row.Id, row.First, row.Second, row.Value)),
            actual.Select(row => (row.Id, row.First, row.Second, row.Value)));

        Assert.Equal(99, input.Id);
    }

    /// <summary>Insert and duplicate-update trigger behavior is left to the native engine without hidden readback.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Native_insert_and_update_triggers_observe_the_actual_statement_once(
        string target
    )
    {
        // Arrange
        await using var store = await UpsertStore.CreateAsync(target, "ordinary", CancellationToken.None);
        await ExecuteNativeAsync(store.Observer,
            "CREATE TABLE `UpsertAudit` (`Kind` char(1) NOT NULL); CREATE TRIGGER `UpsertAfterInsert` AFTER INSERT ON `UpsertRows` FOR EACH ROW INSERT INTO `UpsertAudit` VALUES ('I'); CREATE TRIGGER `UpsertAfterUpdate` AFTER UPDATE ON `UpsertRows` FOR EACH ROW INSERT INTO `UpsertAudit` VALUES ('U');",
            CancellationToken.None);

        var capture = new Writes();
        await using var context = store.CreateContext(capture);
        var inputs = new[] { NewRow(1, "first"), NewRow(1, "second") };
        await using var audit =
            new MySqlCommand("SELECT COUNT(*) FROM `UpsertAudit` WHERE `Kind`=@kind;", store.Observer);

        var kind = audit.Parameters.AddWithValue("@kind", "I");

        // Act
        var affected =
            await context.ExecuteUpsertRangeAsync(inputs, new[] { nameof(UpsertRow.Name) }, CancellationToken.None);

        var insertEvents = Convert.ToInt32(await audit.ExecuteScalarAsync(CancellationToken.None),
            CultureInfo.InvariantCulture);

        kind.Value = "U";
        var updateEvents = Convert.ToInt32(await audit.ExecuteScalarAsync(CancellationToken.None),
            CultureInfo.InvariantCulture);

        var persisted = await context
            .Rows
            .AsNoTracking()
            .SingleAsync(CancellationToken.None);

        // Assert
        Assert.Equal(3, affected);
        Assert.Equal(1, insertEvents);
        Assert.Equal(1, updateEvents);
        Assert.Equal("second", persisted.Name);
        Assert.Single(capture.Statements);
        Assert.Equal(2, capture.AllStatements.Count);
    }

    /// <summary>A native foreign-key failure remains a native error without persisting an invalid candidate.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Foreign_key_failure_is_not_converted_into_a_successful_upsert(
        string target
    )
    {
        // Arrange
        await using var store = await UpsertStore.CreateAsync(target, "ordinary", CancellationToken.None);
        await ExecuteNativeAsync(store.Observer,
            "CREATE TABLE `UpsertParent` (`Id` int NOT NULL PRIMARY KEY) ENGINE=InnoDB; INSERT INTO `UpsertParent` VALUES (1); ALTER TABLE `UpsertRows` ADD CONSTRAINT `FK_Upsert_Number` FOREIGN KEY (`Number`) REFERENCES `UpsertParent` (`Id`);",
            CancellationToken.None);

        var capture = new Writes();
        await using var context = store.CreateContext(capture);
        var input = NewRow(2);

        // Act
        var error = await Record.ExceptionAsync(()
            => context.ExecuteUpsertAsync(input, new[] { nameof(UpsertRow.Name) }, CancellationToken.None));

        var count = await context.Rows.CountAsync(CancellationToken.None);

        // Assert
        Assert.Equal(1452,
            Assert.IsType<MySqlException>(error)
                .Number);

        Assert.Equal(0, count);
        Assert.Single(capture.Statements);
        Assert.Equal(2, input.Id);
    }

    /// <summary>A provider-converted NULL still encounters the actual non-nullable target constraint.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(SelectedTargets))]
    public async Task Provider_null_conversion_is_not_silently_replaced_with_a_default(
        string target
    )
    {
        // Arrange
        await using var store = await UpsertStore.CreateAsync(target, "null-converter", CancellationToken.None);
        var capture = new Writes();
        await using var context = store.CreateContext(capture);
        var input = NewRow(1, "force-null");

        // Act
        var error = await Record.ExceptionAsync(()
            => context.ExecuteUpsertAsync(input, new[] { nameof(UpsertRow.Name) }, CancellationToken.None));

        var count = await context.Rows.CountAsync(CancellationToken.None);

        // Assert
        Assert.Equal(1048,
            Assert.IsType<MySqlException>(error)
                .Number);

        Assert.Equal(0, count);
        Assert.Single(capture.Statements);
        Assert.Equal("force-null", input.Name);
    }
}
