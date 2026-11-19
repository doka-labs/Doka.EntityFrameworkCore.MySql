namespace Doka.EntityFrameworkCore.MySql.IntegrationTests;

public sealed partial class MySqlCteGraphIntegrationTests
{
    /// <summary>Represents a vertex independently from its incoming paths, allowing real diamonds and cycles.</summary>
    private class GraphVertex
    {
        private string _note = string.Empty;

        /// <summary>Gets or sets the stable physical key.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets the tenant discriminator used by the context filter.</summary>
        public int TenantId { get; set; }

        /// <summary>Gets or sets the separately filtered navigation key.</summary>
        public int TargetId { get; set; }

        /// <summary>Gets or sets the bounded Unicode payload.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Gets or sets a required navigation whose own filter must survive binding.</summary>
        public GraphTarget Target { get; set; } = null!;
    }

    /// <summary>Supplies a real discriminator and derived-property mapping.</summary>
    private sealed class SpecialVertex : GraphVertex
    {
        /// <summary>Gets or sets the derived payload.</summary>
        public string Tag { get; set; } = string.Empty;
    }

    /// <summary>Stores path occurrences separately, including repeated edges when deliberately seeded.</summary>
    private sealed class GraphEdge
    {
        /// <summary>Gets or sets the edge identity.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets the previous-iteration vertex.</summary>
        public int ParentId { get; set; }

        /// <summary>Gets or sets the next vertex.</summary>
        public int ChildId { get; set; }

        /// <summary>Gets or sets the independently filtered edge tenant.</summary>
        public int TenantId { get; set; }
    }

    /// <summary>Provides visibility filtering independent from a vertex's tenant.</summary>
    private sealed class GraphTarget
    {
        /// <summary>Gets or sets the physical key.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets the navigation filter discriminator.</summary>
        public bool IsVisible { get; set; }

        /// <summary>Gets or sets the projected target value.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Gets related vertices when a collection rejoin is explicitly requested.</summary>
        public ICollection<GraphVertex> Vertices { get; set; } = new List<GraphVertex>();
    }

    /// <summary>Represents an application value whose store representation requires its real converter.</summary>
    /// <param name="Value">The converted text.</param>
    private readonly record struct GraphCode(string Value);

    /// <summary>Supplies enum conversion independently of the custom value converter.</summary>
    private enum GraphState
    {
        Ready,
        Done,
    }

    /// <summary>Contains exact native facets for ordinary and recursive readback.</summary>
    private sealed class GraphFacet
    {
        /// <summary>Gets or sets the physical identity.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets the binary GUID.</summary>
        public Guid BinaryId { get; set; }

        /// <summary>Gets or sets the character GUID.</summary>
        public Guid CharId { get; set; }

        /// <summary>Gets or sets native MariaDB UUID, or the explicitly qualified MySQL character counterpart.</summary>
        public Guid NativeId { get; set; }

        /// <summary>Gets or sets a genuinely nullable GUID.</summary>
        public Guid? OptionalId { get; set; }

        /// <summary>Gets or sets the nullable exact decimal.</summary>
        public decimal? Amount { get; set; }

        /// <summary>Gets or sets the fractional temporal value.</summary>
        public DateTime Moment { get; set; }

        /// <summary>Gets or sets the nullable scalar.</summary>
        public int? OptionalNumber { get; set; }

        /// <summary>Gets or sets a string-converted enum.</summary>
        public GraphState State { get; set; }

        /// <summary>Gets or sets a custom converted application value.</summary>
        public GraphCode Code { get; set; }

        /// <summary>Gets or sets a Unicode comparison payload.</summary>
        public string Label { get; set; } = string.Empty;
    }

    /// <summary>Provides the same VARCHAR declaration in a genuinely different physical charset.</summary>
    private sealed class GraphLatinRow
    {
        /// <summary>Gets or sets the physical key.</summary>
        public int Id { get; set; }

        /// <summary>Gets or sets the native latin1 payload.</summary>
        public string Value { get; set; } = string.Empty;
    }
}
