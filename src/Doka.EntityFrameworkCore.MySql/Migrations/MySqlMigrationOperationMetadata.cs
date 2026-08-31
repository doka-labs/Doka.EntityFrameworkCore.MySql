namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Provides an immutable, typed snapshot of Doka metadata attached to one
/// EF Core migration operation.
/// </summary>
public sealed class MySqlMigrationOperationMetadata
{
    private static readonly MySqlMigrationOperationMetadata s_empty = new(
        guidFormat: null,
        valueGenerationStrategy: null,
        indexPrefixLengths: null);

    private MySqlMigrationOperationMetadata(
        MySqlGuidFormat? guidFormat,
        MySqlValueGenerationStrategy? valueGenerationStrategy,
        IReadOnlyList<int>? indexPrefixLengths
    )
    {
        GuidFormat = guidFormat;
        ValueGenerationStrategy = valueGenerationStrategy;
        IndexPrefixLengths = indexPrefixLengths;
    }

    /// <summary>
    /// Gets the physical GUID storage format for a column operation, when
    /// provider metadata declares one.
    /// </summary>
    public MySqlGuidFormat? GuidFormat { get; }

    /// <summary>
    /// Gets the provider value-generation strategy for a column operation,
    /// when one is explicitly present.
    /// </summary>
    public MySqlValueGenerationStrategy? ValueGenerationStrategy { get; }

    /// <summary>
    /// Gets the ordered index prefix lengths for a create-index operation.
    /// A zero entry means that the complete key is indexed.
    /// </summary>
    public IReadOnlyList<int>? IndexPrefixLengths { get; }

    internal static MySqlMigrationOperationMetadata Create(
        MigrationOperation operation
    )
    {
        ArgumentNullException.ThrowIfNull(operation);

        var guidFormat = ReadGuidFormat(operation);
        var valueGenerationStrategy = ReadValueGenerationStrategy(operation);
        var indexPrefixLengths = ReadIndexPrefixLengths(operation);

        return guidFormat is null && valueGenerationStrategy is null && indexPrefixLengths is null
            ? s_empty
            : new MySqlMigrationOperationMetadata(guidFormat, valueGenerationStrategy, indexPrefixLengths);
    }

    private static MySqlGuidFormat? ReadGuidFormat(
        MigrationOperation operation
    )
    {
        var annotation = operation.FindAnnotation(MySqlAnnotationNames.GuidFormat);
        if (annotation is null)
        {
            return null;
        }

        if (operation is not ColumnOperation columnOperation)
        {
            throw CreateOperationShapeException(operation, "GUID format", nameof(ColumnOperation));
        }

        if (annotation.Value is not MySqlGuidFormat format
            || !Enum.IsDefined(format))
        {
            throw CreateValueException(operation, "GUID format");
        }

        var modelClrType = Nullable.GetUnderlyingType(columnOperation.ClrType) ?? columnOperation.ClrType;

        if (modelClrType != typeof(Guid)
            || !MatchesGuidStoreType(columnOperation.ColumnType, format))
        {
            throw new InvalidOperationException(
                $"The GUID format metadata on migration operation "
                + $"'{operation.GetType().FullName}' conflicts with its column shape.");
        }

        return format;
    }

    private static bool MatchesGuidStoreType(
        string? columnType,
        MySqlGuidFormat format
    )
    {
        if (string.IsNullOrWhiteSpace(columnType))
        {
            return true;
        }

        var expectedStoreType = format == MySqlGuidFormat.Binary16 ? "binary(16)" : "char(36)";

        return columnType
            .AsSpan()
            .Trim()
            .Equals(expectedStoreType, StringComparison.OrdinalIgnoreCase);
    }

    private static MySqlValueGenerationStrategy? ReadValueGenerationStrategy(
        MigrationOperation operation
    )
    {
        var annotation = operation.FindAnnotation(MySqlAnnotationNames.ValueGenerationStrategy);
        if (annotation is null)
        {
            return null;
        }

        if (operation is not ColumnOperation)
        {
            throw CreateOperationShapeException(operation, "value-generation", nameof(ColumnOperation));
        }

        return annotation.Value is MySqlValueGenerationStrategy strategy
            ? strategy
            : throw CreateValueException(operation, "value-generation");
    }

    private static ReadOnlyCollection<int>? ReadIndexPrefixLengths(
        MigrationOperation operation
    )
    {
        var annotation = operation.FindAnnotation(MySqlAnnotationNames.IndexPrefixLength);
        if (annotation is null)
        {
            return null;
        }

        if (operation is not CreateIndexOperation createIndexOperation)
        {
            throw CreateOperationShapeException(operation, "index-prefix", nameof(CreateIndexOperation));
        }

        if (annotation.Value is not int[] prefixLengths
            || prefixLengths.Any(static prefixLength => prefixLength < 0))
        {
            throw CreateValueException(operation, "index-prefix");
        }

        if (createIndexOperation.Columns is null
            || prefixLengths.Length != createIndexOperation.Columns.Length)
        {
            throw new InvalidOperationException(
                $"The index-prefix metadata on migration operation "
                + $"'{operation.GetType().FullName}' must contain one entry per index key.");
        }

        return Array.AsReadOnly(prefixLengths.ToArray());
    }

    private static InvalidOperationException CreateOperationShapeException(
        MigrationOperation operation,
        string metadataRole,
        string requiredOperationType
    ) => new(
        $"The {metadataRole} metadata on migration operation "
        + $"'{operation.GetType().FullName}' requires a {requiredOperationType}.");

    private static InvalidOperationException CreateValueException(
        MigrationOperation operation,
        string metadataRole
    ) => new(
        $"The {metadataRole} metadata on migration operation "
        + $"'{operation.GetType().FullName}' has an invalid value.");
}

/// <summary>
/// Provides typed, read-only access to Doka metadata on EF Core migration
/// operations without exposing provider annotation identities.
/// </summary>
public static class MySqlMigrationOperationExtensions
{
    /// <summary>
    /// Creates an immutable snapshot of supported Doka metadata attached to an
    /// EF Core migration operation.
    /// </summary>
    /// <param name="operation">The migration operation to inspect.</param>
    /// <returns>The typed provider metadata snapshot.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="operation" /> is <see langword="null" />.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Known provider metadata is malformed or attached to an incompatible
    /// operation shape.
    /// </exception>
    public static MySqlMigrationOperationMetadata GetMySqlMigrationMetadata(
        this MigrationOperation operation
    ) => MySqlMigrationOperationMetadata.Create(operation);
}
