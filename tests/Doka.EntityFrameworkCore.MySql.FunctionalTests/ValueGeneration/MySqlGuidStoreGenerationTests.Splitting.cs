namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

public sealed partial class MySqlGuidStoreGenerationTests
{
    /// <summary>Provides each supported configured engine/storage pair and nullable GUID shape.</summary>
    public static IEnumerable<object[]> FragmentGuidCases()
    {
        foreach (var mariaDb in new[] { false, true })
        {
            foreach (var format in new[] { MySqlGuidFormat.Char36, MySqlGuidFormat.Binary16, MySqlGuidFormat.Uuid })
            {
                if (!mariaDb
                    && format == MySqlGuidFormat.Uuid)
                {
                    continue;
                }

                yield return [mariaDb, format, false];
                yield return [mariaDb, format, true];
            }
        }
    }

    /// <summary>A secondary-only GUID must not be inspected against the entity's unmapped primary table.</summary>
    /// <param name="mariaDb">Whether the configured engine is MariaDB.</param>
    /// <param name="format">The physical GUID representation.</param>
    /// <param name="nullable">Whether the secondary property is nullable.</param>
    [Theory]
    [MemberData(nameof(FragmentGuidCases))]
    public void Secondary_guid_without_default_remains_non_generated(
        bool mariaDb,
        MySqlGuidFormat format,
        bool nullable
    )
    {
        // Arrange
        using var context = CreateFragmentGuidContext(mariaDb, format, nullable, "none");
        var propertyName = nullable ? nameof(FragmentGuidRow.OptionalToken) : nameof(FragmentGuidRow.Token);
        var primary = StoreObjectIdentifier.Table("FragmentGuidPayload", schema: null);
        var fragment = StoreObjectIdentifier.Table("FragmentGuidValues", schema: null);

        // Act
        var property = context.Model.FindEntityType(typeof(FragmentGuidRow))!.FindProperty(propertyName)!;

        // Assert
        Assert.Null(property.GetColumnName(primary));
        Assert.Equal(propertyName, property.GetColumnName(fragment));
        Assert.Null(property.GetDefaultValueSql(fragment));
        Assert.Null(property.GetComputedColumnSql(fragment));
        Assert.Equal(ValueGenerated.Never, property.ValueGenerated);
        Assert.Equal(MySqlValueGenerationStrategy.None, property.GetMySqlValueGenerationStrategy());
    }

    /// <summary>Reports every secondary expression shape independently of non-generated controls.</summary>
    public static IEnumerable<object[]> FragmentExpressionCases()
    {
        foreach (var row in FragmentGuidCases())
        {
            yield return [.. row, "sql"];
            yield return [.. row, "computed"];
        }
    }

    /// <summary>Secondary SQL defaults and computations preserve EF's store-generation facets.</summary>
    /// <param name="mariaDb">Whether the configured engine is MariaDB.</param>
    /// <param name="format">The physical GUID representation.</param>
    /// <param name="nullable">Whether the generated property is nullable.</param>
    /// <param name="expression">The store-expression kind.</param>
    [Theory]
    [MemberData(nameof(FragmentExpressionCases))]
    public void Secondary_guid_expression_preserves_store_generation(
        bool mariaDb,
        MySqlGuidFormat format,
        bool nullable,
        string expression
    )
    {
        // Arrange
        using var context = CreateFragmentGuidContext(mariaDb, format, nullable, expression);
        var propertyName = nullable ? nameof(FragmentGuidRow.OptionalToken) : nameof(FragmentGuidRow.Token);
        var fragment = StoreObjectIdentifier.Table("FragmentGuidValues", schema: null);
        var computed = expression == "computed";

        // Act
        var property = context.Model.FindEntityType(typeof(FragmentGuidRow))!.FindProperty(propertyName)!;

        // Assert
        Assert.Null(property.GetColumnName(StoreObjectIdentifier.Table("FragmentGuidPayload", schema: null)));
        Assert.NotNull(property.GetColumnName(fragment));
        Assert.Null(property.GetMySqlValueGenerationStrategy());
        Assert.Equal(computed ? ValueGenerated.OnAddOrUpdate : ValueGenerated.OnAdd, property.ValueGenerated);
        Assert.Equal(computed ? null : DefaultSql(format), property.GetDefaultValueSql(fragment));
        Assert.Equal(computed ? "`Source`" : null, property.GetComputedColumnSql(fragment));
        Assert.Equal(computed ? PropertySaveBehavior.Ignore : PropertySaveBehavior.Save, property.GetBeforeSaveBehavior());
    }

    /// <summary>Reports explicit strategy controls for each valid secondary mapping.</summary>
    public static IEnumerable<object[]> FragmentStrategyCases()
    {
        foreach (var row in FragmentGuidCases())
        {
            yield return [.. row, MySqlValueGenerationStrategy.ClientGuid];
            yield return [.. row, MySqlValueGenerationStrategy.None];
        }
    }

    /// <summary>Explicit provider strategies retain precedence over a secondary SQL default.</summary>
    /// <param name="mariaDb">Whether the configured engine is MariaDB.</param>
    /// <param name="format">The physical GUID representation.</param>
    /// <param name="nullable">Whether the secondary property is nullable.</param>
    /// <param name="strategy">The explicit application-owned generation strategy.</param>
    [Theory]
    [MemberData(nameof(FragmentStrategyCases))]
    public void Secondary_guid_explicit_strategy_remains_authoritative(
        bool mariaDb,
        MySqlGuidFormat format,
        bool nullable,
        MySqlValueGenerationStrategy strategy
    )
    {
        // Arrange
        using var context = CreateFragmentGuidContext(mariaDb, format, nullable, "sql", strategy);
        var propertyName = nullable ? nameof(FragmentGuidRow.OptionalToken) : nameof(FragmentGuidRow.Token);

        // Act
        var property = context.Model.FindEntityType(typeof(FragmentGuidRow))!.FindProperty(propertyName)!;

        // Assert
        Assert.Equal(strategy, property.GetMySqlValueGenerationStrategy());
        Assert.Equal(strategy == MySqlValueGenerationStrategy.None ? ValueGenerated.Never : ValueGenerated.OnAdd,
            property.ValueGenerated);
        Assert.Equal(DefaultSql(format), property.GetDefaultValueSql());
    }

    /// <summary>Explicit client strategy selection remains permanent even beside a secondary store default.</summary>
    /// <param name="mariaDb">Whether the configured engine is MariaDB.</param>
    /// <param name="format">The physical GUID representation.</param>
    /// <param name="nullable">Whether the generated property is nullable.</param>
    [Theory]
    [MemberData(nameof(FragmentGuidCases))]
    public void Secondary_client_strategy_selects_permanent_guid_generator(
        bool mariaDb,
        MySqlGuidFormat format,
        bool nullable
    )
    {
        // Arrange
        using var context = CreateFragmentGuidContext(
            mariaDb,
            format,
            nullable,
            "sql",
            MySqlValueGenerationStrategy.ClientGuid);

        var propertyName = nullable ? nameof(FragmentGuidRow.OptionalToken) : nameof(FragmentGuidRow.Token);
        var property = context.Model.FindEntityType(typeof(FragmentGuidRow))!.FindProperty(propertyName)!;
        var selector = context.GetService<Microsoft.EntityFrameworkCore.ValueGeneration.IValueGeneratorSelector>();

        // Act
        var selected = selector.TrySelect(property, property.DeclaringType, out var generator);

        // Assert
        Assert.True(selected);
        Assert.IsType<MySqlSequentialGuidValueGenerator>(generator);
        Assert.False(generator.GeneratesTemporaryValues);
    }

    /// <summary>A shared secondary column can own a default absent from the dependent's primary table.</summary>
    /// <param name="mariaDb">Whether the configured engine is MariaDB.</param>
    /// <param name="hasDefault">Whether the physical shared root has a SQL default.</param>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Shared_secondary_guid_uses_its_actual_default_owner(
        bool mariaDb,
        bool hasDefault
    )
    {
        // Arrange
        using var context = CreateSharedFragmentGuidContext(mariaDb, hasDefault);
        var primary = StoreObjectIdentifier.Table("FragmentDependentPayload", schema: null);
        var secondary = StoreObjectIdentifier.Table("FragmentSharedGuid", schema: null);

        // Act
        var property = context.Model
            .FindEntityType(typeof(SharedFragmentGuidDependent))!
            .FindProperty(nameof(SharedFragmentGuidDependent.Token))!;

        // Assert
        Assert.Null(property.GetColumnName(primary));
        Assert.NotNull(property.GetColumnName(secondary));
        Assert.Null(property.GetDefaultValueSql());
        Assert.Equal(hasDefault ? "UUID()" : null, property.GetDefaultValueSql(secondary));
        Assert.Equal(hasDefault ? null : MySqlValueGenerationStrategy.ClientGuid,
            property.GetMySqlValueGenerationStrategy());
        Assert.Equal(ValueGenerated.OnAdd, property.ValueGenerated);
    }

    /// <summary>The runtime selector must find inherited defaults in secondary fragments, not just primary stores.</summary>
    /// <param name="mariaDb">Whether the configured engine is MariaDB.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Shared_secondary_sql_default_selects_temporary_guid_generator(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateSharedFragmentGuidContext(mariaDb, hasDefault: true);
        var property = context.Model
            .FindEntityType(typeof(SharedFragmentGuidDependent))!
            .FindProperty(nameof(SharedFragmentGuidDependent.Token))!;

        var selector = context.GetService<Microsoft.EntityFrameworkCore.ValueGeneration.IValueGeneratorSelector>();

        // Act
        var selected = selector.TrySelect(property, property.DeclaringType, out var generator);

        // Assert
        Assert.True(selected);
        Assert.NotNull(generator);
        Assert.True(generator.GeneratesTemporaryValues);
    }

    /// <summary>Shared computations are resolved on the physical secondary column without local annotations.</summary>
    /// <param name="mariaDb">Whether the configured engine is MariaDB.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Shared_secondary_computed_guid_preserves_store_generation(
        bool mariaDb
    )
    {
        // Arrange
        using var context = CreateSharedFragmentGuidContext(mariaDb, hasDefault: true);

        // Act
        var property = context.Model
            .FindEntityType(typeof(SharedFragmentGuidDependent))!
            .FindProperty(nameof(SharedFragmentGuidDependent.ComputedToken))!;

        // Assert
        Assert.Null(property.GetComputedColumnSql());
        Assert.Equal("'00112233-4455-4677-8899-aabbccddeeff'",
            property.GetComputedColumnSql(StoreObjectIdentifier.Table("FragmentSharedGuid", schema: null)));
        Assert.Null(property.GetMySqlValueGenerationStrategy());
        Assert.Equal(ValueGenerated.OnAddOrUpdate, property.ValueGenerated);
        Assert.Equal(PropertySaveBehavior.Ignore, property.GetBeforeSaveBehavior());
        Assert.Equal(PropertySaveBehavior.Ignore, property.GetAfterSaveBehavior());
    }

    /// <summary>Literal defaults are resolved separately and cannot inherit an unrelated GUID expression.</summary>
    /// <param name="mariaDb">Whether the configured engine is MariaDB.</param>
    /// <param name="hasDefault">Whether the shared root declares literal and SQL defaults.</param>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Shared_secondary_literal_default_does_not_inherit_unrelated_sql_generation(
        bool mariaDb,
        bool hasDefault
    )
    {
        // Arrange
        using var context = CreateSharedFragmentGuidContext(mariaDb, hasDefault);
        var property = context.Model
            .FindEntityType(typeof(SharedFragmentGuidDependent))!
            .FindProperty(nameof(SharedFragmentGuidDependent.Literal))!;

        // Act
        var hasLiteral = MySqlStoreGenerationMetadata.HasLiteralDefault(property);
        var hasExpression = MySqlStoreGenerationMetadata.HasStoreGeneratedExpression(property);
        var defaultSql = MySqlStoreGenerationMetadata.GetDefaultValueSql(property);

        // Assert
        Assert.False(property.TryGetDefaultValue(out _));
        Assert.Equal(hasDefault, hasLiteral);
        Assert.False(hasExpression);
        Assert.Null(defaultSql);
    }
}
