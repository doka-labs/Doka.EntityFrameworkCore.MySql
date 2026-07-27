using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Provides <see cref="ValueComparer"/> instances for JSON CLR types used in MySQL column mappings.
/// The comparers use pooled canonical UTF-8 buffers so EF Core's change tracker detects mutations
/// inside JSON documents without allocating an intermediate string copy per comparison.
///
/// Hot-path mechanics:
/// <list type="bullet">
/// <item>Equals writes both inputs directly as canonical UTF-8 and compares the resulting bytes,
/// avoiding the
/// <see cref="JsonNode.ToJsonString"/> + <see cref="JsonNode.Parse(string,JsonNodeOptions?,JsonDocumentOptions)"/>
/// round-trip the previous implementation paid per comparison.</item>
/// <item>GetHashCode writes the canonical UTF-8 form into a pooled buffer via
/// <see cref="Utf8JsonWriter"/> and folds it through XxHash64 so the per-hash allocation
/// collapses to one rented buffer instead of one .NET string per call.</item>
/// <item>Snapshot uses <see cref="JsonNode.DeepClone"/> for the node-shaped CLR types so the
/// cloned tree carries the same metadata without an intermediate text round-trip.</item>
/// </list>
///
/// AOT suppressions are scoped per-method (not per-type) so the trimmer's audit window stays
/// limited to the call sites that genuinely touch <see cref="JsonNode"/> / <see cref="JsonDocument"/>
/// surfaces.
/// </summary>
internal static class MySqlJsonValueComparers
{
    private const int InitialHashBufferSize = 1024;

    /// <summary>
    /// A <see cref="ValueComparer{T}"/> for <see cref="JsonElement"/> that compares via streaming
    /// token walk and hashes via XxHash64 over the canonical UTF-8 representation.
    /// </summary>
    public static ValueComparer<JsonElement> JsonElementComparer { get; } = new(
        (
            a,
            b
        ) => JsonElementEquals(a, b),
        v => HashJsonElement(v),
        v => v.Clone());

    /// <summary>
    /// A <see cref="ValueComparer{T}"/> for <see cref="JsonDocument"/> that compares via streaming
    /// token walk and hashes via XxHash64 over the document's root element.
    /// </summary>
    public static ValueComparer<JsonDocument?> JsonDocumentComparer { get; } = new(
        (
            a,
            b
        ) => JsonDocumentEquals(a, b),
        v => v != null ? HashJsonElement(v.RootElement) : 0,
        v => CloneJsonDocument(v));

    /// <summary>
    /// A <see cref="ValueComparer{T}"/> for <see cref="JsonNode"/> that compares via streaming
    /// token walk and hashes via XxHash64 over the node's serialized form. The backing field
    /// carries the trim / AOT suppressions because the analyzer flags the field's initializer
    /// chain (which reaches <c>JsonNode.ReplaceWith&lt;T&gt;</c> inside the BCL JsonValue
    /// constructor path) at the field declaration site, not at the property accessor; the
    /// per-method suppressions further down the chain do not propagate up to silence the
    /// field-level warning.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(
        "Trimming",
        "IL2026",
        Justification = "Delegates to CreateJsonNodeComparer which uses well-known JSON types that are preserved.")]
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(
        "AOT",
        "IL3050",
        Justification = "CreateJsonNodeComparer's JsonNode write / clone paths do not trigger runtime code generation for the JSON primitives this comparer handles.")]
    private static readonly ValueComparer<JsonNode?> s_jsonNodeComparer = CreateJsonNodeComparer();

    public static ValueComparer<JsonNode?> JsonNodeComparer
    {
        [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(
            "Trimming",
            "IL2026",
            Justification = "Returns the s_jsonNodeComparer backing field whose initializer is suppression-covered at the field declaration.")]
        [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(
            "AOT",
            "IL3050",
            Justification = "Returns the s_jsonNodeComparer backing field whose initializer is suppression-covered at the field declaration.")]
        get => s_jsonNodeComparer;
    }

    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(
        "Trimming",
        "IL2026",
        Justification = "JsonNode.WriteTo and JsonNode.DeepClone use well-known JSON types that the trimmer preserves.")]
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(
        "AOT",
        "IL3050",
        Justification = "JsonNode write / clone paths do not trigger runtime code generation for the JSON primitives this comparer handles.")]
    private static ValueComparer<JsonNode?> CreateJsonNodeComparer()
    {
        return new ValueComparer<JsonNode?>(
            (
                a,
                b
            ) => JsonNodeEquals(a, b),
            v => v != null ? HashJsonNode(v) : 0,
            v => CloneJsonNode(v));
    }

    private static bool JsonElementEquals(
        JsonElement a,
        JsonElement b
    )
    {
        using var bufferA = new PooledByteBufferStream(ArrayPool<byte>.Shared, InitialHashBufferSize);
        using var bufferB = new PooledByteBufferStream(ArrayPool<byte>.Shared, InitialHashBufferSize);

        WriteCanonicalJson(a, bufferA);
        WriteCanonicalJson(b, bufferB);

        return bufferA.WrittenLength == bufferB.WrittenLength && bufferA.WrittenSpan.SequenceEqual(bufferB.WrittenSpan);
    }

    private static bool JsonDocumentEquals(
        JsonDocument? a,
        JsonDocument? b
    )
    {
        if (ReferenceEquals(a, b))
        {
            return true;
        }

        if (a is null
            || b is null)
        {
            return false;
        }

        return JsonElementEquals(a.RootElement, b.RootElement);
    }

    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(
        "Trimming",
        "IL2026",
        Justification = "JsonNode.WriteTo uses well-known JSON types that the trimmer preserves.")]
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(
        "AOT",
        "IL3050",
        Justification = "JsonNode.WriteTo does not trigger runtime code generation for the JSON primitives this comparer handles.")]
    private static bool JsonNodeEquals(
        JsonNode? a,
        JsonNode? b
    )
    {
        if (ReferenceEquals(a, b))
        {
            return true;
        }

        if (a is null
            || b is null)
        {
            return false;
        }

        using var bufferA = new PooledByteBufferStream(ArrayPool<byte>.Shared, InitialHashBufferSize);
        using var bufferB = new PooledByteBufferStream(ArrayPool<byte>.Shared, InitialHashBufferSize);

        WriteCanonicalJson(a, bufferA);
        WriteCanonicalJson(b, bufferB);

        return bufferA.WrittenLength == bufferB.WrittenLength && bufferA.WrittenSpan.SequenceEqual(bufferB.WrittenSpan);
    }

    private static int HashJsonElement(
        JsonElement element
    )
    {
        using var buffer = new PooledByteBufferStream(ArrayPool<byte>.Shared, InitialHashBufferSize);
        WriteCanonicalJson(element, buffer);
        var hash = XxHash64.HashToUInt64(buffer.WrittenSpan);

        return unchecked((int)hash) ^ unchecked((int)(hash >> 32));
    }

    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(
        "Trimming",
        "IL2026",
        Justification = "JsonNode.WriteTo uses well-known JSON types that the trimmer preserves.")]
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(
        "AOT",
        "IL3050",
        Justification = "JsonNode.WriteTo does not trigger runtime code generation for the JSON primitives this comparer handles.")]
    private static int HashJsonNode(
        JsonNode node
    )
    {
        using var buffer = new PooledByteBufferStream(ArrayPool<byte>.Shared, InitialHashBufferSize);
        WriteCanonicalJson(node, buffer);
        var hash = XxHash64.HashToUInt64(buffer.WrittenSpan);

        return unchecked((int)hash) ^ unchecked((int)(hash >> 32));
    }

    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(
        "Trimming",
        "IL2026",
        Justification = "JsonNode.DeepClone uses well-known JSON types that the trimmer preserves.")]
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(
        "AOT",
        "IL3050",
        Justification = "JsonNode.DeepClone does not trigger runtime code generation for the JSON primitives this comparer handles.")]
    private static JsonNode? CloneJsonNode(
        JsonNode? source
    ) => source?.DeepClone();

    private static JsonDocument? CloneJsonDocument(
        JsonDocument? source
    )
    {
        if (source is null)
        {
            return null;
        }

        using var buffer = new PooledByteBufferStream(ArrayPool<byte>.Shared, InitialHashBufferSize);
        WriteCanonicalJson(source.RootElement, buffer);

        return JsonDocument.Parse(buffer.WrittenMemory);
    }

    private static void WriteCanonicalJson(
        JsonElement element,
        PooledByteBufferStream buffer
    )
    {
        using var writer = new Utf8JsonWriter(buffer);
        element.WriteTo(writer);
    }

    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(
        "Trimming",
        "IL2026",
        Justification = "JsonNode.WriteTo uses well-known JSON types that the trimmer preserves.")]
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(
        "AOT",
        "IL3050",
        Justification = "JsonNode.WriteTo does not trigger runtime code generation for the JSON primitives this comparer handles.")]
    private static void WriteCanonicalJson(
        JsonNode node,
        PooledByteBufferStream buffer
    )
    {
        using var writer = new Utf8JsonWriter(buffer);
        node.WriteTo(writer);
    }

    /// <summary>
    /// Backing stream with exclusive ownership of one rented byte array. Growth
    /// returns the previous array only after the replacement is installed, and
    /// disposal returns the current array exactly once even when JSON writing fails.
    /// </summary>
    internal sealed class PooledByteBufferStream : Stream
    {
        private readonly ArrayPool<byte> _pool;
        private byte[]? _buffer;

        public PooledByteBufferStream(
            ArrayPool<byte> pool,
            int initialCapacity
        )
        {
            ArgumentNullException.ThrowIfNull(pool);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(initialCapacity);

            _pool = pool;
            _buffer = pool.Rent(initialCapacity);
        }

        public int WrittenLength { get; private set; }

        internal ReadOnlySpan<byte> WrittenSpan => Buffer.AsSpan(0, WrittenLength);

        internal ReadOnlyMemory<byte> WrittenMemory => Buffer.AsMemory(0, WrittenLength);

        private byte[] Buffer => _buffer ?? throw new ObjectDisposedException(nameof(PooledByteBufferStream));

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => WrittenLength;

        public override long Position
        {
            get => WrittenLength;
            set => throw new NotSupportedException();
        }

        public override void Flush() { }

        public override int Read(
            byte[] buffer,
            int offset,
            int count
        ) => throw new NotSupportedException();

        public override long Seek(
            long offset,
            SeekOrigin origin
        ) => throw new NotSupportedException();

        public override void SetLength(
            long value
        ) => throw new NotSupportedException();

        public override void Write(
            byte[] buffer,
            int offset,
            int count
        ) => Write(buffer.AsSpan(offset, count));

        public override void Write(
            ReadOnlySpan<byte> source
        )
        {
            EnsureCapacity(WrittenLength + source.Length);
            source.CopyTo(Buffer.AsSpan(WrittenLength));
            WrittenLength += source.Length;
        }

        public override void WriteByte(
            byte value
        )
        {
            EnsureCapacity(WrittenLength + 1);
            Buffer[WrittenLength++] = value;
        }

        private void EnsureCapacity(
            int requiredLength
        )
        {
            if (requiredLength <= Buffer.Length)
            {
                return;
            }

            var newCapacity = Math.Max(Buffer.Length * 2, requiredLength);
            var oldBuffer = Buffer;
            var newBuffer = _pool.Rent(newCapacity);
            Buffer
                .AsSpan(0, WrittenLength)
                .CopyTo(newBuffer);
            _buffer = newBuffer;
            _pool.Return(oldBuffer);
        }

        protected override void Dispose(
            bool disposing
        )
        {
            if (disposing)
            {
                var buffer = Interlocked.Exchange(ref _buffer, null);
                if (buffer is not null)
                {
                    _pool.Return(buffer);
                }
            }

            base.Dispose(disposing);
        }
    }
}
