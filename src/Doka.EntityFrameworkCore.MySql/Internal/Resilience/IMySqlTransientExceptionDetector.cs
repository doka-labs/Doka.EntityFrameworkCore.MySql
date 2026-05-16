namespace Doka.EntityFrameworkCore.MySql;

internal interface IMySqlTransientExceptionDetector
{
    bool IsCommandTimeout(
        Exception exception
    );

    bool ShouldRetryOn(
        Exception exception
    );
}
