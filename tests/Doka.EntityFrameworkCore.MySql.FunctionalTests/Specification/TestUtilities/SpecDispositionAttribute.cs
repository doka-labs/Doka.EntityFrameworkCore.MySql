using Xunit.v3;

namespace Doka.EntityFrameworkCore.MySql.FunctionalTests.Specification.TestUtilities;

/// <summary>
/// Provides the xUnit v3 runtime-skip mechanism for specification dispositions without
/// introducing a second fact or theory attribute on an inherited test method.
/// </summary>
public abstract class SpecDispositionAttribute : BeforeAfterTestAttribute
{
    /// <summary>
    /// Gets or sets the reason reported when the active target is covered by the disposition.
    /// </summary>
    protected string? SkipReason { get; set; }

    /// <inheritdoc />
    public override void Before(
        MethodInfo methodUnderTest,
        IXunitTest test
    )
    {
        if (SkipReason is not null)
        {
            Assert.Skip(SkipReason);
        }
    }
}
