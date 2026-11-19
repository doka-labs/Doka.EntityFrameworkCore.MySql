namespace Doka.EntityFrameworkCore.MySql;

/// <summary>Records typed column assignments for a server-side INSERT SELECT.</summary>
/// <typeparam name="TSource">The SQL source row type.</typeparam>
/// <typeparam name="TTarget">The mapped target entity type.</typeparam>
/// <remarks>
/// Assignments are expression trees. The operation neither constructs target entities nor
/// invokes source-value expressions in memory. Use <see cref="EF.Property{TProperty}" />
/// in a target selector for mapped shadow or field-only properties.
/// </remarks>
public sealed class MySqlInsertSettersBuilder<TSource, TTarget>
    where TTarget : class
{
    private readonly IEvaluatableExpressionFilter _evaluatableFilter;
    private readonly List<MySqlInsertSetter> _setters = [];
    private readonly IEntityType _target;

    /// <summary>Creates the invocation-local assignment recorder for the actual mapped target.</summary>
    internal MySqlInsertSettersBuilder(
        IEntityType target,
        IEvaluatableExpressionFilter evaluatableFilter
    )
    {
        _target = target;
        _evaluatableFilter = evaluatableFilter;
    }

    /// <summary>Assigns a writable mapped target property from a SQL-translatable source expression.</summary>
    /// <typeparam name="TProperty">The property value type.</typeparam>
    /// <param name="targetProperty">A mapped property access, including EF.Property for shadow or field-only values.</param>
    /// <param name="sourceValue">The value evaluated for each source row by the database.</param>
    /// <returns>This recorder for the next assignment.</returns>
    /// <exception cref="ArgumentNullException">A selector is null.</exception>
    /// <exception cref="InvalidOperationException">The target selector is unmapped or assigned more than once.</exception>
    public MySqlInsertSettersBuilder<TSource, TTarget> SetProperty<TProperty>(
        Expression<Func<TTarget, TProperty>> targetProperty,
        Expression<Func<TSource, TProperty>> sourceValue
    )
    {
        ArgumentNullException.ThrowIfNull(targetProperty);
        ArgumentNullException.ThrowIfNull(sourceValue);

        var property = MySqlInsertPropertySelector.Resolve(_target, targetProperty);
        var selectedType = Nullable.GetUnderlyingType(typeof(TProperty)) ?? typeof(TProperty);
        var mappedType = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;

        if (selectedType != mappedType)
        {
            throw new InvalidOperationException(
                "An INSERT SELECT target selector cannot change the mapped property type.");
        }

        MySqlInsertValueValidator.Validate(sourceValue, _target.Model, _evaluatableFilter);

        // WHY: Reject a duplicate property immediately while retaining reference-identity comparison.
        // ReSharper disable once ForeachCanBeConvertedToQueryUsingAnotherGetEnumerator
        foreach (var setter in _setters)
        {
            if (ReferenceEquals(setter.Property, property))
            {
                throw new InvalidOperationException(
                    $"INSERT SELECT assigns property '{property.Name}' more than once.");
            }
        }

        _setters.Add(new MySqlInsertSetter(property,
            sourceValue,
            MySqlInsertProjectionMarker.Methods<TProperty>.Value));

        return this;
    }

    /// <summary>Freezes the selected assignment order before the callback can mutate its recorder again.</summary>
    internal MySqlInsertSetter[] Build() => _setters.Count != 0
        // WHY: Return an explicit setter snapshot after the assignment list is complete.
        // ReSharper disable once UseCollectionExpression
        ? _setters.ToArray()
        : throw new InvalidOperationException("INSERT SELECT requires at least one target assignment.");
}

/// <summary>Pairs actual mapped property identity with its unevaluated source-value expression.</summary>
internal readonly record struct MySqlInsertSetter(
    IProperty Property,
    LambdaExpression ValueSelector,
    MethodInfo ProjectionMethod
);
