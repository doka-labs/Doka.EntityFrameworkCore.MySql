namespace Doka.EntityFrameworkCore.MySql.Tests;

/// <summary>Protects static nullable scaffolding mappings and their pass-through boundaries.</summary>
public sealed class MySqlScaffoldingClrTypeTests
{
    /// <summary>Provides every supported scalar type and its statically referenced nullable counterpart.</summary>
    public static TheoryData<Type, Type> SupportedTypes => new()
    {
        { typeof(bool), typeof(bool?) },
        { typeof(byte), typeof(byte?) },
        { typeof(sbyte), typeof(sbyte?) },
        { typeof(short), typeof(short?) },
        { typeof(ushort), typeof(ushort?) },
        { typeof(int), typeof(int?) },
        { typeof(uint), typeof(uint?) },
        { typeof(long), typeof(long?) },
        { typeof(ulong), typeof(ulong?) },
        { typeof(float), typeof(float?) },
        { typeof(double), typeof(double?) },
        { typeof(decimal), typeof(decimal?) },
        { typeof(Guid), typeof(Guid?) },
        { typeof(DateTime), typeof(DateTime?) },
        { typeof(DateOnly), typeof(DateOnly?) },
        { typeof(TimeOnly), typeof(TimeOnly?) },
    };

    /// <summary>Provides scalar types outside the scaffolding mapping's closed set.</summary>
    public static TheoryData<Type> UnsupportedValueTypes => new()
    {
        typeof(DateTimeOffset),
        typeof(TimeSpan),
        typeof(UnsupportedEnum),
        typeof(UnsupportedValue),
    };

    /// <summary>Includes unsupported value types because required columns never request nullable construction.</summary>
    public static IEnumerable<object[]> RequiredValueTypes()
    {
        foreach (var pair in SupportedTypes)
        {
            yield return [pair.Data.Item1];
        }

        foreach (var unsupported in UnsupportedValueTypes)
        {
            yield return [unsupported.Data];
        }
    }

    /// <summary>Includes already-nullable unsupported types, which must also pass through unchanged.</summary>
    public static IEnumerable<object[]> AlreadyNullableTypes()
    {
        foreach (var pair in SupportedTypes)
        {
            yield return [pair.Data.Item2, false];
            yield return [pair.Data.Item2, true];
        }

        foreach (var type in new[]
        {
            typeof(DateTimeOffset?), typeof(TimeSpan?), typeof(UnsupportedEnum?), typeof(UnsupportedValue?),
        })
        {
            yield return [type, false];
            yield return [type, true];
        }
    }

    /// <summary>Wraps each supported scalar type only when the store column is nullable.</summary>
    /// <param name="clrType">The mapped scalar type.</param>
    /// <param name="expectedType">Its expected nullable counterpart.</param>
    [Theory]
    [MemberData(nameof(SupportedTypes))]
    public void Nullable_scalar_uses_explicit_nullable_mapping(Type clrType, Type expectedType)
    {
        // Arrange: The theory supplies the scalar and expected nullable type.

        // Act
        var actual = MySqlScaffoldingModelFactory.GetPropertyClrType(clrType, isNullable: true);

        // Assert
        Assert.Same(expectedType, actual);
    }

    /// <summary>Required value types do not need or acquire a nullable mapping.</summary>
    /// <param name="clrType">A supported or unsupported unwrapped value type.</param>
    [Theory]
    [MemberData(nameof(RequiredValueTypes))]
    public void Required_value_type_is_unchanged(Type clrType)
    {
        // Arrange: The theory supplies the required value type.

        // Act
        var actual = MySqlScaffoldingModelFactory.GetPropertyClrType(clrType, isNullable: false);

        // Assert
        Assert.Same(clrType, actual);
    }

    /// <summary>Existing nullable wrappers are neither nested nor rejected for unsupported underlying types.</summary>
    /// <param name="clrType">The already-nullable value type.</param>
    /// <param name="isNullable">The requested store-column nullability.</param>
    [Theory]
    [MemberData(nameof(AlreadyNullableTypes))]
    public void Already_nullable_value_type_is_unchanged(Type clrType, bool isNullable)
    {
        // Arrange: The theory supplies an existing wrapper and column nullability.

        // Act
        var actual = MySqlScaffoldingModelFactory.GetPropertyClrType(clrType, isNullable);

        // Assert
        Assert.Same(clrType, actual);
    }

    /// <summary>Reference types keep their CLR identity regardless of column nullability.</summary>
    /// <param name="clrType">The reference type selected by scaffolding.</param>
    /// <param name="isNullable">The requested store-column nullability.</param>
    [Theory]
    [InlineData(typeof(string), false)]
    [InlineData(typeof(string), true)]
    [InlineData(typeof(byte[]), false)]
    [InlineData(typeof(byte[]), true)]
    [InlineData(typeof(object), false)]
    [InlineData(typeof(object), true)]
    public void Reference_type_is_unchanged(Type clrType, bool isNullable)
    {
        // Arrange: The theory supplies the reference type and column nullability.

        // Act
        var actual = MySqlScaffoldingModelFactory.GetPropertyClrType(clrType, isNullable);

        // Assert
        Assert.Same(clrType, actual);
    }

    /// <summary>Unknown value types fail explicitly instead of relying on dynamic generic construction.</summary>
    /// <param name="clrType">An unwrapped type outside the supported scalar set.</param>
    [Theory]
    [MemberData(nameof(UnsupportedValueTypes))]
    public void Unsupported_nullable_value_type_is_rejected(Type clrType)
    {
        // Arrange: The theory supplies the unsupported value type.

        // Act
        var exception = Record.Exception(() => MySqlScaffoldingModelFactory.GetPropertyClrType(clrType, true));

        // Assert
        var failure = Assert.IsType<InvalidOperationException>(exception);

        Assert.Equal($"The scaffolded value type '{clrType}' has no AOT-safe nullable mapping.", failure.Message);
    }

    /// <summary>Represents an enum not produced by store-type resolution.</summary>
    private enum UnsupportedEnum { Value }

    /// <summary>Represents an application value type outside the closed scalar mapping.</summary>
    private readonly record struct UnsupportedValue(int Value);
}
