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
        IndentedStringBuilder stringBuilder
    )
    {
        if (!RequiresModelClrType(property))
        {
            base.GenerateProperty(entityTypeBuilderName, property, stringBuilder);
            return;
        }

        // EF Core snapshots declare converted properties through the converter's
        // provider CLR type. Provider-owned fluent metadata or type mapping restores
        // these mappings, so their declarations must retain the model CLR type.
        // Application-owned converters continue through the base implementation.
        // This branch mirrors EF Core 10.0.11
        // CSharpSnapshotGenerator.GenerateProperty except for that CLR-type choice.
        // Diff it against upstream whenever the supported EF Core range changes.
        var clrType = MakeNullable(property.ClrType, property.IsNullable);
        var propertyCall = property.IsPrimitiveCollection ? "PrimitiveCollection" : "Property";
        var code = Dependencies.CSharpHelper;
        var propertyBuilderName =
            $"{entityTypeBuilderName}.{propertyCall}<{code.Reference(clrType, fullName: true)}>({code.Literal(property.Name)})";

        stringBuilder
            .AppendLine()
            .Append(propertyBuilderName);
        stringBuilder.IncrementIndent();

        var isInComplexCollection = property.DeclaringType is IComplexType { ComplexProperty.IsCollection: true };

        if (!isInComplexCollection
            && property.IsConcurrencyToken)
        {
            stringBuilder
                .AppendLine()
                .Append(".IsConcurrencyToken()");
        }

        if (property.IsNullable != (IsNullableType(clrType) && !property.IsPrimaryKey()))
        {
            stringBuilder
                .AppendLine()
                .Append(".IsRequired()");
        }

        if (!isInComplexCollection
            && property.ValueGenerated != ValueGenerated.Never)
        {
            stringBuilder
                .AppendLine()
                .Append(
                    property.ValueGenerated == ValueGenerated.OnAdd ? ".ValueGeneratedOnAdd()" :
                    property.ValueGenerated == ValueGenerated.OnUpdate ? ".ValueGeneratedOnUpdate()" :
                    property.ValueGenerated == ValueGenerated.OnUpdateSometimes ? ".ValueGeneratedOnUpdateSometimes()" :
                    ".ValueGeneratedOnAddOrUpdate()");
        }

        GeneratePropertyAnnotations(propertyBuilderName, property, stringBuilder);
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

        return typeMapping is MySqlJsonTypeMapping or MySqlRowVersionTypeMapping
            && typeMapping.Converter?.ProviderClrType != property.ClrType;
    }

    private static bool IsNullableType(
        Type clrType
    ) => !clrType.IsValueType || Nullable.GetUnderlyingType(clrType) is not null;
}
