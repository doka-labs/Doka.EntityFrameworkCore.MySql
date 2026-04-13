using Microsoft.EntityFrameworkCore.Design;
using System.Runtime.CompilerServices;

[assembly: DesignTimeServicesReference(
    "Doka.EntityFrameworkCore.MySql.MySqlNetTopologySuiteDesignTimeServices, Doka.EntityFrameworkCore.MySql.NetTopologySuite",
    "Doka.EntityFrameworkCore.MySql"
)]
[assembly: InternalsVisibleTo("Doka.EntityFrameworkCore.MySql.Tests")]
[assembly: InternalsVisibleTo("Doka.EntityFrameworkCore.MySql.FunctionalTests")]
[assembly: InternalsVisibleTo("Doka.EntityFrameworkCore.MySql.IntegrationTests")]
