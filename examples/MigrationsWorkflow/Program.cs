using Doka.EntityFrameworkCore.MySql.Examples.MigrationsWorkflow;

return await MigrationWorkflowCommand
    .RunAsync(args)
    .ConfigureAwait(false);
