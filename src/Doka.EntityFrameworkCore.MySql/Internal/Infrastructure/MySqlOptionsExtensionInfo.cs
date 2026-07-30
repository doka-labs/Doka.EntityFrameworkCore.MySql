namespace Doka.EntityFrameworkCore.MySql;

public sealed partial class MySqlOptionsExtension
{
    private sealed class MySqlOptionsExtensionInfo : RelationalExtensionInfo
    {
        private readonly MySqlOptionsExtension _extension;

        public MySqlOptionsExtensionInfo(
            MySqlOptionsExtension extension
        ) : base(extension)
        {
            _extension = extension ?? throw new ArgumentNullException(nameof(extension));
        }

        public override string LogFragment =>
            base.LogFragment
            + (_extension.ServerVersion is null
                ? "using Doka MySql "
                : FormattableString.Invariant($"using Doka MySql ({_extension.ServerVersion}) "));

        public override int GetServiceProviderHashCode() => HashCode.Combine(
            _extension.ServerVersion,
            _extension.RetryOptions,
            _extension.DefaultGuidFormat,
            _extension.Connection is not null,
            _extension.DataSource is not null);

        public override void PopulateDebugInfo(
            IDictionary<string, string> debugInfo
        )
        {
            ArgumentNullException.ThrowIfNull(debugInfo);

            debugInfo["DokaMySql"] = GetServiceProviderHashCode()
                .ToString(CultureInfo.InvariantCulture);
        }

        public override bool ShouldUseSameServiceProvider(
            DbContextOptionsExtensionInfo other
        ) => other is MySqlOptionsExtensionInfo otherInfo
            && Equals(_extension.ServerVersion, otherInfo._extension.ServerVersion)
            && Equals(_extension.RetryOptions, otherInfo._extension.RetryOptions)
            && _extension.DefaultGuidFormat == otherInfo._extension.DefaultGuidFormat
            && (_extension.Connection is not null) == (otherInfo._extension.Connection is not null)
            && (_extension.DataSource is not null) == (otherInfo._extension.DataSource is not null);
    }
}
