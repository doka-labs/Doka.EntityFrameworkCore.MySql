using Microsoft.EntityFrameworkCore.Migrations.Design;

namespace Doka.EntityFrameworkCore.MySql;

internal sealed class MySqlCSharpSnapshotGenerator : CSharpSnapshotGenerator
{
    public MySqlCSharpSnapshotGenerator(
        CSharpSnapshotGeneratorDependencies dependencies
    ) : base(dependencies) { }

    protected override void GenerateProperty(
        string entityTypeBuilderName,
        IProperty property,
        CSharpSnapshotGeneratorParameters parameters
    )
    {
        if (!RequiresModelClrType(property))
        {
            base.GenerateProperty(entityTypeBuilderName, property, parameters);
            return;
        }

        // Let EF Core own its complete property-generation algorithm and replace
        // only the provider CLR type selected from the converter. Copying the
        // upstream generator would make every new fluent call an update hazard.
        var generatedBuilder = new IndentedStringBuilder();
        base.GenerateProperty(
            entityTypeBuilderName,
            property,
            parameters with { StringBuilder = generatedBuilder });

        var code = Dependencies.CSharpHelper;
        var propertyCall = property.IsPrimitiveCollection ? "PrimitiveCollection" : "Property";
        var providerClrType = MakeNullable(
            property.GetTypeMapping().Converter?.ProviderClrType ?? property.ClrType,
            property.IsNullable);

        var modelClrType = MakeNullable(property.ClrType, property.IsNullable);
        var propertyName = code.Literal(property.Name);
        var providerDeclaration =
            $".{propertyCall}<{code.Reference(providerClrType)}>({propertyName})";

        var generated = generatedBuilder.ToString();
        if (!generated.Contains(providerDeclaration, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"EF Core did not emit the expected provider-CLR declaration '{providerDeclaration}' for property '{property.Name}'.");
        }

        var requiredCall = !property.IsNullable
            && !modelClrType.IsValueType
            && providerClrType.IsValueType
                ? ".IsRequired()"
                : string.Empty;

        var modelDeclaration =
            $".{propertyCall}<{code.Reference(modelClrType, fullName: true)}>({propertyName}){requiredCall}";

        parameters.StringBuilder.AppendLines(
            generated.Replace(providerDeclaration, modelDeclaration, StringComparison.Ordinal));
    }

    protected override void GenerateData(
        string entityTypeBuilderName,
        IEnumerable<IProperty> properties,
        IEnumerable<IDictionary<string, object?>> data,
        IndentedStringBuilder stringBuilder
    )
    {
        // EF Core supplies provider-shaped seed data. Doka-owned mappings retain
        // model CLR properties in generated models, so their seeds must match
        // those model-side types.
        var propertyList = properties.ToArray();
        var converters = propertyList
            .Where(RequiresModelSeedValue)
            .Select(property => (
                PropertyName: property.Name,
                ModelClrType: Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType,
                TypeMapping: property.GetTypeMapping() as IMySqlProviderOwnedModelTypeMapping,
                Converter: property.GetTypeMapping().Converter))
            .ToArray();

        base.GenerateData(
            entityTypeBuilderName,
            propertyList,
            converters.Length == 0 ? data : ConvertProviderSeedValues(data, converters),
            stringBuilder);
    }

    private static IEnumerable<IDictionary<string, object?>> ConvertProviderSeedValues(
        IEnumerable<IDictionary<string, object?>> data,
        IReadOnlyList<(
            string PropertyName,
            Type ModelClrType,
            IMySqlProviderOwnedModelTypeMapping? TypeMapping,
            ValueConverter? Converter)> converters
    )
    {
        foreach (var seedValues in data)
        {
            Dictionary<string, object?>? convertedSeedValues = null;

            foreach (var (propertyName, modelClrType, typeMapping, converter) in converters)
            {
                if (!seedValues.TryGetValue(propertyName, out var providerValue)
                    || providerValue is null
                    || modelClrType.IsInstanceOfType(providerValue))
                {
                    continue;
                }

                convertedSeedValues ??= new Dictionary<string, object?>(seedValues, StringComparer.Ordinal);
                convertedSeedValues[propertyName] = typeMapping is not null
                    ? typeMapping.ConvertToModelValue(providerValue)
                    : converter!.ConvertFromProvider(providerValue);
            }

            yield return convertedSeedValues ?? seedValues;
        }
    }

    private static bool RequiresModelSeedValue(
        IProperty property
    )
    {
        var modelClrType = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;

        if (property.GetTypeMapping() is IMySqlProviderOwnedModelTypeMapping)
        {
            return true;
        }

        return modelClrType == typeof(Guid)
            && property.GetMySqlGuidFormat() is not null
            && property.GetTypeMapping().Converter is not null;
    }

    private static Type MakeNullable(
        Type clrType,
        bool nullable
    ) => nullable
        ? clrType == typeof(Guid)
            ? typeof(Guid?)
            : clrType == typeof(JsonElement)
                ? typeof(JsonElement?)
                : clrType
        : clrType;

    private static bool RequiresModelClrType(
        IProperty property
    )
    {
        var modelClrType = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;

        if (modelClrType == typeof(Guid)
            && property.GetMySqlGuidFormat() is not null)
        {
            return true;
        }

        var typeMapping = property.GetRelationalTypeMapping();

        return typeMapping is IMySqlProviderOwnedModelTypeMapping providerOwnedMapping
            && providerOwnedMapping.ProviderClrType != property.ClrType;
    }

}
