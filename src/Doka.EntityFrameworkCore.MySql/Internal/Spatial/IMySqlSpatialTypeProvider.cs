namespace Doka.EntityFrameworkCore.MySql;

internal interface IMySqlSpatialTypeProvider
{
    bool TryResolveClrType(
        string? storeTypeName,
        out Type? clrType
    );
}
