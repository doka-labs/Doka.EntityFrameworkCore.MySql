namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

/// <summary>Qualifies complete INSERT SELECT translation while refusing database I/O at a deterministic boundary.</summary>
public sealed partial class MySqlInsertSelectTranslationTests
{
    /// <summary>Source filters and current parameters feed an explicitly quoted physical column list.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Basic_insert_uses_the_actual_source_and_target_mapping(
        bool mariaDb
    )
    {
        // Arrange
        var observation = new InsertObservation();
        await using var context = CreateContext(mariaDb, observation);
        var minimum = 12;
        var cancellationToken = CancellationToken.None;

        // Act
        var actual = await Record.ExceptionAsync(() => context
            .Sources
            .Where(row => row.Id > minimum)
            .ExecuteInsertFromQueryAsync(context.Targets,
                setters => setters
                    .SetProperty(row => row.Name, row => row.Name)
                    .SetProperty(row => row.Value, row => row.Value),
                cancellationToken));

        // Assert
        Assert.Same(observation.OpenFailure, actual);
        Assert.Equal(1, observation.OpenAttempts);
        var command = Assert.Single(observation.Commands);
        Assert.StartsWith("INSERT INTO `insert``Target` (`Data``Name`, `payload value`)",
            command.Sql,
            StringComparison.Ordinal);

        Assert.Contains("FROM `insert Source`", command.Sql, StringComparison.Ordinal);
        Assert.Contains("WHERE", command.Sql, StringComparison.Ordinal);
        Assert.Contains(12, command.Parameters.Values);
        Assert.Contains(7, command.Parameters.Values);
        Assert.DoesNotContain("target-filter-must-not-authorize-insert", command.Sql, StringComparison.Ordinal);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    /// <summary>Literal assignments remain database projections rather than discarded client constants.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Constant_assignments_are_real_sql_values(
        bool mariaDb
    )
    {
        // Arrange
        var observation = new InsertObservation();
        await using var context = CreateContext(mariaDb, observation);
        var cancellationToken = CancellationToken.None;

        // Act
        var actual = await Record.ExceptionAsync(() => context.Sources.ExecuteInsertFromQueryAsync(
            context.Targets,
            setters => setters
                .SetProperty(row => row.Name, _ => "constant-value")
                .SetProperty(row => row.Value, _ => 42),
            cancellationToken));

        // Assert
        Assert.Same(observation.OpenFailure, actual);
        var sql = Assert.Single(observation.Commands)
            .Sql;

        Assert.Contains("'constant-value'", sql, StringComparison.Ordinal);
        Assert.Contains("42", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("ComputedLength", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("DefaultText", sql, StringComparison.Ordinal);
    }

    /// <summary>Joined/navigated values preserve independently filtered source roots.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Navigated_source_filter_survives_the_assignment_projection(
        bool mariaDb
    )
    {
        // Arrange
        var observation = new InsertObservation();
        await using var context = CreateContext(mariaDb, observation);
        var cancellationToken = CancellationToken.None;

        // Act
        var actual = await Record.ExceptionAsync(() => context.Sources.ExecuteInsertFromQueryAsync(
            context.Targets,
            setters => setters
                .SetProperty(row => row.Name, row => row.Lookup.Name)
                .SetProperty(row => row.Value, row => row.Value),
            cancellationToken));

        // Assert
        Assert.Same(observation.OpenFailure, actual);
        var sql = Assert.Single(observation.Commands)
            .Sql;

        Assert.Contains("JOIN", sql, StringComparison.Ordinal);
        Assert.Contains("insert Lookups", sql, StringComparison.Ordinal);
        Assert.Contains("Visible", sql, StringComparison.Ordinal);
        Assert.Contains("TenantId", sql, StringComparison.Ordinal);
    }

    /// <summary>Explicit source filter opt-out does not become a target insertion policy.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Explicit_source_filter_opt_out_is_retained(
        bool mariaDb
    )
    {
        // Arrange
        var observation = new InsertObservation();
        await using var context = CreateContext(mariaDb, observation);
        var cancellationToken = CancellationToken.None;

        // Act
        var actual = await Record.ExceptionAsync(() => context
            .Sources
            .IgnoreQueryFilters()
            .ExecuteInsertFromQueryAsync(
                context.Targets,
                setters => setters
                    .SetProperty(row => row.Name, row => row.Lookup.Name)
                    .SetProperty(row => row.Value, row => row.Value),
                cancellationToken));

        // Assert
        Assert.Same(observation.OpenFailure, actual);
        var command = Assert.Single(observation.Commands);
        Assert.DoesNotContain("Visible", command.Sql, StringComparison.Ordinal);
        Assert.DoesNotContain("TenantId", command.Sql, StringComparison.Ordinal);
        Assert.DoesNotContain("target-filter-must-not-authorize-insert", command.Sql, StringComparison.Ordinal);
        Assert.Empty(command.Parameters);
    }

    /// <summary>Source page selection remains native and precedes insertion without an extra count.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Paging_remains_in_the_select_statement(
        bool mariaDb
    )
    {
        // Arrange
        var observation = new InsertObservation();
        await using var context = CreateContext(mariaDb, observation);
        var skip = 2;
        var take = 5;
        var cancellationToken = CancellationToken.None;

        // Act
        var actual = await Record.ExceptionAsync(() => context
            .Sources
            .OrderBy(row => row.Id)
            .Skip(skip)
            .Take(take)
            .ExecuteInsertFromQueryAsync(context.Targets,
                setters => setters
                    .SetProperty(row => row.Name, row => row.Name)
                    .SetProperty(row => row.Value, row => row.Value),
                cancellationToken));

        // Assert
        Assert.Same(observation.OpenFailure, actual);
        var command = Assert.Single(observation.Commands);
        Assert.Contains("ORDER BY", command.Sql, StringComparison.Ordinal);
        Assert.Contains("LIMIT", command.Sql, StringComparison.Ordinal);
        Assert.Contains("OFFSET", command.Sql, StringComparison.Ordinal);
        Assert.Contains(2, command.Parameters.Values);
        Assert.Contains(5, command.Parameters.Values);
    }

    /// <summary>Ordinary CTE headers occur after the INSERT target and before SELECT.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Ordinary_cte_has_the_native_insert_select_header_position(
        bool mariaDb
    )
    {
        // Arrange
        var observation = new InsertObservation();
        await using var context = CreateContext(mariaDb, observation);
        var cancellationToken = CancellationToken.None;

        // Act
        var actual = await Record.ExceptionAsync(() => context
            .Sources
            .WithCte(rows => rows.Where(row => row.Value > 0))
            .ExecuteInsertFromQueryAsync(context.Targets,
                setters => setters
                    .SetProperty(row => row.Name, row => row.Name)
                    .SetProperty(row => row.Value, row => row.Value),
                cancellationToken));

        // Assert
        Assert.Same(observation.OpenFailure, actual);
        var sql = Assert.Single(observation.Commands)
            .Sql;

        Assert.True(
            sql.IndexOf("INSERT INTO", StringComparison.Ordinal) < sql.IndexOf("WITH", StringComparison.Ordinal),
            sql);

        Assert.DoesNotContain("WITH RECURSIVE", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("WHERE 1 = 0", sql, StringComparison.Ordinal);
    }

    /// <summary>Canonical binary/text GUID conversions are explicit server expressions.</summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Guid_store_representation_changes_use_known_sql_conversions(
        bool mariaDb,
        bool toBinary
    )
    {
        // Arrange
        var observation = new InsertObservation();
        await using var context = CreateContext(mariaDb, observation);
        var cancellationToken = CancellationToken.None;

        // Act
        var actual = await Record.ExceptionAsync(() => context.Sources.ExecuteInsertFromQueryAsync(context.Targets,
            setters =>
            {
                setters
                    .SetProperty(row => row.Name, row => row.Name)
                    .SetProperty(row => row.Value, row => row.Value);

                if (toBinary)
                {
                    setters.SetProperty(row => row.TokenBinary, row => row.TokenText);
                }
                else
                {
                    setters.SetProperty(row => row.TokenText, row => row.TokenBinary);
                }
            },
            cancellationToken));

        // Assert
        Assert.Same(observation.OpenFailure, actual);
        var sql = Assert.Single(observation.Commands)
            .Sql;

        Assert.Contains(toBinary
                ? "UNHEX("
                : "HEX(",
            sql,
            StringComparison.Ordinal);

        Assert.Contains(toBinary
                ? "REPLACE("
                : "LOWER(",
            sql,
            StringComparison.Ordinal);
    }

    /// <summary>Field and shadow selectors retain their mapped physical column names.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Field_and_shadow_targets_use_ef_metadata(
        bool mariaDb
    )
    {
        // Arrange
        var observation = new InsertObservation();
        await using var context = CreateContext(mariaDb, observation);
        var cancellationToken = CancellationToken.None;

        // Act
        var actual = await Record.ExceptionAsync(() => context.Sources.ExecuteInsertFromQueryAsync(context.Targets,
            setters => setters
                .SetProperty(row => row.Name, row => row.Name)
                .SetProperty(row => row.Value, row => row.Value)
                .SetProperty(row => EF.Property<string>(row, "_secret"), row => row.Name)
                .SetProperty(row => EF.Property<int>(row, "ShadowCode"), row => row.Value),
            cancellationToken));

        // Assert
        Assert.Same(observation.OpenFailure, actual);
        var sql = Assert.Single(observation.Commands)
            .Sql;

        Assert.Contains("`secret payload`", sql, StringComparison.Ordinal);
        Assert.Contains("`ShadowCode`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("`_secret`", sql, StringComparison.Ordinal);
    }

    /// <summary>A concrete TPH target receives its model discriminator, independent of source filters.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Concrete_tph_target_inserts_the_model_discriminator(
        bool mariaDb
    )
    {
        // Arrange
        var observation = new InsertObservation();
        await using var context = CreateContext(mariaDb, observation);
        var cancellationToken = CancellationToken.None;

        // Act
        var actual = await Record.ExceptionAsync(() => context.Sources.ExecuteInsertFromQueryAsync(
            context.DerivedTargets,
            setters => setters
                .SetProperty(row => row.Name, row => row.Name)
                .SetProperty(row => row.Detail, row => row.Value),
            cancellationToken));

        // Assert
        Assert.Same(observation.OpenFailure, actual);
        var sql = Assert.Single(observation.Commands)
            .Sql;

        Assert.Contains("`Kind`", sql, StringComparison.Ordinal);
        Assert.Contains("'derived'", sql, StringComparison.Ordinal);
        Assert.StartsWith("INSERT INTO `InsertHierarchy`", sql, StringComparison.Ordinal);
    }
}
