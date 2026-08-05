using System.Diagnostics.CodeAnalysis;

namespace Doka.EntityFrameworkCore.MySql.Internal.Metadata;

/// <summary>
/// Contains trimming contracts shared by public generic metadata builders.
/// </summary>
internal static class MySqlTrimmingConstants
{
    /// <summary>
    /// Preserves the CLR members that EF Core may inspect while building entity metadata.
    /// This mirrors the contract on EF Core 10's generic entity builders, whose source
    /// constant is internal and therefore cannot be referenced by a provider.
    /// </summary>
    internal const DynamicallyAccessedMemberTypes EntityType = DynamicallyAccessedMemberTypes.PublicConstructors
        | DynamicallyAccessedMemberTypes.NonPublicConstructors
        | DynamicallyAccessedMemberTypes.PublicProperties
        | DynamicallyAccessedMemberTypes.PublicFields
        | DynamicallyAccessedMemberTypes.NonPublicProperties
        | DynamicallyAccessedMemberTypes.NonPublicFields
        | DynamicallyAccessedMemberTypes.Interfaces;
}
