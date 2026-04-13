namespace Doka.EntityFrameworkCore.MySql.Benchmarks;

public static class Program
{
    public static int Main(
        string[] args
    )
    {
        BenchmarkSwitcher
            .FromAssembly(typeof(Program).Assembly)
            .Run(args, BenchmarkConfiguration.Create());

        return 0;
    }
}
