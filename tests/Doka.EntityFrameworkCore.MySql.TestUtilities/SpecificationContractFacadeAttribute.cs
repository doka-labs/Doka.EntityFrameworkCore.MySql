namespace Doka.EntityFrameworkCore.MySql.TestUtilities;

/// <summary>
/// Identifies a test facade that structurally implements an upstream
/// specification contract without inheriting its test methods.
/// </summary>
/// <remarks>
/// The specification contract gate accepts the facade only when it declares
/// every upstream test method with the same signature and an xUnit fact or
/// theory attribute.
/// </remarks>
[AttributeUsage(AttributeTargets.Class)]
public sealed class SpecificationContractFacadeAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of the
    /// <see cref="SpecificationContractFacadeAttribute"/> class.
    /// </summary>
    /// <param name="contractType">The upstream specification contract type.</param>
    public SpecificationContractFacadeAttribute(
        Type contractType
    )
    {
        ContractType = contractType ?? throw new ArgumentNullException(nameof(contractType));
    }

    /// <summary>
    /// Gets the upstream specification contract implemented by the facade.
    /// </summary>
    public Type ContractType { get; }
}
