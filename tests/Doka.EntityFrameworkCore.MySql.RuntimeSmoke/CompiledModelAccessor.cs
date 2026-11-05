using Doka.EntityFrameworkCore.MySql.RuntimeSmoke.CompiledModels.Basic;
using Doka.EntityFrameworkCore.MySql.RuntimeSmoke.CompiledModels.Spatial;

namespace Doka.EntityFrameworkCore.MySql.RuntimeSmoke;

internal static class CompiledModelAccessor
{
    public static IModel GetBasicModel() => BasicSmokeContextModel.Instance;

    public static IModel GetSpatialModel() => SpatialSmokeContextModel.Instance;
}
