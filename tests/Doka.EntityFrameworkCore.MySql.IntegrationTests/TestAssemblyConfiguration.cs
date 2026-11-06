// WHY: Each class owns its database, but the selected servers are shared;
// bound concurrent classes so an IDE run cannot overload those servers.
[assembly: Xunit.v3.Parallelization(Mode = Xunit.Sdk.ParallelMode.Collections, MaxThreads = 4)]
[assembly: AssemblyFixture(typeof(Doka.EntityFrameworkCore.MySql.IntegrationTests.IntegrationDatabaseFixture))]
