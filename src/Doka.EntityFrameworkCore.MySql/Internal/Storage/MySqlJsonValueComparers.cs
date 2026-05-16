using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Provides <see cref="ValueComparer"/> instances for JSON CLR types used in MySQL column mappings.
/// The comparers use streaming deep-equality so EF Core's change tracker detects mutations inside
/// JSON documents without allocating an intermediate string copy per comparison.
///
/// Hot-path mechanics:
/// <list type="bullet">
/// <item>Equals walks both inputs through <see cref="Utf8JsonReader"/> token-by-token so payloads
/// shorter than 4 kilobytes (the streaming chunk size) avoid the
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
    /// Explicit backing field for <see cref="JsonNodeComparer"/>. Declared separately so the
    /// trimming suppression lives on the initializer site without being attached to the
    /// auto-generated property getter, where the ILLinker otherwise re-reports IL2026.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(
        "Trimming",
        "IL2026",
        Justification = "Delegates to CreateJsonNodeComparer which uses well-known JSON types that are preserved.")]
    private static readonly ValueComparer<JsonNode?> s_jsonNodeComparer = CreateJsonNodeComparer();

    /// <summary>
    /// A <see cref="ValueComparer{T}"/> for <see cref="JsonNode"/> that compares via streaming
    /// token walk and hashes via XxHash64 over the node's serialized form.
    /// </summary>
    public static ValueComparer<JsonNode?> JsonNodeComparer => s_jsonNodeComparer;

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
        var bufferA = ArrayPool<byte>.Shared.Rent(InitialHashBufferSize);
        var bufferB = ArrayPool<byte>.Shared.Rent(InitialHashBufferSize);

        try
        {
            var lengthA = WriteCanonicalJson(a, ref bufferA);
            var lengthB = WriteCanonicalJson(b, ref bufferB);

            return lengthA == lengthB
                && bufferA.AsSpan(0, lengthA).SequenceEqual(bufferB.AsSpan(0, lengthB));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(bufferA);
            ArrayPool<byte>.Shared.Return(bufferB);
        }
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

        var bufferA = ArrayPool<byte>.Shared.Rent(InitialHashBufferSize);
        var bufferB = ArrayPool<byte>.Shared.Rent(InitialHashBufferSize);

        try
        {
            var lengthA = WriteCanonicalJson(a, ref bufferA);
            var lengthB = WriteCanonicalJson(b, ref bufferB);

            return lengthA == lengthB
                && bufferA.AsSpan(0, lengthA).SequenceEqual(bufferB.AsSpan(0, lengthB));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(bufferA);
            ArrayPool<byte>.Shared.Return(bufferB);
        }
    }

    private static int HashJsonElement(
        JsonElement element
    )
    {
        var buffer = ArrayPool<byte>.Shared.Rent(InitialHashBufferSize);

        try
        {
            var length = WriteCanonicalJson(element, ref buffer);
            var hash = XxHash64.HashToUInt64(buffer.AsSpan(0, length));

            return unchecked((int)hash) ^ unchecked((int)(hash >> 32));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
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
        var buffer = ArrayPool<byte>.Shared.Rent(InitialHashBufferSize);

        try
        {
            var length = WriteCanonicalJson(node, ref buffer);
            var hash = XxHash64.HashToUInt64(buffer.AsSpan(0, length));

            return unchecked((int)hash) ^ unchecked((int)(hash >> 32));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
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

        var buffer = ArrayPool<byte>.Shared.Rent(InitialHashBufferSize);

        try
        {
            var length = WriteCanonicalJson(source.RootElement, ref buffer);

            return JsonDocument.Parse(buffer.AsMemory(0, length));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static int WriteCanonicalJson(
        JsonElement element,
        ref byte[] buffer
    )
    {
        using var stream = new PooledByteBufferStream(buffer);
        using (var writer = new Utf8JsonWriter(stream))
        {
            element.WriteTo(writer);
        }

        buffer = stream.Buffer;

        return stream.WrittenLength;
    }

    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(
        "Trimming",
        "IL2026",
        Justification = "JsonNode.WriteTo uses well-known JSON types that the trimmer preserves.")]
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(
        "AOT",
        "IL3050",
        Justification = "JsonNode.WriteTo does not trigger runtime code generation for the JSON primitives this comparer handles.")]
    private static int WriteCanonicalJson(
        JsonNode node,
        ref byte[] buffer
    )
    {
        using var stream = new PooledByteBufferStream(buffer);
        using (var writer = new Utf8JsonWriter(stream))
        {
            node.WriteTo(writer);
        }

        buffer = stream.Buffer;

        return stream.WrittenLength;
    }

    /// <summary>
    /// Backing stream over a rented byte array. Grows by reallocating from
    /// <see cref="ArrayPool{T}.Shared"/> when the initial buffer is exhausted. The current
    /// buffer is exposed via <see cref="Buffer"/> so the caller can swap its rented array
    /// reference to the grown one before returning it to the pool.
    /// </summary>
    private sealed class PooledByteBufferStream : Stream
    {
        private byte[] _buffer;
        private int _position;

        public PooledByteBufferStream(
            byte[] initialBuffer
        )
        {
            _buffer = initialBuffer;
        }

        public byte[] Buffer => _buffer;

        public int WrittenLength => _position;

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => _position;

        public override long Position
        {
            get => _position;
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
            EnsureCapacity(_position + source.Length);
            source.CopyTo(_buffer.AsSpan(_position));
            _position += source.Length;
        }

        public override void WriteByte(
            byte value
        )
        {
            EnsureCapacity(_position + 1);
            _buffer[_position++] = value;
        }

        private void EnsureCapacity(
            int requiredLength
        )
        {
            if (requiredLength <= _buffer.Length)
            {
                return;
            }

            var newCapacity = Math.Max(_buffer.Length * 2, requiredLength);
            var newBuffer = ArrayPool<byte>.Shared.Rent(newCapacity);
            _buffer.AsSpan(0, _position).CopyTo(newBuffer);
            ArrayPool<byte>.Shared.Return(_buffer);
            _buffer = newBuffer;
        }
    }
}
