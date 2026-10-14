// WHY: Each class owns its database, but the selected servers are shared;
// bound concurrent classes so an IDE run cannot overload those servers.
[assembly: CollectionBehavior(CollectionBehavior.CollectionPerClass, MaxParallelThreads = 4)]
[assembly: TestFramework(
    "Doka.EntityFrameworkCore.MySql.IntegrationTests.IntegrationTestFramework",
    "Doka.EntityFrameworkCore.MySql.IntegrationTests")]
