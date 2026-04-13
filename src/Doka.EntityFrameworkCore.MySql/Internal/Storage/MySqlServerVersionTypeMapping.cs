namespace Doka.EntityFrameworkCore.MySql;

internal sealed class MySqlServerVersionTypeMapping : RelationalTypeMapping
{
    private static readonly ConstructorInfo s_version2Constructor = typeof(Version).GetConstructor(
    [
        typeof(int),
        typeof(int),
    ])!;

    private static readonly ConstructorInfo s_version3Constructor = typeof(Version).GetConstructor(
    [
        typeof(int),
        typeof(int),
        typeof(int),
    ])!;

    private static readonly ConstructorInfo s_version4Constructor = typeof(Version).GetConstructor(
    [
        typeof(int),
        typeof(int),
        typeof(int),
        typeof(int),
    ])!;

    private static readonly MethodInfo s_mySqlFactoryMethod = typeof(MySqlServerVersion).GetRuntimeMethod(
        nameof(MySqlServerVersion.MySql),
        [typeof(Version)])!;

    private static readonly MethodInfo s_mariaDbFactoryMethod = typeof(MySqlServerVersion).GetRuntimeMethod(
        nameof(MySqlServerVersion.MariaDb),
        [typeof(Version)])!;

    public MySqlServerVersionTypeMapping() : base(
        new RelationalTypeMappingParameters(
            new CoreTypeMappingParameters(typeof(MySqlServerVersion)),
            storeType: "varchar(32)",
            StoreTypePostfix.None,
            dbType: System.Data.DbType.String,
            unicode: false,
            size: null,
            fixedLength: false,
            precision: null,
            scale: null))
    { }

    private MySqlServerVersionTypeMapping(
        RelationalTypeMappingParameters parameters
    ) : base(parameters) { }

    protected override RelationalTypeMapping Clone(
        RelationalTypeMappingParameters parameters
    ) => new MySqlServerVersionTypeMapping(parameters);

    public override Expression GenerateCodeLiteral(
        object value
    )
    {
        if (value is not MySqlServerVersion serverVersion)
        {
            throw new ArgumentException($"Expected a {nameof(MySqlServerVersion)} literal value.", nameof(value));
        }

        var versionLiteral = CreateVersionLiteral(serverVersion.Version);
        var factoryMethod = serverVersion.IsMariaDb ? s_mariaDbFactoryMethod : s_mySqlFactoryMethod;

        return Expression.Call(factoryMethod, versionLiteral);
    }

    private static NewExpression CreateVersionLiteral(
        Version version
    )
    {
        ArgumentNullException.ThrowIfNull(version);

        if (version.Revision >= 0)
        {
            return Expression.New(
                s_version4Constructor,
                Expression.Constant(version.Major),
                Expression.Constant(version.Minor),
                Expression.Constant(version.Build),
                Expression.Constant(version.Revision));
        }

        if (version.Build >= 0)
        {
            return Expression.New(
                s_version3Constructor,
                Expression.Constant(version.Major),
                Expression.Constant(version.Minor),
                Expression.Constant(version.Build));
        }

        return Expression.New(
            s_version2Constructor,
            Expression.Constant(version.Major),
            Expression.Constant(version.Minor));
    }
}
