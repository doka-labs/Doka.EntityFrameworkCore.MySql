using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Doka.EntityFrameworkCore.MySql.Tests;

public sealed partial class MySqlStraightJoinTests
{
    /// <summary>Records the actual compiled command without opening a connection or substituting SQL translation.</summary>
    private static ServiceProvider CreateRecordingServices(List<string> commands)
    {
        var services = new ServiceCollection();
        services.AddEntityFrameworkDokaMySql();
        services.Replace(ServiceDescriptor.Singleton<IQuerySqlGeneratorFactory>(provider => new RecordingFactory(
            provider.GetRequiredService<QuerySqlGeneratorDependencies>(), provider.GetServices<ISingletonOptions>(), commands)));

        return services.BuildServiceProvider();
    }

    /// <summary>Decorates the real Doka generator factory only at its public command-generation boundary.</summary>
    private sealed class RecordingFactory(QuerySqlGeneratorDependencies dependencies,
        IEnumerable<ISingletonOptions> options, List<string> commands) : IQuerySqlGeneratorFactory
    {
        private readonly MySqlQuerySqlGeneratorFactory _inner = new(dependencies, options);

        /// <inheritdoc />
        public QuerySqlGenerator Create() => new RecordingGenerator(dependencies, _inner.Create(), commands);
    }

    /// <summary>Delegates the complete SQL generation to Doka and retains the exact resulting command text.</summary>
    private sealed class RecordingGenerator(QuerySqlGeneratorDependencies dependencies, QuerySqlGenerator inner,
        List<string> commands) : QuerySqlGenerator(dependencies)
    {
        /// <inheritdoc />
        public override IRelationalCommand GetCommand(Expression queryExpression)
        {
            var command = inner.GetCommand(queryExpression);
            commands.Add(command.CommandText);

            return command;
        }
    }
}
