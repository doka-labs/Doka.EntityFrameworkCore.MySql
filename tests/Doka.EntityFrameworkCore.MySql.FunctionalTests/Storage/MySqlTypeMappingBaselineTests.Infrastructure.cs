using System.Data;
using System.Linq.Expressions;

namespace Doka.EntityFrameworkCore.MySql.FunctionalTests;

/// <summary>Contains the infrastructure responsibility for MySqlTypeMappingBaselineTests.</summary>
public sealed partial class MySqlTypeMappingBaselineTests
{
    private static DbContextOptions<TContext> CreateOptions<TContext>()
        where TContext : DbContext
    {
        var builder = MySqlFunctionalTestOptions.CreateTransientBuilder<TContext>();

        builder.UseMySql(
            "Server=localhost;Database=doka;User ID=root;Password=password;",
            MySqlServerVersion.MySql(new Version(8, 4, 0)));

        return builder.Options;
    }

    private static DbContextOptionsBuilder<TemporalPrecisionContext> CreateTemporalPrecisionOptions(
        ConnectionOpeningProbe connectionProbe
    )
    {
        var builder = MySqlFunctionalTestOptions.CreateTransientBuilder<TemporalPrecisionContext>();
        builder
            .AddInterceptors(connectionProbe)
            .EnableServiceProviderCaching(false)
            .UseMySql(
                "Server=localhost;Database=doka;User ID=root;Password=password;",
                MySqlServerVersion.MySql(new Version(8, 4, 0)));

        return builder;
    }

    private static char ReadCharProviderValue(
        RelationalTypeMapping mapping,
        object value
    )
    {
        var table = new DataTable();
        table.Columns.Add("Value", value.GetType());
        table.Rows.Add(value);

        using var reader = table.CreateDataReader();

        Assert.True(reader.Read());

        var readerExpression = Expression.Parameter(typeof(DbDataReader), "reader");
        var getCharExpression = Expression.Call(
            readerExpression,
            RelationalTypeMapping.GetDataReaderMethod(typeof(char)),
            Expression.Constant(0));

        var customizedExpression = mapping.CustomizeDataReaderExpression(getCharExpression);
        var materialize = Expression
            .Lambda<Func<DbDataReader, char>>(customizedExpression, readerExpression)
            .Compile();

        return materialize(reader);
    }
}
