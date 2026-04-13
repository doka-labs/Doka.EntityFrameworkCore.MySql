namespace Doka.EntityFrameworkCore.MySql;

internal sealed class MySqlNetTopologySuiteOptionsExtensionInfo : DbContextOptionsExtensionInfo
{
    public MySqlNetTopologySuiteOptionsExtensionInfo(
        MySqlNetTopologySuiteOptionsExtension extension
    ) : base(extension) { }

    public override bool IsDatabaseProvider => false;

    public override string LogFragment => "using NetTopologySuite ";

    public override int GetServiceProviderHashCode() => HashCode.Combine(typeof(MySqlNetTopologySuiteOptionsExtension));

    public override void PopulateDebugInfo(
        IDictionary<string, string> debugInfo
    )
    {
        ArgumentNullException.ThrowIfNull(debugInfo);

        debugInfo["DokaMySql:NetTopologySuite"] = "1";
    }

    public override bool ShouldUseSameServiceProvider(
        DbContextOptionsExtensionInfo other
    ) => other is MySqlNetTopologySuiteOptionsExtensionInfo;
}
