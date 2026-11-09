// WHY: Each class owns its database, but the selected servers are shared;
// bound concurrent classes so an IDE run cannot overload those servers.

using Doka.EntityFrameworkCore.MySql.IntegrationTests;
using Xunit.Sdk;
using Xunit.v3;

[assembly: Parallelization(Mode = ParallelMode.Collections, MaxThreads = 4)]
[assembly: AssemblyFixture(typeof(IntegrationDatabaseFixture))]
