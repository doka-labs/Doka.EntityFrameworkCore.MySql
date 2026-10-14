using System.Reflection;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

/// <summary>
/// Starts the shared database servers once per selected test run while leaving
/// independent test classes in separate xUnit collections.
/// </summary>
public sealed class IntegrationTestFramework : XunitTestFramework
{
    /// <summary>Initializes the integration test framework.</summary>
    /// <param name="messageSink">The runner diagnostic sink.</param>
    public IntegrationTestFramework(
        IMessageSink messageSink
    ) : base(messageSink) { }

    /// <inheritdoc />
    protected override ITestFrameworkExecutor CreateExecutor(
        AssemblyName assemblyName
    ) => new IntegrationTestFrameworkExecutor(
        assemblyName,
        SourceInformationProvider,
        DiagnosticMessageSink);
}

internal sealed class IntegrationTestFrameworkExecutor : XunitTestFrameworkExecutor
{
    public IntegrationTestFrameworkExecutor(
        AssemblyName assemblyName,
        ISourceInformationProvider sourceInformationProvider,
        IMessageSink diagnosticMessageSink
    ) : base(assemblyName, sourceInformationProvider, diagnosticMessageSink) { }

    protected override void RunTestCases(
        IEnumerable<IXunitTestCase> testCases,
        IMessageSink executionMessageSink,
        ITestFrameworkExecutionOptions executionOptions
    )
    {
        var selectedCases = testCases.ToArray();
        using var runner = new IntegrationTestAssemblyRunner(
            TestAssembly,
            selectedCases,
            DiagnosticMessageSink,
            executionMessageSink,
            executionOptions);

        // WHY: xUnit v2 exposes a synchronous executor hook, but the assembly
        // runner must await server startup and cleanup around parallel classes.
        runner.RunAsync().GetAwaiter().GetResult();
    }
}

internal sealed class IntegrationTestAssemblyRunner : XunitTestAssemblyRunner
{
    private readonly bool _requiresDatabase;
    private IntegrationDatabaseFixture? _databaseFixture;

    public IntegrationTestAssemblyRunner(
        ITestAssembly testAssembly,
        IReadOnlyCollection<IXunitTestCase> testCases,
        IMessageSink diagnosticMessageSink,
        IMessageSink executionMessageSink,
        ITestFrameworkExecutionOptions executionOptions
    ) : base(testAssembly, testCases, diagnosticMessageSink, executionMessageSink, executionOptions)
    {
        var attributeName = typeof(IntegrationDatabaseTestAttribute).AssemblyQualifiedName!;
        _requiresDatabase = testCases.Any(testCase => testCase.TestMethod.TestClass.Class
            .GetCustomAttributes(attributeName)
            .Any());
    }

    protected override async Task AfterTestAssemblyStartingAsync()
    {
        await base.AfterTestAssemblyStartingAsync().ConfigureAwait(false);

        if (!_requiresDatabase)
        {
            return;
        }

        // WHY: An assembly-owned session can outlive concurrently running class
        // collections; a collection fixture would serialize all of those classes.
        var fixture = new IntegrationDatabaseFixture();
        await fixture.InitializeAsync().ConfigureAwait(false);
        _databaseFixture = fixture;
    }

    protected override async Task BeforeTestAssemblyFinishedAsync()
    {
        try
        {
            if (_databaseFixture is not null)
            {
                await _databaseFixture.DisposeAsync().ConfigureAwait(false);
                _databaseFixture = null;
            }
        }
        finally
        {
            await base.BeforeTestAssemblyFinishedAsync().ConfigureAwait(false);
        }
    }
}
