namespace Doka.EntityFrameworkCore.MySql;

internal sealed class MySqlScaffoldingModelFactory : IScaffoldingModelFactory
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
            propertyNamesByEntity[table] = new HashSet<string>(StringComparer.Ordinal);
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

            if (sequence.StartValue is long startValue)
            {
                sequenceBuilder.StartsAt(startValue);
            }

            if (sequence.IncrementBy is int incrementBy)
            {
                sequenceBuilder.IncrementsBy(incrementBy);
            }

            if (sequence.MinValue is long minValue)
            {
                sequenceBuilder.HasMin(minValue);
            }

            if (sequence.MaxValue is long maxValue)
            {
                sequenceBuilder.HasMax(maxValue);
            }

            if (sequence.IsCyclic is bool isCyclic)
            {
                sequenceBuilder.IsCyclic(isCyclic);
            }
        }

        return modelBuilder.FinalizeModel();
    }

    private static void ApplyTemporalConfiguration(
        DatabaseTable table,
        EntityTypeBuilder entityBuilder,
        Dictionary<(DatabaseTable Table, DatabaseColumn Column), PropertyBuilder> propertyBuilders
    )
    {
        if (table.FindAnnotation(MySqlAnnotationNames.TemporalSourceIsTemporal)
                ?.Value is not true)
        {
            return;
        }

        var periodStartColumnName = table.FindAnnotation(MySqlAnnotationNames.TemporalSourcePeriodStartColumn)
            ?.Value as string;

        var periodEndColumnName = table.FindAnnotation(MySqlAnnotationNames.TemporalSourcePeriodEndColumn)
            ?.Value as string;

        if (string.IsNullOrWhiteSpace(periodStartColumnName)
            || string.IsNullOrWhiteSpace(periodEndColumnName))
        {
            throw new InvalidOperationException(
                $"Temporal table '{table.Name}' does not expose both period-column names.");
        }

        var periodStartColumn = table.Columns.Single(column => string.Equals(
            column.Name,
            periodStartColumnName,
            StringComparison.Ordinal));

        var periodEndColumn = table.Columns.Single(column => string.Equals(
            column.Name,
            periodEndColumnName,
            StringComparison.Ordinal));

        var periodStartProperty = propertyBuilders[(table, periodStartColumn)];
        var periodEndProperty = propertyBuilders[(table, periodEndColumn)];

        entityBuilder.Metadata.SetMySqlTemporal(true);
        entityBuilder.Metadata.SetMySqlTemporalPeriodStartPropertyName(periodStartProperty.Metadata.Name);
        entityBuilder.Metadata.SetMySqlTemporalPeriodEndPropertyName(periodEndProperty.Metadata.Name);
        periodStartProperty.ValueGeneratedOnAddOrUpdate();
        periodEndProperty.ValueGeneratedOnAddOrUpdate();

        if (table.FindAnnotation(MySqlAnnotationNames.TemporalSourceHistoryTable)
                ?.Value is string historyTableName)
        {
            entityBuilder.Metadata.SetMySqlTemporalHistoryTableName(historyTableName);
        }

        if (table.FindAnnotation(MySqlAnnotationNames.TemporalSourceHistorySchema)
                ?.Value is string historyTableSchema)
        {
            entityBuilder.Metadata.SetMySqlTemporalHistoryTableSchema(historyTableSchema);
        }
    }

    private static void ApplyApplicationTimeConfiguration(
        DatabaseTable table,
        EntityTypeBuilder entityBuilder,
        Dictionary<(DatabaseTable Table, DatabaseColumn Column), PropertyBuilder> propertyBuilders
    )
    {
        if (table.FindAnnotation(MySqlAnnotationNames.IsApplicationTime)
                ?.Value is not true)
        {
            return;
        }

        var periodName = RequireApplicationTimeAnnotation(table, MySqlAnnotationNames.ApplicationTimePeriodName);

        var periodStartColumnName = RequireApplicationTimeAnnotation(
            table,
            MySqlAnnotationNames.ApplicationTimePeriodStartColumn);

        var periodEndColumnName = RequireApplicationTimeAnnotation(
            table,
            MySqlAnnotationNames.ApplicationTimePeriodEndColumn);

        var periodStartColumn = table.Columns.Single(column => string.Equals(
            column.Name,
            periodStartColumnName,
            StringComparison.Ordinal));

        var periodEndColumn = table.Columns.Single(column => string.Equals(
            column.Name,
            periodEndColumnName,
            StringComparison.Ordinal));

        var periodStartProperty = propertyBuilders[(table, periodStartColumn)];
        var periodEndProperty = propertyBuilders[(table, periodEndColumn)];

        entityBuilder.Metadata.SetMySqlApplicationTime(true);
        entityBuilder.Metadata.SetMySqlApplicationTimePeriodName(periodName);
        entityBuilder.Metadata.SetMySqlApplicationTimePeriodStartPropertyName(periodStartProperty.Metadata.Name);
        entityBuilder.Metadata.SetMySqlApplicationTimePeriodEndPropertyName(periodEndProperty.Metadata.Name);
        periodStartProperty.ValueGeneratedNever();
        periodEndProperty.ValueGeneratedNever();
    }

    private static string RequireApplicationTimeAnnotation(
        DatabaseTable table,
        string annotationName
    ) => table.FindAnnotation(annotationName)?.Value as string is { } value
        && !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new InvalidOperationException(
                $"Application-time table '{table.Name}' does not define required annotation '{annotationName}'.");

    private static void ApplyColumnConfiguration(
        PropertyBuilder propertyBuilder,
        DatabaseColumn column,
        ColumnMapping mapping
    )
    {
        ArgumentNullException.ThrowIfNull(propertyBuilder);
        ArgumentNullException.ThrowIfNull(column);

        propertyBuilder.HasColumnName(column.Name);
        propertyBuilder.HasColumnType(column.StoreType);
        propertyBuilder.Metadata.SetColumnOrder(column.Table.Columns.IndexOf(column));

        if (!column.IsNullable)
        {
            propertyBuilder.IsRequired();
        }

        if (mapping.MaxLength is not null)
        {
            propertyBuilder.HasMaxLength(mapping.MaxLength.Value);
        }

        if (mapping.IsFixedLength is not null)
        {
            propertyBuilder.IsFixedLength(mapping.IsFixedLength.Value);
        }

        if (mapping.Precision is not null
            && mapping.Scale is not null)
        {
            propertyBuilder.HasPrecision(mapping.Precision.Value, mapping.Scale.Value);
        }
        else if (mapping.Precision is not null)
        {
            propertyBuilder.HasPrecision(mapping.Precision.Value);
        }

        if (!string.IsNullOrWhiteSpace(column.Collation))
        {
            propertyBuilder.UseCollation(column.Collation);
        }

        if (!string.IsNullOrWhiteSpace(column.Comment))
        {
            propertyBuilder.HasComment(column.Comment);
        }

        if (!string.IsNullOrWhiteSpace(column.ComputedColumnSql))
        {
            propertyBuilder.HasComputedColumnSql(column.ComputedColumnSql, column.IsStored);
        }
        else if (column.DefaultValueSql is not null)
        {
            propertyBuilder.HasDefaultValueSql(column.DefaultValueSql);
        }

        if (column.ValueGenerated == ValueGenerated.OnAdd)
        {
            propertyBuilder.ValueGeneratedOnAdd();
        }

        if (mapping.GuidFormat is not null)
        {
            propertyBuilder.Metadata.SetMySqlGuidFormat(mapping.GuidFormat.Value);
        }

        if (column.FindAnnotation(MySqlAnnotationNames.SpatialReferenceSystemId)
                ?.Value is int spatialReferenceSystemId)
        {
            propertyBuilder.Metadata.SetMySqlSpatialReferenceSystemId(spatialReferenceSystemId);
        }
    }

    private static Type GetPropertyClrType(
        Type clrType,
        bool isNullable
    )
    {
        if (!isNullable
            || !clrType.IsValueType
            || Nullable.GetUnderlyingType(clrType) is not null)
        {
            return clrType;
        }

        return clrType == typeof(bool) ? typeof(bool?)
            : clrType == typeof(byte) ? typeof(byte?)
            : clrType == typeof(sbyte) ? typeof(sbyte?)
            : clrType == typeof(short) ? typeof(short?)
            : clrType == typeof(ushort) ? typeof(ushort?)
            : clrType == typeof(int) ? typeof(int?)
            : clrType == typeof(uint) ? typeof(uint?)
            : clrType == typeof(long) ? typeof(long?)
            : clrType == typeof(ulong) ? typeof(ulong?)
            : clrType == typeof(float) ? typeof(float?)
            : clrType == typeof(double) ? typeof(double?)
            : clrType == typeof(decimal) ? typeof(decimal?)
            : clrType == typeof(Guid) ? typeof(Guid?)
            : clrType == typeof(DateTime) ? typeof(DateTime?)
            : clrType == typeof(DateOnly) ? typeof(DateOnly?)
            : clrType == typeof(TimeOnly) ? typeof(TimeOnly?)
            : throw new InvalidOperationException(
                $"The scaffolded value type '{clrType}' has no AOT-safe nullable mapping.");
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

    private bool TryHandleSpatialColumn(
        DatabaseTable table,
        DatabaseColumn column,
        EntityTypeBuilder entityBuilder,
        HashSet<string> usedPropertyNames,
        Dictionary<(DatabaseTable Table, DatabaseColumn Column), PropertyBuilder> entityPropertyBuilders,
        HashSet<(DatabaseTable Table, DatabaseColumn Column)> skippedColumns
    )
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(column);
        ArgumentNullException.ThrowIfNull(entityBuilder);
        ArgumentNullException.ThrowIfNull(usedPropertyNames);
        ArgumentNullException.ThrowIfNull(entityPropertyBuilders);
        ArgumentNullException.ThrowIfNull(skippedColumns);

        if (!MySqlSpatialTypeSupport.IsSpatialStoreType(column.StoreType))
        {
            return false;
        }

        if (_spatialTypeProvider is null
            || !_spatialTypeProvider.TryResolveClrType(column.StoreType, out var spatialClrType)
            || spatialClrType is null)
        {
            skippedColumns.Add((table, column));
            MySqlLoggerMessages.MissingSpatialPackageDuringScaffolding(
                _logger,
                table.Name,
                column.Name);

            return true;
        }

        var propertyName = CreateUniqueClrIdentifier(column.Name, usedPropertyNames, "Geometry");
        var propertyBuilder = entityBuilder.Property(spatialClrType, propertyName);

        ApplyColumnConfiguration(propertyBuilder, column, ColumnMapping.Scalar(spatialClrType));

        entityPropertyBuilders[(table, column)] = propertyBuilder;
        _scaffoldingContext.MarkUsesNetTopologySuiteScaffolding();

        return true;
    }

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

    private ColumnMapping ResolveColumnMapping(
        DatabaseColumn column
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(column.StoreType);

        var storeType = column.StoreType;
        var normalizedStoreType = storeType.ToLowerInvariant();

        if (normalizedStoreType is "char(36)" or "varchar(36)")
        {
            if (_reverseEngineeringOptions.ScaffoldTextGuidsAsGuids)
            {
                return new ColumnMapping(
                    typeof(Guid),
                    MaxLength: 36,
                    IsFixedLength: normalizedStoreType == "char(36)",
                    Precision: null,
                    Scale: null,
                    GuidFormat: normalizedStoreType == "char(36)" ? MySqlGuidFormat.Char36 : null);
            }

            return new ColumnMapping(
                typeof(string),
                MaxLength: 36,
                IsFixedLength: normalizedStoreType == "char(36)",
                Precision: null,
                Scale: null,
                GuidFormat: null);
        }

        if (normalizedStoreType == "binary(16)")
        {
            return new ColumnMapping(
                typeof(Guid),
                MaxLength: 16,
                IsFixedLength: true,
                Precision: null,
                Scale: null,
                GuidFormat: MySqlGuidFormat.Binary16);
        }

        if (normalizedStoreType.StartsWith("binary(", StringComparison.Ordinal)
            || normalizedStoreType == "binary")
        {
            return new ColumnMapping(
                typeof(byte[]),
                MaxLength: ParseSingleFacet(storeType),
                IsFixedLength: true,
                Precision: null,
                Scale: null,
                GuidFormat: null);
        }

        if (normalizedStoreType.StartsWith("varchar(", StringComparison.Ordinal)
            || normalizedStoreType == "varchar")
        {
            return new ColumnMapping(
                typeof(string),
                MaxLength: ParseSingleFacet(storeType),
                IsFixedLength: false,
                Precision: null,
                Scale: null,
                GuidFormat: null);
        }

        if (normalizedStoreType.StartsWith("char(", StringComparison.Ordinal)
            || normalizedStoreType == "char")
        {
            return new ColumnMapping(
                typeof(string),
                MaxLength: ParseSingleFacet(storeType),
                IsFixedLength: true,
                Precision: null,
                Scale: null,
                GuidFormat: null);
        }

        if (normalizedStoreType.StartsWith("varbinary(", StringComparison.Ordinal)
            || normalizedStoreType == "varbinary")
        {
            return new ColumnMapping(
                typeof(byte[]),
                MaxLength: ParseSingleFacet(storeType),
                IsFixedLength: false,
                Precision: null,
                Scale: null,
                GuidFormat: null);
        }

        if (normalizedStoreType.StartsWith("decimal(", StringComparison.Ordinal))
        {
            var (precision, scale) = ParsePrecisionAndScale(storeType);

            return new ColumnMapping(
                typeof(decimal),
                MaxLength: null,
                IsFixedLength: null,
                Precision: precision,
                Scale: scale,
                GuidFormat: null);
        }

        if (normalizedStoreType.StartsWith("enum(", StringComparison.Ordinal)
            || normalizedStoreType.StartsWith("set(", StringComparison.Ordinal))
        {
            return ColumnMapping.Scalar(typeof(string));
        }

        if (normalizedStoreType.StartsWith("bit(", StringComparison.Ordinal))
        {
            return normalizedStoreType == "bit(1)"
                ? ColumnMapping.Scalar(typeof(bool))
                : ColumnMapping.Scalar(typeof(ulong));
        }

        if (normalizedStoreType.StartsWith("datetime(", StringComparison.Ordinal)
            || normalizedStoreType.StartsWith("timestamp(", StringComparison.Ordinal))
        {
            return ColumnMapping.Scalar(typeof(DateTime));
        }

        if (normalizedStoreType.StartsWith("time(", StringComparison.Ordinal))
        {
            return ColumnMapping.Scalar(typeof(TimeOnly));
        }

        return normalizedStoreType switch
        {
            "int" or "integer" => ColumnMapping.Scalar(typeof(int)),
            "bigint" => ColumnMapping.Scalar(typeof(long)),
            "smallint" => ColumnMapping.Scalar(typeof(short)),
            "mediumint" => ColumnMapping.Scalar(typeof(int)),
            "tinyint" => ColumnMapping.Scalar(typeof(sbyte)),
            "tinyint(1)" => ColumnMapping.Scalar(typeof(bool)),
            "tinyint unsigned" => ColumnMapping.Scalar(typeof(byte)),
            "smallint unsigned" => ColumnMapping.Scalar(typeof(ushort)),
            "mediumint unsigned" => ColumnMapping.Scalar(typeof(uint)),
            "int unsigned" => ColumnMapping.Scalar(typeof(uint)),
            "bigint unsigned" => ColumnMapping.Scalar(typeof(ulong)),
            "double" or "double unsigned" => ColumnMapping.Scalar(typeof(double)),
            "float" or "float unsigned" => ColumnMapping.Scalar(typeof(float)),
            "year" => ColumnMapping.Scalar(typeof(short)),
            "bit" => ColumnMapping.Scalar(typeof(bool)),
            "datetime" or "datetime(6)" or "timestamp" or "timestamp(6)" => ColumnMapping.Scalar(typeof(DateTime)),
            "date" => ColumnMapping.Scalar(typeof(DateOnly)),
            "time" or "time(6)" => ColumnMapping.Scalar(typeof(TimeOnly)),
            "json" or "longtext" or "mediumtext" or "text" or "tinytext" =>
                ColumnMapping.Scalar(typeof(string)),
            "longblob" or "mediumblob" or "blob" or "tinyblob" =>
                ColumnMapping.Scalar(typeof(byte[])),
            _ => ColumnMapping.Scalar(typeof(string)),
        };
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

    private static string CreateUniqueClrIdentifier(
        string databaseIdentifier,
        HashSet<string> usedNames,
        string fallbackRoot
    )
    {
        var candidate = SanitizeToPascalCase(databaseIdentifier);

        if (string.IsNullOrWhiteSpace(candidate))
        {
            candidate = fallbackRoot;
        }

        if (!char.IsLetter(candidate[0])
            && candidate[0] != '_')
        {
            candidate = "_" + candidate;
        }

        var uniqueCandidate = candidate;
        var suffix = 1;

        while (!usedNames.Add(uniqueCandidate))
        {
            uniqueCandidate = candidate + suffix.ToString(CultureInfo.InvariantCulture);
            suffix++;
        }

        return uniqueCandidate;
    }

    private static string SanitizeToPascalCase(
        string value
    )
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        var nextUpper = true;

        foreach (var character in value)
        {
            if (!char.IsLetterOrDigit(character))
            {
                nextUpper = true;
                continue;
            }

            builder.Append(nextUpper ? char.ToUpperInvariant(character) : character);
            nextUpper = false;
        }

        return builder.ToString();
    }

    private static int? ParseSingleFacet(
        string storeType
    )
    {
        var startIndex = storeType.IndexOf('(');
        var endIndex = storeType.IndexOf(')');

        if (startIndex < 0
            || endIndex <= startIndex + 1)
        {
            return null;
        }

        return int.TryParse(
            storeType[(startIndex + 1)..endIndex],
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var size)
            ? size
            : null;
    }

    private static (int? Precision, int? Scale) ParsePrecisionAndScale(
        string storeType
    )
    {
        var startIndex = storeType.IndexOf('(');
        var endIndex = storeType.IndexOf(')');

        if (startIndex < 0
            || endIndex <= startIndex + 1)
        {
            return (null, null);
        }

        var parts = storeType[(startIndex + 1)..endIndex]
            .Split(',', StringSplitOptions.TrimEntries);

        if (parts.Length == 0
            || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var precision))
        {
            return (null, null);
        }

        if (parts.Length == 1
            || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var scale))
        {
            return (precision, null);
        }

        return (precision, scale);
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

    private readonly record struct ColumnMapping(
        Type ClrType,
        int? MaxLength,
        bool? IsFixedLength,
        int? Precision,
        int? Scale,
        MySqlGuidFormat? GuidFormat
    )
    {
        public static ColumnMapping Scalar(
            Type clrType
        ) => new(clrType, null, null, null, null, null);
    }
}
