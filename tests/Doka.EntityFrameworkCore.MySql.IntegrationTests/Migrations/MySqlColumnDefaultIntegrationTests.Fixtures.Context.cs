namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlColumnDefaultIntegrationTests
{
    private sealed class ColumnDefaultContext : DbContext
    {
        /// <summary>
        /// Initializes the ColumnDefaultContext fixture.
        /// </summary>
        public ColumnDefaultContext(
            DbContextOptions<ColumnDefaultContext> options
        ) : base(options) { }
    }
}
