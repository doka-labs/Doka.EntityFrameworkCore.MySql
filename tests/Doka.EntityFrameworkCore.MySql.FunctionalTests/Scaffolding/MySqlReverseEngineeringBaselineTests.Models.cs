namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

/// <summary>Contains the models cases and shared support.</summary>
public sealed partial class MySqlReverseEngineeringBaselineTests
{
    private sealed class StubDatabaseModelFactory : IDatabaseModelFactory
    {
        private readonly DatabaseModel _databaseModel;
        private readonly string _detectedServerVersionText;
        private readonly MySqlScaffoldingContext _scaffoldingContext;

        public StubDatabaseModelFactory(
            DatabaseModel databaseModel,
            string detectedServerVersionText,
            MySqlScaffoldingContext scaffoldingContext
        )
        {
            _databaseModel = databaseModel ?? throw new ArgumentNullException(nameof(databaseModel));
            _detectedServerVersionText = detectedServerVersionText
                ?? throw new ArgumentNullException(nameof(detectedServerVersionText));
            _scaffoldingContext = scaffoldingContext ?? throw new ArgumentNullException(nameof(scaffoldingContext));
        }

        public DatabaseModel Create(
            string connectionString,
            DatabaseModelFactoryOptions options
        )
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
            ArgumentNullException.ThrowIfNull(options);

            _scaffoldingContext.Begin();
            _scaffoldingContext.SetDetectedServerVersionText(_detectedServerVersionText);

            return _databaseModel;
        }

        public DatabaseModel Create(
            DbConnection connection,
            DatabaseModelFactoryOptions options
        )
        {
            ArgumentNullException.ThrowIfNull(connection);
            ArgumentNullException.ThrowIfNull(options);

            _scaffoldingContext.Begin();
            _scaffoldingContext.SetDetectedServerVersionText(_detectedServerVersionText);

            return _databaseModel;
        }
    }
}
