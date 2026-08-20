namespace Doka.EntityFrameworkCore.MySql;

internal sealed class MySqlMigrationSessionCleanupException : InvalidOperationException
{
    public MySqlMigrationSessionCleanupException(
        Exception cleanupException
    ) : base(
        "Migration session-state cleanup failed after the operation completed. "
        + "Its DDL outcome may already be committed, so automatic retry is disabled.",
        cleanupException) { }

    public MySqlMigrationSessionCleanupException(
        Exception primaryException,
        Exception cleanupException
    ) : base(
        "The migration operation and its session-state cleanup both failed. "
        + "Automatic retry is disabled because the DDL outcome may be ambiguous.",
        new AggregateException(primaryException, cleanupException)) { }
}
