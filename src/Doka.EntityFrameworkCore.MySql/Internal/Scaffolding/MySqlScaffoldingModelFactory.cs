namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Reconstructs an EF model from the provider database model while preserving MySQL-specific metadata.
/// </summary>
internal sealed partial class MySqlScaffoldingModelFactory : IScaffoldingModelFactory
{
    private readonly MySqlReverseEngineeringOptions _reverseEngineeringOptions;
    private readonly MySqlScaffoldingContext _scaffoldingContext;
    private readonly IMySqlSpatialTypeProvider? _spatialTypeProvider;
    private readonly IRelationalTypeMappingSource _typeMappingSource;
    private readonly ILogger _logger;

    public MySqlScaffoldingModelFactory(
        MySqlReverseEngineeringOptions reverseEngineeringOptions,
        MySqlScaffoldingContext scaffoldingContext,
        IEnumerable<IMySqlSpatialTypeProvider> spatialTypeProviders,
        IRelationalTypeMappingSource typeMappingSource,
        ILoggerFactory loggerFactory
    )
    {
        ArgumentNullException.ThrowIfNull(spatialTypeProviders);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        _reverseEngineeringOptions = reverseEngineeringOptions
            ?? throw new ArgumentNullException(nameof(reverseEngineeringOptions));
        _scaffoldingContext = scaffoldingContext ?? throw new ArgumentNullException(nameof(scaffoldingContext));
        _spatialTypeProvider = spatialTypeProviders.SingleOrDefault();
        _typeMappingSource = typeMappingSource
            ?? throw new ArgumentNullException(nameof(typeMappingSource));
        _logger = loggerFactory.CreateLogger(MySqlLoggerCategory.Scaffolding);
    }

    public IModel Create(
        DatabaseModel databaseModel,
        ModelReverseEngineerOptions options
    )
    {
        ArgumentNullException.ThrowIfNull(databaseModel);
        ArgumentNullException.ThrowIfNull(options);

        var modelBuilder = new ModelBuilder(new ConventionSet());
        var modelCharSet = databaseModel.FindAnnotation(MySqlAnnotationNames.CharSet)?.Value as string;
        var entityNames = new HashSet<string>(StringComparer.Ordinal);
        var propertyNamesByEntity = new Dictionary<DatabaseTable, HashSet<string>>();
        var entityBuilders = new Dictionary<DatabaseTable, EntityTypeBuilder>();
        var entityPropertyBuilders = new Dictionary<(DatabaseTable Table, DatabaseColumn Column), PropertyBuilder>();
        var skippedColumns = new HashSet<(DatabaseTable Table, DatabaseColumn Column)>();

        _scaffoldingContext.SetUsesNetTopologySuiteScaffolding(false);

        if (!string.IsNullOrWhiteSpace(modelCharSet))
        {
            modelBuilder.Model.SetMySqlCharSet(modelCharSet);
        }

        // Pre-sort tables once to avoid redundant OrderBy allocations in each pass.
        var sortedTables = databaseModel
            .Tables.OrderBy(table => table.Name, StringComparer.Ordinal)
            .ToList();

        foreach (var table in sortedTables)
        {
            var entityName = CreateUniqueClrIdentifier(table.Name, entityNames, "Entity");
            var entityBuilder = modelBuilder.Entity(entityName);
            var tableCollation = table.FindAnnotation(RelationalAnnotationNames.Collation)
                ?.Value as string;

            var tableCharSet = table.FindAnnotation(MySqlAnnotationNames.CharSet)
                ?.Value as string;

            var storageEngine = table.FindAnnotation(MySqlAnnotationNames.StorageEngine)
                ?.Value as string;

            if (table is DatabaseView)
            {
                entityBuilder.HasNoKey();
                entityBuilder.ToView(table.Name);
            }
            else
            {
                entityBuilder.ToTable(
                    table.Name,
                    tableBuilder =>
                    {
                        if (!string.IsNullOrWhiteSpace(table.Comment))
                        {
                            tableBuilder.HasComment(table.Comment);
                        }

                        foreach (var checkConstraint in GetCheckConstraints(table))
                        {
                            tableBuilder.HasCheckConstraint(checkConstraint.Name, checkConstraint.Sql);
                        }
                    });
            }

            if (!string.IsNullOrWhiteSpace(tableCollation))
            {
                entityBuilder.Metadata.SetAnnotation(RelationalAnnotationNames.Collation, tableCollation);
            }

            if (!string.IsNullOrWhiteSpace(tableCharSet))
            {
                entityBuilder.Metadata.SetMySqlCharSet(tableCharSet);
            }

            if (!string.IsNullOrWhiteSpace(storageEngine))
            {
                entityBuilder.Metadata.SetMySqlStorageEngine(storageEngine);
            }

            entityBuilders[table] = entityBuilder;
            propertyNamesByEntity[table] = [with(StringComparer.Ordinal)];
        }

        foreach (var table in sortedTables)
        {
            var entityBuilder = entityBuilders[table];
            var usedPropertyNames = propertyNamesByEntity[table];

            foreach (var column in table.Columns.OrderBy(column => column.Name, StringComparer.Ordinal))
            {
                if (TryHandleSpatialColumn(
                        table,
                        column,
                        entityBuilder,
                        usedPropertyNames,
                        entityPropertyBuilders,
                        skippedColumns))
                {
                    continue;
                }

                var mapping = ResolveColumnMapping(column);
                var propertyName = CreateUniqueClrIdentifier(column.Name, usedPropertyNames, "Value");
                var propertyBuilder = entityBuilder.Property(
                    GetPropertyClrType(mapping.ClrType, column.IsNullable),
                    propertyName);

                ApplyColumnConfiguration(propertyBuilder, column, mapping);

                entityPropertyBuilders[(table, column)] = propertyBuilder;
            }
        }

        foreach (var table in sortedTables)
        {
            ApplyTemporalConfiguration(
                table,
                entityBuilders[table],
                entityPropertyBuilders);

            ApplyApplicationTimeConfiguration(
                table,
                entityBuilders[table],
                entityPropertyBuilders);
        }

        foreach (var table in sortedTables)
        {
            var entityBuilder = entityBuilders[table];

            if (table.PrimaryKey is not null
                && table.PrimaryKey.Columns.Count > 0)
            {
                if (!ContainsSkippedColumn(table.PrimaryKey.Columns, skippedColumns, table))
                {
                    var primaryKeyBuilder = entityBuilder.HasKey(
                        GetPropertyNames(table.PrimaryKey.Columns, entityPropertyBuilders, table));

                    if (!string.IsNullOrWhiteSpace(table.PrimaryKey.Name))
                    {
                        primaryKeyBuilder.HasName(table.PrimaryKey.Name);
                    }

                    if (table.PrimaryKey.FindAnnotation(
                            MySqlAnnotationNames.ApplicationTimeKeyWithoutOverlaps)
                            ?.Value is true)
                    {
                        primaryKeyBuilder.UseWithoutOverlaps();
                    }
                }
            }

            foreach (var uniqueConstraint in table.UniqueConstraints)
            {
                if (uniqueConstraint.Columns.Count > 0
                    && !ContainsSkippedColumn(uniqueConstraint.Columns, skippedColumns, table))
                {
                    var alternateKeyBuilder = entityBuilder.HasAlternateKey(
                        GetPropertyNames(uniqueConstraint.Columns, entityPropertyBuilders, table));

                    if (!string.IsNullOrWhiteSpace(uniqueConstraint.Name))
                    {
                        alternateKeyBuilder.HasName(uniqueConstraint.Name);
                    }

                    if (uniqueConstraint.FindAnnotation(
                            MySqlAnnotationNames.ApplicationTimeKeyWithoutOverlaps)
                            ?.Value is true)
                    {
                        alternateKeyBuilder.UseWithoutOverlaps();
                    }
                }
            }

            var uniqueConstraintNames = table
                .UniqueConstraints.Select(constraint => constraint.Name)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .ToHashSet(StringComparer.Ordinal);

            foreach (var index in table.Indexes)
            {
                var scaffoldedParts = GetScaffoldedIndexParts(index);

                // EF Core requires every IIndex part to reference an IProperty. Retaining only
                // the column-backed subset would silently change a functional index, so its
                // exact parts remain on the DatabaseIndex for database-model consumers.
                if (scaffoldedParts.Any(part => part.Expression is not null))
                {
                    continue;
                }

                if (index.Columns.Count == 0)
                {
                    continue;
                }

                if (index.Name is not null
                    && uniqueConstraintNames.Contains(index.Name))
                {
                    continue;
                }

                if (ContainsSkippedColumn(index.Columns, skippedColumns, table))
                {
                    continue;
                }

                var indexBuilder =
                    entityBuilder.HasIndex(GetPropertyNames(index.Columns, entityPropertyBuilders, table));

                if (!string.IsNullOrWhiteSpace(index.Name))
                {
                    indexBuilder.HasDatabaseName(index.Name);
                }

                if (index.IsUnique)
                {
                    indexBuilder.IsUnique();
                }

                if (index.FindAnnotation(MySqlAnnotationNames.ApplicationTimeIndexWithoutOverlaps)
                        ?.Value is true)
                {
                    indexBuilder.UseWithoutOverlaps();
                }

                if (index.IsDescending is { Count: > 0 }
                    && index.IsDescending.Any(isDescending => isDescending))
                {
                    indexBuilder.IsDescending(index.IsDescending.ToArray());
                }

                if (index.FindAnnotation(MySqlAnnotationNames.IndexPrefixLength)
                        ?.Value is int[] prefixLengths)
                {
                    indexBuilder.Metadata.SetMySqlIndexPrefixLengths(prefixLengths);
                }

                if ((index.FindAnnotation(MySqlAnnotationNames.FullTextIndex)?.Value as bool?) == true)
                {
                    indexBuilder.Metadata.SetMySqlFullTextIndex(true);
                }

                if ((index.FindAnnotation(MySqlAnnotationNames.SpatialIndex)?.Value as bool?) == true)
                {
                    indexBuilder.Metadata.SetMySqlSpatialIndex(true);
                    _scaffoldingContext.MarkUsesNetTopologySuiteScaffolding();
                }
            }
        }

        foreach (var table in sortedTables)
        {
            foreach (var foreignKey in table.ForeignKeys)
            {
                if (foreignKey.Columns.Count == 0
                    || foreignKey.PrincipalColumns.Count == 0)
                {
                    continue;
                }

                if (!entityBuilders.TryGetValue(foreignKey.PrincipalTable, out var principalEntityBuilder))
                {
                    continue;
                }

                if (ContainsSkippedColumn(foreignKey.Columns, skippedColumns, table)
                    || ContainsSkippedColumn(foreignKey.PrincipalColumns, skippedColumns, foreignKey.PrincipalTable))
                {
                    continue;
                }

                var dependentEntityBuilder = entityBuilders[table];
                var dependentProperties = foreignKey
                    .Columns.Select(column => entityPropertyBuilders[(table, column)].Metadata)
                    .ToArray();

                var principalProperties = foreignKey
                    .PrincipalColumns.Select(
                        column => entityPropertyBuilders[(foreignKey.PrincipalTable, column)].Metadata)
                    .ToArray();

                var principalReadOnlyProperties = principalProperties
                    .Cast<IReadOnlyProperty>()
                    .ToArray();

                var principalKey = principalEntityBuilder.Metadata.FindKey(principalReadOnlyProperties)
                    ?? principalEntityBuilder.Metadata.AddKey(principalProperties);

                var relationship = dependentEntityBuilder.Metadata.AddForeignKey(
                    dependentProperties,
                    principalKey,
                    principalEntityBuilder.Metadata);

                if (!string.IsNullOrWhiteSpace(foreignKey.Name))
                {
                    relationship.SetConstraintName(foreignKey.Name);
                }

                relationship.DeleteBehavior = ConvertDeleteBehavior(foreignKey.OnDelete);
                relationship.SetDependentToPrincipal(
                    CreateUniqueClrIdentifier(
                        principalEntityBuilder.Metadata.Name,
                        propertyNamesByEntity[table],
                        "Principal"));
                relationship.SetPrincipalToDependent(
                    CreateUniqueClrIdentifier(
                        dependentEntityBuilder.Metadata.Name + "Collection",
                        propertyNamesByEntity[foreignKey.PrincipalTable],
                        "Dependents"));
            }
        }

        foreach (var sequence in databaseModel.Sequences.OrderBy(sequence => sequence.Name, StringComparer.Ordinal))
        {
            var sequenceType = string.IsNullOrWhiteSpace(sequence.StoreType)
                ? typeof(long)
                : _typeMappingSource.FindMapping(sequence.StoreType)?.ClrType
                    ?? typeof(long);

            var sequenceBuilder = modelBuilder.HasSequence(
                sequenceType,
                sequence.Name,
                sequence.Schema);

            if (sequence.StartValue is { } startValue)
            {
                sequenceBuilder.StartsAt(startValue);
            }

            if (sequence.IncrementBy is { } incrementBy)
            {
                sequenceBuilder.IncrementsBy(incrementBy);
            }

            if (sequence.MinValue is { } minValue)
            {
                sequenceBuilder.HasMin(minValue);
            }

            if (sequence.MaxValue is { } maxValue)
            {
                sequenceBuilder.HasMax(maxValue);
            }

            if (sequence.IsCyclic is { } isCyclic)
            {
                sequenceBuilder.IsCyclic(isCyclic);
            }
        }

        return modelBuilder.FinalizeModel();
    }

    private static IReadOnlyList<MySqlScaffoldedCheckConstraint> GetCheckConstraints(
        DatabaseTable table
    ) => table.FindAnnotation(MySqlAnnotationNames.ScaffoldingCheckConstraints)
        ?.Value as IReadOnlyList<MySqlScaffoldedCheckConstraint>
        ?? [];

    private static IReadOnlyList<MySqlScaffoldedIndexPart> GetScaffoldedIndexParts(
        DatabaseIndex index
    ) => index.FindAnnotation(MySqlAnnotationNames.ScaffoldingIndexParts)
        ?.Value as IReadOnlyList<MySqlScaffoldedIndexPart>
        ?? [];

    private static bool ContainsSkippedColumn(
        IEnumerable<DatabaseColumn> columns,
        HashSet<(DatabaseTable Table, DatabaseColumn Column)> skippedColumns,
        DatabaseTable table
    )
    {
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(skippedColumns);
        ArgumentNullException.ThrowIfNull(table);

        return columns.Any(column => skippedColumns.Contains((table, column)));
    }

    private static string[] GetPropertyNames(
        IEnumerable<DatabaseColumn> columns,
        Dictionary<(DatabaseTable Table, DatabaseColumn Column), PropertyBuilder> propertyBuilders,
        DatabaseTable table
    )
    {
        return columns
            .Select(column => propertyBuilders[(table, column)].Metadata.Name)
            .ToArray();
    }

    private static DeleteBehavior ConvertDeleteBehavior(
        ReferentialAction? referentialAction
    ) => referentialAction switch
    {
        ReferentialAction.Cascade => DeleteBehavior.Cascade,
        ReferentialAction.SetNull => DeleteBehavior.SetNull,
        ReferentialAction.SetDefault => DeleteBehavior.ClientSetNull,
        ReferentialAction.Restrict => DeleteBehavior.Restrict,
        ReferentialAction.NoAction => DeleteBehavior.NoAction,
        _ => DeleteBehavior.ClientSetNull,
    };
}
