namespace Doka.EntityFrameworkCore.MySql;

internal interface IMySqlSpatialTypeProvider
{
    Type GeometryType { get; }

    bool TryResolveClrType(
        string? storeTypeName,
        out Type? clrType
    );
}
