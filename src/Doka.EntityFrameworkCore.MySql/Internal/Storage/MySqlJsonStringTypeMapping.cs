namespace Doka.EntityFrameworkCore.MySql;

internal sealed class MySqlJsonStringTypeMapping : JsonTypeMapping
{
    public MySqlJsonStringTypeMapping(
        string storeType
    ) : base(storeType, typeof(string), System.Data.DbType.String) { }

    private MySqlJsonStringTypeMapping(
        RelationalTypeMappingParameters parameters
    ) : base(parameters) { }

    protected override RelationalTypeMapping Clone(
        RelationalTypeMappingParameters parameters
    ) => new MySqlJsonStringTypeMapping(parameters);
}
