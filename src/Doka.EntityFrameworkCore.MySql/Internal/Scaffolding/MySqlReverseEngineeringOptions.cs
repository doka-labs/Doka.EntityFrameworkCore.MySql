namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Internal mutable carrier for design-time reverse-engineering toggles. The public
/// <see cref="MySqlReverseEngineeringOptionsBuilder"/> wraps this type and exposes a
/// fluent surface; the split keeps the public API surface (auditable through
/// <c>PublicAPI.Shipped.txt</c>) free of raw setters while the internal carrier stays
/// simple enough for direct mutation inside the design-time service-collection wiring.
/// </summary>
internal sealed class MySqlReverseEngineeringOptions
{
    public bool ScaffoldTextGuidsAsGuids { get; set; }
}
