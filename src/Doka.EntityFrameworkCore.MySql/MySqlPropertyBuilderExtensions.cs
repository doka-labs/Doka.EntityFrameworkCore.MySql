namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Adds provider-specific property configuration extensions for MySQL-family metadata.
/// </summary>
public static class MySqlPropertyBuilderExtensions
{
    /// <summary>
    /// Configures the provider-managed value-generation strategy for a property.
    /// </summary>
    /// <param name="propertyBuilder">The property builder.</param>
    /// <param name="strategy">The provider-specific value-generation strategy.</param>
    /// <returns>The same <see cref="PropertyBuilder"/> instance.</returns>
    public static PropertyBuilder HasMySqlValueGenerationStrategy(
        this PropertyBuilder propertyBuilder,
        MySqlValueGenerationStrategy strategy
    )
    {
        ArgumentNullException.ThrowIfNull(propertyBuilder);

        if (!Enum.IsDefined(strategy))
        {
            throw new ArgumentOutOfRangeException(nameof(strategy));
        }

        propertyBuilder.Metadata.SetMySqlValueGenerationStrategy(strategy);

        switch (strategy)
        {
            case MySqlValueGenerationStrategy.None:
                propertyBuilder.ValueGeneratedNever();
                break;
            case MySqlValueGenerationStrategy.AutoIncrement:
            case MySqlValueGenerationStrategy.ClientGuid:
            case MySqlValueGenerationStrategy.HiLo:
                propertyBuilder.ValueGeneratedOnAdd();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(strategy));
        }

        return propertyBuilder;
    }

    /// <summary>
    /// Configures the provider-managed value-generation strategy for a typed property.
    /// </summary>
    /// <typeparam name="TProperty">The property CLR type.</typeparam>
    /// <param name="propertyBuilder">The property builder.</param>
    /// <param name="strategy">The provider-specific value-generation strategy.</param>
    /// <returns>The same <see cref="PropertyBuilder{TProperty}"/> instance.</returns>
    public static PropertyBuilder<TProperty> HasMySqlValueGenerationStrategy<TProperty>(
        this PropertyBuilder<TProperty> propertyBuilder,
        MySqlValueGenerationStrategy strategy
    )
    {
        ArgumentNullException.ThrowIfNull(propertyBuilder);

        ((PropertyBuilder)propertyBuilder).HasMySqlValueGenerationStrategy(strategy);

        return propertyBuilder;
    }

    /// <summary>
    /// Configures an integer property to use MySQL-family auto-increment semantics.
    /// </summary>
    /// <param name="propertyBuilder">The property builder.</param>
    /// <returns>The same <see cref="PropertyBuilder"/> instance.</returns>
    public static PropertyBuilder UseMySqlAutoIncrementColumn(
        this PropertyBuilder propertyBuilder
    )
    {
        ArgumentNullException.ThrowIfNull(propertyBuilder);

        return propertyBuilder.HasMySqlValueGenerationStrategy(MySqlValueGenerationStrategy.AutoIncrement);
    }

    /// <summary>
    /// Configures an integer property to use MySQL-family auto-increment semantics.
    /// </summary>
    /// <typeparam name="TProperty">The property CLR type.</typeparam>
    /// <param name="propertyBuilder">The property builder.</param>
    /// <returns>The same <see cref="PropertyBuilder{TProperty}"/> instance.</returns>
    public static PropertyBuilder<TProperty> UseMySqlAutoIncrementColumn<TProperty>(
        this PropertyBuilder<TProperty> propertyBuilder
    )
    {
        ArgumentNullException.ThrowIfNull(propertyBuilder);

        UseMySqlAutoIncrementColumn((PropertyBuilder)propertyBuilder);

        return propertyBuilder;
    }

    /// <summary>
    /// Configures a GUID property to use explicit client-side value generation.
    /// </summary>
    /// <param name="propertyBuilder">The property builder.</param>
    /// <returns>The same <see cref="PropertyBuilder"/> instance.</returns>
    public static PropertyBuilder UseMySqlClientGuidValueGeneration(
        this PropertyBuilder propertyBuilder
    )
    {
        ArgumentNullException.ThrowIfNull(propertyBuilder);

        return propertyBuilder.HasMySqlValueGenerationStrategy(MySqlValueGenerationStrategy.ClientGuid);
    }

    /// <summary>
    /// Configures a GUID property to use explicit client-side value generation.
    /// </summary>
    /// <typeparam name="TProperty">The property CLR type.</typeparam>
    /// <param name="propertyBuilder">The property builder.</param>
    /// <returns>The same <see cref="PropertyBuilder{TProperty}"/> instance.</returns>
    public static PropertyBuilder<TProperty> UseMySqlClientGuidValueGeneration<TProperty>(
        this PropertyBuilder<TProperty> propertyBuilder
    )
    {
        ArgumentNullException.ThrowIfNull(propertyBuilder);

        ((PropertyBuilder)propertyBuilder).UseMySqlClientGuidValueGeneration();

        return propertyBuilder;
    }

    /// <summary>
    /// Configures an integer property to use Hi/Lo value generation backed by a sequence.
    /// </summary>
    /// <param name="propertyBuilder">The property builder.</param>
    /// <param name="name">The sequence name (defaults to a convention-based name if <c>null</c>).</param>
    /// <param name="schema">The sequence schema (unused on MySQL; included for API compatibility).</param>
    /// <returns>The same <see cref="PropertyBuilder"/> instance.</returns>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "ApiDesign",
        "RS0026:Do not add multiple overloads with optional parameters",
        Justification = "EF Core community standard: HiLo sequence name and schema are optional and default to convention. See ADR D-008.")]
    public static PropertyBuilder UseHiLo(
        this PropertyBuilder propertyBuilder,
        string? name = null,
        string? schema = null
    )
    {
        ArgumentNullException.ThrowIfNull(propertyBuilder);

        var property = propertyBuilder.Metadata;
        name ??= $"EntityFrameworkHiLoSequence_{property.DeclaringType.ShortName()}";

        var model = property.DeclaringType.Model;

        if (model.FindSequence(name, schema) is null)
        {
            model.AddSequence(name, schema)
                .IncrementBy = 10;
        }

        propertyBuilder.HasMySqlValueGenerationStrategy(MySqlValueGenerationStrategy.HiLo);
        property.SetAnnotation(MySqlAnnotationNames.HiLoSequenceName, name);

        return propertyBuilder;
    }

    /// <summary>
    /// Configures an integer property to use Hi/Lo value generation backed by a sequence.
    /// </summary>
    /// <typeparam name="TProperty">The property CLR type.</typeparam>
    /// <param name="propertyBuilder">The property builder.</param>
    /// <param name="name">The sequence name.</param>
    /// <param name="schema">The sequence schema (unused on MySQL).</param>
    /// <returns>The same <see cref="PropertyBuilder{TProperty}"/> instance.</returns>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "ApiDesign",
        "RS0026:Do not add multiple overloads with optional parameters",
        Justification = "EF Core community standard: HiLo sequence name and schema are optional and default to convention. See ADR D-008.")]
    public static PropertyBuilder<TProperty> UseHiLo<TProperty>(
        this PropertyBuilder<TProperty> propertyBuilder,
        string? name = null,
        string? schema = null
    )
    {
        ArgumentNullException.ThrowIfNull(propertyBuilder);

        ((PropertyBuilder)propertyBuilder).UseHiLo(name, schema);

        return propertyBuilder;
    }

    /// <summary>
    /// Configures the provider-level GUID storage format for a property.
    /// </summary>
    /// <param name="propertyBuilder">The property builder.</param>
    /// <param name="format">The GUID storage format.</param>
    /// <returns>The same <see cref="PropertyBuilder"/> instance.</returns>
    public static PropertyBuilder HasMySqlGuidFormat(
        this PropertyBuilder propertyBuilder,
        MySqlGuidFormat format
    )
    {
        ArgumentNullException.ThrowIfNull(propertyBuilder);

        if (!Enum.IsDefined(format))
        {
            throw new ArgumentOutOfRangeException(nameof(format));
        }

        propertyBuilder.Metadata.SetMySqlGuidFormat(format);

        switch (format)
        {
            case MySqlGuidFormat.Binary16:
                propertyBuilder.IsFixedLength();
                propertyBuilder.HasMaxLength(16);
                propertyBuilder.HasColumnType("binary(16)");
                propertyBuilder.Metadata.SetProviderClrType(null);
                propertyBuilder.Metadata.SetValueConverter((ValueConverter?)null);
                break;
            case MySqlGuidFormat.Char36:
                propertyBuilder.IsFixedLength();
                propertyBuilder.HasMaxLength(36);
                propertyBuilder.HasColumnType("char(36)");
                propertyBuilder.Metadata.SetProviderClrType(typeof(string));
                propertyBuilder.Metadata.SetValueConverter(new GuidToStringConverter());
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(format));
        }

        return propertyBuilder;
    }

    /// <summary>
    /// Configures the provider-level GUID storage format for a typed property.
    /// </summary>
    /// <typeparam name="TProperty">The property CLR type.</typeparam>
    /// <param name="propertyBuilder">The property builder.</param>
    /// <param name="format">The GUID storage format.</param>
    /// <returns>The same <see cref="PropertyBuilder{TProperty}"/> instance.</returns>
    public static PropertyBuilder<TProperty> HasMySqlGuidFormat<TProperty>(
        this PropertyBuilder<TProperty> propertyBuilder,
        MySqlGuidFormat format
    )
    {
        ArgumentNullException.ThrowIfNull(propertyBuilder);

        ((PropertyBuilder)propertyBuilder).HasMySqlGuidFormat(format);

        return propertyBuilder;
    }

    /// <summary>
    /// Marks a column as <c>INVISIBLE</c> (MariaDB 10.3.3+). Invisible columns are excluded
    /// from <c>SELECT *</c> and <c>INSERT</c> without explicit column lists.
    /// </summary>
    /// <param name="propertyBuilder">The property builder.</param>
    /// <param name="invisible">Whether the column should be invisible.</param>
    /// <returns>The same <see cref="PropertyBuilder"/> instance.</returns>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "ApiDesign",
        "RS0026:Do not add multiple overloads with optional parameters",
        Justification = "MariaDB INVISIBLE fluent API: the toggle defaults to true so the no-argument call site reads naturally. See ADR D-008.")]
    public static PropertyBuilder IsInvisible(
        this PropertyBuilder propertyBuilder,
        bool invisible = true
    )
    {
        ArgumentNullException.ThrowIfNull(propertyBuilder);

        propertyBuilder.Metadata.SetAnnotation(MySqlAnnotationNames.Invisible, invisible);

        return propertyBuilder;
    }

    /// <summary>
    /// Marks a column as <c>INVISIBLE</c> (MariaDB 10.3.3+).
    /// </summary>
    /// <typeparam name="TProperty">The property CLR type.</typeparam>
    /// <param name="propertyBuilder">The property builder.</param>
    /// <param name="invisible">Whether the column should be invisible.</param>
    /// <returns>The same <see cref="PropertyBuilder{TProperty}"/> instance.</returns>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "ApiDesign",
        "RS0026:Do not add multiple overloads with optional parameters",
        Justification = "MariaDB INVISIBLE fluent API: the toggle defaults to true so the no-argument call site reads naturally. See ADR D-008.")]
    public static PropertyBuilder<TProperty> IsInvisible<TProperty>(
        this PropertyBuilder<TProperty> propertyBuilder,
        bool invisible = true
    )
    {
        ArgumentNullException.ThrowIfNull(propertyBuilder);

        ((PropertyBuilder)propertyBuilder).IsInvisible(invisible);

        return propertyBuilder;
    }
}
