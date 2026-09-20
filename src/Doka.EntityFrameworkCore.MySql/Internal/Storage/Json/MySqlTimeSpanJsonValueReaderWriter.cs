namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Reads and writes <see cref="TimeSpan"/> JSON values within the
/// MySQL-family <c>TIME</c> domain.
/// </summary>
internal sealed class MySqlTimeSpanJsonValueReaderWriter : JsonValueReaderWriter<TimeSpan>
{
    private static readonly PropertyInfo s_instanceProperty =
        typeof(MySqlTimeSpanJsonValueReaderWriter).GetProperty(nameof(Instance))!;

    /// <summary>
    /// Gets the reusable JSON reader/writer.
    /// </summary>
    public static MySqlTimeSpanJsonValueReaderWriter Instance { get; } = new();

    private MySqlTimeSpanJsonValueReaderWriter() { }

    /// <inheritdoc />
    public override TimeSpan FromJsonTyped(
        ref Utf8JsonReaderManager manager,
        object? existingObject = null
    ) => JsonTimeSpanReaderWriter.Instance.FromJsonTyped(ref manager, existingObject);

    /// <inheritdoc />
    public override void ToJsonTyped(
        Utf8JsonWriter writer,
        TimeSpan value
    )
    {
        // MySQL silently saturates out-of-range TIME casts. Validate before
        // transport so invalid values cannot become false-positive matches.
        MySqlTimeSpanTypeMapping.ValidateStoreRange(value);

        JsonTimeSpanReaderWriter.Instance.ToJsonTyped(writer, value);
    }

    /// <inheritdoc />
    public override Expression ConstructorExpression => Expression.Property(null, s_instanceProperty);
}
