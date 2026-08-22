namespace Doka.EntityFrameworkCore.MySql.Benchmarks;

/// <summary>
/// Measures the dominant no-truncation parameter path with all values prepared
/// outside the measured operation.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(launchCount: 1, warmupCount: 3, iterationCount: 5)]
public class ParameterSizeInterceptorBenchmark : IDisposable
{
    private readonly MySqlParameterSizeCommandInterceptor _interceptor = new();
    private readonly MySqlCommand _withinSizeCommand = CreateCommand(size: 128);
    private readonly MySqlCommand _unsizedCommand = CreateCommand(size: 0);

    [Benchmark]
    public int Process1000WithinSizeParameters() => Process(_withinSizeCommand);

    [Benchmark]
    public int Process1000UnsizedParameters() => Process(_unsizedCommand);

    [Benchmark(Baseline = true)]
    public int Process1000WithinSizeParametersWithValueWriteback()
    {
        foreach (MySqlParameter parameter in _withinSizeCommand.Parameters)
        {
            parameter.Value = parameter.Value;
        }

        return _withinSizeCommand.Parameters.Count;
    }

    public void Dispose()
    {
        _withinSizeCommand.Dispose();
        _unsizedCommand.Dispose();
        GC.SuppressFinalize(this);
    }

    private int Process(
        MySqlCommand command
    )
    {
        _interceptor.CommandInitialized(eventData: null!, command);

        return command.Parameters.Count;
    }

    private static MySqlCommand CreateCommand(
        int size
    )
    {
        var command = new MySqlCommand();
        for (var index = 0; index < 1000; index++)
        {
            command.Parameters.Add(
                new MySqlParameter($"p{index}", "within-size")
                {
                    Direction = System.Data.ParameterDirection.Input,
                    Size = size,
                });
        }

        return command;
    }
}
