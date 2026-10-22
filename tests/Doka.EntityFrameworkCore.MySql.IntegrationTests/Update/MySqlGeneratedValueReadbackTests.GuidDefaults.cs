namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlGeneratedValueReadbackTests
{
    /// <summary>Reports every supported selected engine and its valid GUID storage mappings.</summary>
    public static IEnumerable<object[]> GuidDefaultMappingCases => CreateGuidDefaultMappingCases();

    /// <summary>Reports MariaDB GUID key mappings with both synchronous and asynchronous saves.</summary>
    public static IEnumerable<object[]> MariaDbGuidDefaultKeyCases => CreateGuidDefaultMappingCases()
        .Where(data => !IsGuidDefaultMySqlTarget(Enum.Parse<IntegrationDatabaseTarget>((string)data[0])))
        .SelectMany(data => new[]
        {
            new[] { data[0], data[1], false },
            new[] { data[0], data[1], true },
        });

    /// <summary>Reports MySQL GUID key mappings with both synchronous and asynchronous saves.</summary>
    public static IEnumerable<object[]> MySqlGuidDefaultKeyCases => CreateGuidDefaultMappingCases()
        .Where(data => IsGuidDefaultMySqlTarget(Enum.Parse<IntegrationDatabaseTarget>((string)data[0])))
        .SelectMany(data => new[]
        {
            new[] { data[0], data[1], false },
            new[] { data[0], data[1], true },
        });

    /// <summary>Replaces temporary keys with server UUIDs and propagates each key to its own dependent.</summary>
    [RequiresDatabaseTargetsTheory(
        IntegrationDatabaseTarget.MariaDb1011,
        IntegrationDatabaseTarget.MariaDb114,
        IntegrationDatabaseTarget.MariaDb118,
        IntegrationDatabaseTarget.MariaDb123)]
    [MemberData(nameof(MariaDbGuidDefaultKeyCases))]
    public async Task Guid_sql_default_keys_use_returning_and_propagate_correlated_foreign_keys(
        string targetName,
        MySqlGuidFormat format,
        bool asyncSave
    )
    {
        // Arrange
        var target = Enum.Parse<IntegrationDatabaseTarget>(targetName);
        var capture = new ReadbackCapture();
        await using var context = CreateGuidDefaultsContext<GuidDefaultsKeyMode>(target, format, capture);
        await RecreateGuidDefaultsTablesAsync(context);
        var rows = Enumerable.Range(1, 24)
            .Select(marker => new GuidDefaultPrincipal { Marker = marker })
            .Reverse()
            .ToArray();

        foreach (var row in rows)
        {
            row.Dependents.Add(new GuidDefaultDependent { Id = row.Marker + 1000, Principal = row });
        }

        context.AddRange(rows);
        var temporaryKeys = rows.Select(row => context.Entry(row).Property(item => item.Id).IsTemporary).ToArray();
        capture.Enabled = true;

        // Act
        int written;

        if (asyncSave)
        {
            written = await context.SaveChangesAsync(CancellationToken.None);
        }
        else
        {
            // WHY: This case verifies the separate synchronous RETURNING result consumer.
            // ReSharper disable once MethodHasAsyncOverload
            written = context.SaveChanges();
        }

        capture.Enabled = false;
        var persisted = await context.Set<GuidDefaultPrincipal>().AsNoTracking()
            .ToDictionaryAsync(row => row.Marker, CancellationToken.None);
        var dependents = await context.Set<GuidDefaultDependent>().AsNoTracking()
            .ToDictionaryAsync(row => row.Id, CancellationToken.None);

        // Assert
        Assert.Equal(48, written);
        Assert.All(temporaryKeys, Assert.True);
        Assert.Equal(24, persisted.Count);
        Assert.Equal(24, dependents.Count);
        Assert.Equal(24, rows.Select(row => row.Id).Distinct().Count());
        Assert.All(rows, row =>
        {
            // WHY: Uniqueness alone cannot expose RETURNING rows assigned to
            // the wrong tracked principal or FK propagation to the wrong child.
            Assert.NotEqual(Guid.Empty, row.Id);
            Assert.Equal(persisted[row.Marker].Id, row.Id);
            Assert.Equal(row.Id, Assert.Single(row.Dependents).PrincipalId);
            Assert.Equal(persisted[row.Marker].Id, dependents[row.Marker + 1000].PrincipalId);
            Assert.False(context.Entry(row).Property(item => item.Id).IsTemporary);
        });
        Assert.Contains(capture.Commands, sql => sql.Contains("RETURNING `Id`", StringComparison.Ordinal));
        Assert.NotEmpty(GuidDefaultInsertHeaders(capture.Commands, GuidDefaultPrincipalTable));
        Assert.All(GuidDefaultInsertHeaders(capture.Commands, GuidDefaultPrincipalTable), header =>
        {
            Assert.Contains("`Marker`", header, StringComparison.Ordinal);
            Assert.DoesNotContain("`Id`", header, StringComparison.Ordinal);
        });
    }

    /// <summary>Rejects actual unknown SQL-default keys on MySQL before any INSERT reaches the server.</summary>
    [RequiresDatabaseTargetsTheory(IntegrationDatabaseTarget.MySql84, IntegrationDatabaseTarget.MySql97)]
    [MemberData(nameof(MySqlGuidDefaultKeyCases))]
    public async Task Guid_sql_default_unknown_keys_are_rejected_before_mysql_insert(
        string targetName,
        MySqlGuidFormat format,
        bool asyncSave
    )
    {
        // Arrange
        var target = Enum.Parse<IntegrationDatabaseTarget>(targetName);
        var capture = new ReadbackCapture();
        await using var context = CreateGuidDefaultsContext<GuidDefaultsKeyMode>(target, format, capture);
        await RecreateGuidDefaultsTablesAsync(context);
        var row = new GuidDefaultPrincipal { Marker = 17 };
        context.Add(row);
        var wasTemporary = context.Entry(row).Property(item => item.Id).IsTemporary;
        capture.Enabled = true;

        // Act
        Exception? exception;

        if (asyncSave)
        {
            exception = await Record.ExceptionAsync(() => context.SaveChangesAsync(CancellationToken.None));
        }
        else
        {
            // WHY: This case verifies the unknown-key guard before synchronous command execution.
            // ReSharper disable once MethodHasAsyncOverload
            exception = Record.Exception(() => context.SaveChanges());
        }

        capture.Enabled = false;
        var count = await context.Set<GuidDefaultPrincipal>().AsNoTracking().CountAsync(CancellationToken.None);

        // Assert
        var failure = Assert.IsType<InvalidOperationException>(exception);
        Assert.True(wasTemporary);
        Assert.Contains("cannot retrieve", failure.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(GuidDefaultPrincipalTable, failure.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(GuidDefaultPrincipal.Id), failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(capture.Commands, sql => sql.Contains("INSERT INTO", StringComparison.Ordinal));
        Assert.Equal(0, count);
    }

    /// <summary>Accepts explicitly supplied keys on the same SQL-default model on every supported engine.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(GuidDefaultMappingCases))]
    public async Task Guid_sql_default_explicit_keys_preserve_application_values(
        string targetName,
        MySqlGuidFormat format
    )
    {
        // Arrange
        var target = Enum.Parse<IntegrationDatabaseTarget>(targetName);
        var capture = new ReadbackCapture();
        await using var context = CreateGuidDefaultsContext<GuidDefaultsKeyMode>(target, format, capture);
        await RecreateGuidDefaultsTablesAsync(context);
        var rows = Enumerable.Range(1, 8)
            .Select(marker => new GuidDefaultPrincipal { Id = CreateGuid(marker), Marker = marker })
            .Reverse()
            .ToArray();

        context.AddRange(rows);
        var temporaryKeys = rows.Select(row => context.Entry(row).Property(item => item.Id).IsTemporary).ToArray();
        capture.Enabled = true;

        // Act
        var written = await context.SaveChangesAsync(CancellationToken.None);

        capture.Enabled = false;
        var persisted = await context.Set<GuidDefaultPrincipal>().AsNoTracking()
            .ToDictionaryAsync(row => row.Marker, CancellationToken.None);

        // Assert
        Assert.Equal(8, written);
        Assert.All(temporaryKeys, Assert.False);
        Assert.Equal(8, persisted.Count);
        Assert.All(rows, row =>
        {
            Assert.Equal(CreateGuid(row.Marker), row.Id);
            Assert.Equal(row.Id, persisted[row.Marker].Id);
        });
        Assert.NotEmpty(GuidDefaultInsertHeaders(capture.Commands, GuidDefaultPrincipalTable));
        Assert.All(GuidDefaultInsertHeaders(capture.Commands, GuidDefaultPrincipalTable), header =>
            Assert.Contains("`Id`", header, StringComparison.Ordinal));
    }

    /// <summary>Reads nullable, required, and application-converted SQL-default GUIDs by known integer keys.</summary>
    [RequiresSupportedDatabaseTargetTheory]
    [MemberData(nameof(GuidDefaultMappingCases))]
    public async Task Guid_sql_default_nonkey_values_are_read_back_and_correlated(
        string targetName,
        MySqlGuidFormat format
    )
    {
        // Arrange
        var target = Enum.Parse<IntegrationDatabaseTarget>(targetName);
        var capture = new ReadbackCapture();
        await using var context = CreateGuidDefaultsContext<GuidDefaultsValueMode>(target, format, capture);
        await RecreateGuidDefaultsTablesAsync(context);
        var rows = Enumerable.Range(1, 24)
            .Select(id => new GuidDefaultValueRow { Id = id })
            .Reverse()
            .ToArray();
        var supplied = new GuidDefaultValueRow
        {
            Id = 100,
            RequiredValue = CreateGuid(101),
            NullableValue = CreateGuid(102),
            ConvertedValue = CreateGuid(103),
            ConvertedNullableValue = CreateGuid(104),
        };

        context.AddRange(rows);
        context.Add(supplied);
        capture.Enabled = true;

        // Act
        var written = await context.SaveChangesAsync(CancellationToken.None);

        capture.Enabled = false;
        var persisted = await context.Set<GuidDefaultValueRow>().AsNoTracking()
            .ToDictionaryAsync(row => row.Id, CancellationToken.None);

        // Assert
        Assert.Equal(25, written);
        Assert.Equal(25, persisted.Count);
        Assert.Equal(24, rows.Select(row => row.RequiredValue).Distinct().Count());
        Assert.Equal(24, rows.Select(row => row.NullableValue).Distinct().Count());
        Assert.All(rows.Append(supplied), row =>
        {
            Assert.NotEqual(Guid.Empty, row.RequiredValue);
            Assert.NotNull(row.NullableValue);
            Assert.NotEqual<Guid?>(Guid.Empty, row.NullableValue);
            Assert.NotEqual(Guid.Empty, row.ConvertedValue);
            Assert.NotNull(row.ConvertedNullableValue);
            Assert.NotEqual<Guid?>(Guid.Empty, row.ConvertedNullableValue);
            Assert.Equal(persisted[row.Id].RequiredValue, row.RequiredValue);
            Assert.Equal(persisted[row.Id].NullableValue, row.NullableValue);
            Assert.Equal(persisted[row.Id].ConvertedValue, row.ConvertedValue);
            Assert.Equal(persisted[row.Id].ConvertedNullableValue, row.ConvertedNullableValue);
        });
        Assert.Equal(CreateGuid(101), supplied.RequiredValue);
        Assert.Equal(CreateGuid(102), supplied.NullableValue);
        Assert.Equal(CreateGuid(103), supplied.ConvertedValue);
        Assert.Equal(CreateGuid(104), supplied.ConvertedNullableValue);
        Assert.Contains(GuidDefaultInsertHeaders(capture.Commands, GuidDefaultValueTable), header =>
            !header.Contains("`RequiredValue`", StringComparison.Ordinal)
            && !header.Contains("`NullableValue`", StringComparison.Ordinal)
            && !header.Contains("`ConvertedValue`", StringComparison.Ordinal)
            && !header.Contains("`ConvertedNullableValue`", StringComparison.Ordinal));

        if (IsGuidDefaultMySqlTarget(target))
        {
            Assert.Contains(capture.Commands, sql => sql.Contains("UNION ALL", StringComparison.Ordinal));
        }
        else
        {
            Assert.Contains(capture.Commands, sql => sql.Contains("RETURNING", StringComparison.Ordinal));
        }
    }

    private static IEnumerable<object[]> CreateGuidDefaultMappingCases()
    {
        foreach (var target in IntegrationTestEnvironment.GetSupportedTargets()
                     .Where(IntegrationTestEnvironment.IsTargetSelected))
        {
            yield return [target.ToString(), MySqlGuidFormat.Char36];
            yield return [target.ToString(), MySqlGuidFormat.Binary16];

            if (!IsGuidDefaultMySqlTarget(target))
            {
                yield return [target.ToString(), MySqlGuidFormat.Uuid];
            }
        }
    }

    private static bool IsGuidDefaultMySqlTarget(
        IntegrationDatabaseTarget target
    ) => target is IntegrationDatabaseTarget.MySql84 or IntegrationDatabaseTarget.MySql97;

    private static IEnumerable<string> GuidDefaultInsertHeaders(
        IEnumerable<string> commands,
        string table
    ) => commands.SelectMany(command => command.Split('\n'))
        .Where(line => line.StartsWith($"INSERT INTO `{table}` (", StringComparison.Ordinal));
}
