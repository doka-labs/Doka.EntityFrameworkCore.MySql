namespace Doka.EntityFrameworkCore.MySql.Benchmarks;

/// <summary>
/// A measurement that did not converge well enough to produce a verdict.
/// </summary>
/// <remarks>
/// This is deliberately distinct from every other failure the driver can hit.
/// A workload whose calibration would not settle says something about the
/// machine it ran on, not about the provider, and the exit code below is what
/// keeps the attempt path from recording it as a regression. Exit 1 -- an
/// ordinary unhandled exception -- classifies as `regression`, which is a
/// verdict about the code and is not retryable, so a busy runner could convict
/// a provider it never measured.
/// </remarks>
internal sealed class MeasurementQualityException : Exception
{
    /// <summary>
    /// The exit code the attempt path reads as `measurement-inconclusive`.
    /// </summary>
    /// <remarks>
    /// Registered in <c>eng/performance/contract.py</c> as
    /// <c>MEASUREMENT_QUALITY_EXIT_CODE</c>; a contract test holds the two
    /// definitions together.
    /// </remarks>
    public const int ExitCode = 75;

    public MeasurementQualityException(string message)
        : base(message)
    {
    }

    public MeasurementQualityException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
