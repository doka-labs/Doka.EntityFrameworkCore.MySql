namespace Doka.EntityFrameworkCore.MySql.Tests;

public sealed partial class MySqlStraightJoinTests
{
    /// <summary>Records the actual compiled command without opening a connection or substituting SQL translation.</summary>
    private static ServiceProvider CreateRecordingServices(
        List<string> commands
    )
    {
        var services = new ServiceCollection();
        services.AddEntityFrameworkDokaMySql();
        services.Replace(
            ServiceDescriptor.Singleton<IQuerySqlGeneratorFactory>(provider => new RecordingFactory(
                provider.GetRequiredService<QuerySqlGeneratorDependencies>(),
                provider.GetServices<ISingletonOptions>(),
                commands)));

        return services.BuildServiceProvider();
    }

    /// <summary>Decorates the real Doka generator factory only at its public command-generation boundary.</summary>
    private sealed class RecordingFactory : IQuerySqlGeneratorFactory
    {
        private readonly List<string> _commands;
        private readonly QuerySqlGeneratorDependencies _dependencies;

        private readonly MySqlQuerySqlGeneratorFactory _inner;

        /// <summary>Initializes the factory with the dependencies and observations shared by this test.</summary>
        public RecordingFactory(
            QuerySqlGeneratorDependencies dependencies,
            IEnumerable<ISingletonOptions> options,
            List<string> commands
        )
        {
            _dependencies = dependencies;
            _commands = commands;
            _inner = new MySqlQuerySqlGeneratorFactory(dependencies, options);
        }

        /// <inheritdoc />
        public QuerySqlGenerator Create() => new RecordingGenerator(_dependencies, _inner.Create(), _commands);
    }

    /// <summary>Delegates the complete SQL generation to Doka and retains the exact resulting command text.</summary>
    private sealed class RecordingGenerator : QuerySqlGenerator
    {
        private readonly List<string> _commands;
        private readonly QuerySqlGenerator _inner;

        /// <summary>Initializes the test helper with its supplied dependencies and values.</summary>
        public RecordingGenerator(
            QuerySqlGeneratorDependencies dependencies,
            QuerySqlGenerator inner,
            List<string> commands
        ) : base(dependencies)
        {
            _inner = inner;
            _commands = commands;
        }

        /// <inheritdoc />
        public override IRelationalCommand GetCommand(
            Expression queryExpression
        )
        {
            var command = _inner.GetCommand(queryExpression);
            _commands.Add(command.CommandText);

            return command;
        }
    }
}
