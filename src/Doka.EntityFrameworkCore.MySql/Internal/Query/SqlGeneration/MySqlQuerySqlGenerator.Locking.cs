namespace Doka.EntityFrameworkCore.MySql;

internal sealed partial class MySqlQuerySqlGenerator
{
    /// <summary>Copies opt-in execution contracts into EF's reusable command template.</summary>
    public override IRelationalCommand GetCommand(
        Expression queryExpression
    )
    {
        // WHY: A generator can be reused after failed generation. Statement-local names and
        // policies must never be inherited by another query or selected from diagnostic tags.
        _materializedSourceNames = null;
        _recursiveCteShapes = null;
        _recursiveQueryPolicy = null;
        _recursiveWriteDepthParameters = default;
        _recursiveTextCapacityBytes = 0;
        _windowParameterConstraintsBuilder = null;

        try
        {
            var command = base.GetCommand(queryExpression);
            var mode = queryExpression is SelectExpression select
                ? MySqlLockingReadMetadata.GetMode(select)
                : MySqlLockingReadMode.None;

            var windows = _windowParameterConstraintsBuilder?.Build();
            var directWrite = queryExpression is MySqlInsertSelectExpression;

            if (command is MySqlRelationalCommand providerCommand)
            {
                providerCommand.LockingReadMode = mode;
                providerCommand.RecursiveQueryPolicy = _recursiveQueryPolicy;
                providerCommand.RecursiveWriteDepthParameters = _recursiveWriteDepthParameters;
                providerCommand.WindowParameterConstraints = windows;
                providerCommand.IsDirectWrite = directWrite;
            }
            else if (mode != MySqlLockingReadMode.None
                || _recursiveQueryPolicy is not null
                || windows is not null
                || directWrite)
            {
                throw new InvalidOperationException("The selected query requires the Doka relational command factory.");
            }

            return command;
        }
        finally
        {
            _materializedSourceNames = null;
            _recursiveCteShapes = null;
            _recursiveQueryPolicy = null;
            _recursiveWriteDepthParameters = default;
            _recursiveTextCapacityBytes = 0;
            _windowParameterConstraintsBuilder = null;
        }
    }

    /// <summary>Appends a clause only after the complete validated root block, never after a derived alias.</summary>
    private void AppendLockingReadClause(
        Expression queryExpression
    )
    {
        if (queryExpression is not SelectExpression select)
        {
            return;
        }

        var mode = MySqlLockingReadMetadata.GetMode(select);

        if (mode == MySqlLockingReadMode.None)
        {
            return;
        }

        var engine = Profile.Engine;
        Sql.AppendLine();
        Sql.Append(mode switch
        {
            MySqlLockingReadMode.Update => "FOR UPDATE",
            MySqlLockingReadMode.Share when engine.Has(EngineCapability.SharedLockingReadUsesLockInShareMode)
                => "LOCK IN SHARE MODE",
            MySqlLockingReadMode.Share when engine.Has(EngineCapability.ForShareClause) => "FOR SHARE",
            MySqlLockingReadMode.NoWait when engine.Has(EngineCapability.ForUpdateNoWaitClause)
                => "FOR UPDATE NOWAIT",
            MySqlLockingReadMode.SkipLocked when engine.Has(EngineCapability.ForUpdateSkipLockedClause)
                => "FOR UPDATE SKIP LOCKED",
            _ => throw new InvalidOperationException(
                "The locking read mode is not supported by the configured engine capabilities."),
        });
    }
}
