namespace Doka.EntityFrameworkCore.MySql;

internal interface IMySqlTransientExceptionDetector
{
    bool IsCancellation(
        Exception exception
    );

    bool IsCommandTimeout(
        Exception exception
    );

    bool ShouldRetryOn(
        Exception exception
    );
}
