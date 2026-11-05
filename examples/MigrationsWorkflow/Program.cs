using Doka.EntityFrameworkCore.MySql.Examples.MigrationsWorkflow;

// WHY: The standalone command-line entry point has no caller cancellation token.
return await MigrationWorkflowCommand
    .RunAsync(args, CancellationToken.None)
    .ConfigureAwait(false);
