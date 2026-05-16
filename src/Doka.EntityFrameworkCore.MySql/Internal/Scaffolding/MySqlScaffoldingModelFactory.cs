namespace Doka.EntityFrameworkCore.MySql;

internal sealed class MySqlScaffoldingModelFactory : IScaffoldingModelFactory
{
    private readonly MySqlReverseEngineeringOptions _reverseEngineeringOptions;
    private readonly MySqlScaffoldingContext _scaffoldingContext;
    private readonly IMySqlSpatialTypeProvider? _spatialTypeProvider;
    private readonly ILogger _logger;

    public MySqlScaffoldingModelFactory(
        MySqlReverseEngineeringOptions reverseEngineeringOptions,
        MySqlScaffoldingContext scaffoldingContext,
        IEnumerable<IMySqlSpatialTypeProvider> spatialTypeProviders,
        ILoggerFactory loggerFactory
    )
    {
        ArgumentNullException.ThrowIfNull(spatialTypeProviders);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        _reverseEngineeringOptions = reverseEngineeringOptions ?? throw new ArgumentNullException(nameof(reverseEngineeringOptions));
        _scaffoldingContext = scaffoldingContext ?? throw new ArgumentNullException(nameof(scaffoldingContext));
        _spatialTypeProvider = spatialTypeProviders.SingleOrDefault();
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

            entityBuilder.ToTable(
                table.Name,
                tableBuilder =>
                {
                    if (!string.IsNullOrWhiteSpace(table.Comment))
                    {
                        tableBuilder.HasComment(table.Comment);
                    }
                });

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
                var propertyBuilder = entityBuilder.Property(mapping.ClrType, propertyName);

                ApplyColumnConfiguration(propertyBuilder, column, mapping);

                entityPropertyBuilders[(table, column)] = propertyBuilder;
            }
        }

        foreach (var table in sortedTables)
        {
            var entityBuilder = entityBuilders[table];

            if (table.PrimaryKey is not null
                && table.PrimaryKey.Columns.Count > 0)
            {
                if (!ContainsSkippedColumn(table.PrimaryKey.Columns, skippedColumns, table))
                {
                    entityBuilder.HasKey(GetPropertyNames(table.PrimaryKey.Columns, entityPropertyBuilders, table));
                }
            }

            foreach (var uniqueConstraint in table.UniqueConstraints)
            {
                if (uniqueConstraint.Columns.Count > 0
                    && !ContainsSkippedColumn(uniqueConstraint.Columns, skippedColumns, table))
                {
                    entityBuilder.HasAlternateKey(
                        GetPropertyNames(uniqueConstraint.Columns, entityPropertyBuilders, table));
                }
            }

            foreach (var index in table.Indexes)
            {
                if (index.Columns.Count == 0)
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
                var dependentPropertyNames = GetPropertyNames(foreignKey.Columns, entityPropertyBuilders, table);
                var principalPropertyNames = GetPropertyNames(
                    foreignKey.PrincipalColumns,
                    entityPropertyBuilders,
                    foreignKey.PrincipalTable);

                var relationshipBuilder = dependentEntityBuilder
                    .HasOne(principalEntityBuilder.Metadata.ClrType)
                    .WithMany()
                    .HasForeignKey(dependentPropertyNames)
                    .HasPrincipalKey(principalPropertyNames);

                if (!string.IsNullOrWhiteSpace(foreignKey.Name))
                {
                    relationshipBuilder.HasConstraintName(foreignKey.Name);
                }

                relationshipBuilder.OnDelete(ConvertDeleteBehavior(foreignKey.OnDelete));
            }
        }

        return modelBuilder.FinalizeModel();
    }

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

        if (!string.IsNullOrWhiteSpace(column.ComputedColumnSql))
        {
            propertyBuilder.HasComputedColumnSql(column.ComputedColumnSql, column.IsStored);
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

        return normalizedStoreType switch
        {
            "int" or "integer" => ColumnMapping.Scalar(typeof(int)),
            "bigint" => ColumnMapping.Scalar(typeof(long)),
            "smallint" => ColumnMapping.Scalar(typeof(short)),
            "tinyint" => ColumnMapping.Scalar(typeof(sbyte)),
            "tinyint(1)" => ColumnMapping.Scalar(typeof(bool)),
            "tinyint unsigned" => ColumnMapping.Scalar(typeof(byte)),
            "smallint unsigned" => ColumnMapping.Scalar(typeof(ushort)),
            "int unsigned" => ColumnMapping.Scalar(typeof(uint)),
            "bigint unsigned" => ColumnMapping.Scalar(typeof(ulong)),
            "double" => ColumnMapping.Scalar(typeof(double)),
            "float" => ColumnMapping.Scalar(typeof(float)),
            "datetime" or "datetime(6)" or "timestamp" or "timestamp(6)" => ColumnMapping.Scalar(typeof(DateTime)),
            "date" => ColumnMapping.Scalar(typeof(DateOnly)),
            "time" or "time(6)" => ColumnMapping.Scalar(typeof(TimeOnly)),
            "json" or "longtext" or "text" => ColumnMapping.Scalar(typeof(string)),
            "longblob" or "blob" => ColumnMapping.Scalar(typeof(byte[])),
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
