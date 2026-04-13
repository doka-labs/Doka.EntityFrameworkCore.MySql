namespace Doka.EntityFrameworkCore.MySql.RuntimeSmoke;

internal static class CompiledModelAccessor
{
    public static IModel GetBasicModel() => CompiledModels.Basic.BasicSmokeContextModel.Instance;

    public static IModel GetSpatialModel() => CompiledModels.Spatial.SpatialSmokeContextModel.Instance;
}
