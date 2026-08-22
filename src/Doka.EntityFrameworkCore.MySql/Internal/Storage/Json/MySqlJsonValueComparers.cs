namespace Doka.EntityFrameworkCore.MySql;

/// <summary>
/// Provides <see cref="ValueComparer"/> instances for JSON CLR types used in MySQL column mappings.
/// The comparers use the .NET JSON DOM's structural equality and allocation-conscious hashing so
/// EF Core's change tracker detects mutations inside JSON documents without a serialization
/// round-trip per comparison.
///
/// Hot-path mechanics:
/// <list type="bullet">
/// <item><see cref="JsonElement"/> equality delegates to <see cref="JsonElement.DeepEquals"/>,
/// which walks the existing DOM instead of allocating writers and intermediate buffers.</item>
/// <item><see cref="JsonElement"/> hashing follows the same structural semantics, including
/// order-independent JSON objects and equivalent numeric representations.</item>
/// <item><see cref="JsonNode"/> equality walks ordinary object and array nodes once while
/// preserving symmetric property-name semantics; customized values fall back to
/// <see cref="JsonNode.DeepEquals"/>.</item>
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
    /// A <see cref="ValueComparer{T}"/> for <see cref="JsonElement"/> that compares and hashes
    /// using structural JSON semantics.
    /// </summary>
    public static ValueComparer<JsonElement> JsonElementComparer { get; } = new(
        (
            a,
            b
        ) => JsonElementEquals(a, b),
        v => HashJsonElement(v),
        v => v.Clone());

    /// <summary>
    /// A <see cref="ValueComparer{T}"/> for <see cref="JsonDocument"/> that compares and hashes
    /// the document's root element using structural JSON semantics.
    /// </summary>
    public static ValueComparer<JsonDocument?> JsonDocumentComparer { get; } = new(
        (
            a,
            b
        ) => JsonDocumentEquals(a, b),
        v => v != null ? HashJsonElement(v.RootElement) : 0,
        v => CloneJsonDocument(v));

    [field: UnconditionalSuppressMessage(
        "Trimming",
        "IL2026",
        Justification = "Delegates to CreateJsonNodeComparer which uses well-known JSON types that are preserved.")]
    [field: System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(
        "AOT",
        "IL3050",
        Justification = "CreateJsonNodeComparer's JsonNode write / clone paths do not trigger runtime code generation for the JSON primitives this comparer handles.")]
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
        get;
    } = CreateJsonNodeComparer();

    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2026",
        Justification = "The rare customized JsonValue fallback and JsonNode.DeepClone use well-known JSON types that the trimmer preserves.")]
    [UnconditionalSuppressMessage(
        "AOT",
        "IL3050",
        Justification = "The rare customized JsonValue fallback and clone path do not trigger runtime code generation for the JSON primitives this comparer handles.")]
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
    ) => JsonElement.DeepEquals(a, b);

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

        return JsonNodeEqualsCore(a, b);
    }

    private static bool JsonNodeEqualsCore(
        JsonNode? left,
        JsonNode? right
    )
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null
            || right is null)
        {
            return false;
        }

        if (left is JsonObject leftObject)
        {
            if (right is JsonObject rightObject)
            {
                return JsonObjectEquals(leftObject, rightObject);
            }

            return right is not JsonArray && JsonNode.DeepEquals(left, right);
        }

        if (left is JsonArray leftArray)
        {
            if (right is JsonArray rightArray)
            {
                return JsonArrayEquals(leftArray, rightArray);
            }

            return right is not JsonObject && JsonNode.DeepEquals(left, right);
        }

        return JsonNode.DeepEquals(left, right);
    }

    private static bool JsonObjectEquals(
        JsonObject left,
        JsonObject right
    )
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var index = 0; index < left.Count; index++)
        {
            var leftProperty = left.GetAt(index);
            if (!right.TryGetPropertyValue(leftProperty.Key, out var rightValue, out var rightIndex))
            {
                return false;
            }

            var rightProperty = right.GetAt(rightIndex);
            if (!HasReversePropertyMatch(left, index, leftProperty.Key, rightProperty.Key)
                || !JsonNodeEqualsCore(leftProperty.Value, rightValue))
            {
                return false;
            }
        }

        return true;
    }

    private static bool HasReversePropertyMatch(
        JsonObject left,
        int leftIndex,
        string leftName,
        string rightName
    )
    {
        if (string.Equals(leftName, rightName, StringComparison.Ordinal))
        {
            return true;
        }

        return left.TryGetPropertyValue(rightName, out _, out var reverseIndex) && reverseIndex == leftIndex;
    }

    private static bool JsonArrayEquals(
        JsonArray left,
        JsonArray right
    )
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var index = 0; index < left.Count; index++)
        {
            if (!JsonNodeEqualsCore(left[index], right[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static int HashJsonElement(
        JsonElement element
    )
    {
        var hash = new HashCode();
        hash.Add(element.ValueKind);

        switch (element.ValueKind)
        {
            case JsonValueKind.Undefined:
            case JsonValueKind.Null:
            case JsonValueKind.False:
            case JsonValueKind.True:
                break;

            case JsonValueKind.Number:
                AddNumberHash(element, ref hash);
                break;

            case JsonValueKind.String:
                hash.Add(element.GetString(), StringComparer.Ordinal);
                break;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    hash.Add(HashJsonElement(item));
                }

                break;

            case JsonValueKind.Object:
                var propertyCount = 0;
                var unorderedPropertiesHash = 0;

                foreach (var property in element.EnumerateObject())
                {
                    var propertyHash = new HashCode();
                    propertyHash.Add(property.Name, StringComparer.Ordinal);
                    propertyHash.Add(HashJsonElement(property.Value));
                    unorderedPropertiesHash = unchecked(unorderedPropertiesHash + propertyHash.ToHashCode());
                    propertyCount++;
                }

                hash.Add(propertyCount);
                hash.Add(unorderedPropertiesHash);
                break;

            default:
                throw new UnreachableException();
        }

        return hash.ToHashCode();
    }

    private static void AddNumberHash(
        JsonElement element,
        ref HashCode hash
    )
    {
        // DeepEquals compares mathematical JSON numbers rather than their lexical
        // spellings. Decimal preserves exact values where possible; double extends
        // the useful hash distribution for the remaining exponent range. Collisions
        // are valid for values outside both representations, while equal values must
        // always reach the same branch and hash.
        if (element.TryGetDecimal(out var decimalValue))
        {
            hash.Add(decimalValue);
        }
        else if (element.TryGetDouble(out var doubleValue))
        {
            hash.Add(doubleValue);
        }
    }

    private static int HashJsonNode(
        JsonNode node
    )
    {
        if (node is JsonObject jsonObject)
        {
            return HashJsonObject(jsonObject);
        }

        if (node is JsonArray jsonArray)
        {
            return HashJsonArray(jsonArray);
        }

        var valueKind = node.GetValueKind();
        return valueKind is JsonValueKind.Object or JsonValueKind.Array
            ? HashCustomizedJsonValue(node)
            : HashJsonScalar(node, valueKind);
    }

    private static int HashNullableJsonNode(
        JsonNode? node
    ) => node is null ? HashJsonScalarKind(JsonValueKind.Null) : HashJsonNode(node);

    private static int HashJsonObject(
        JsonObject jsonObject
    )
    {
        var unorderedPropertiesHash = 0;

        for (var index = 0; index < jsonObject.Count; index++)
        {
            var property = jsonObject.GetAt(index);
            var propertyHash = new HashCode();
            propertyHash.Add(property.Key, StringComparer.OrdinalIgnoreCase);
            propertyHash.Add(HashNullableJsonNode(property.Value));
            unorderedPropertiesHash = unchecked(unorderedPropertiesHash + propertyHash.ToHashCode());
        }

        var hash = new HashCode();
        hash.Add(JsonValueKind.Object);
        hash.Add(jsonObject.Count);
        hash.Add(unorderedPropertiesHash);

        return hash.ToHashCode();
    }

    private static int HashJsonArray(
        JsonArray jsonArray
    )
    {
        var hash = new HashCode();
        hash.Add(JsonValueKind.Array);
        hash.Add(jsonArray.Count);

        for (var index = 0; index < jsonArray.Count; index++)
        {
            hash.Add(HashNullableJsonNode(jsonArray[index]));
        }

        return hash.ToHashCode();
    }

    private static int HashJsonScalar(
        JsonNode node,
        JsonValueKind valueKind
    )
    {
        var hash = new HashCode();
        hash.Add(valueKind);
        if (valueKind == JsonValueKind.Number)
        {
            if (!TryGetJsonNumberAsDouble(node, out var number))
            {
                return HashCustomizedJsonValue(node);
            }

            // A double is deliberately the common denominator for every built-in numeric
            // representation. It preserves the equal-value hash contract across parsed
            // JSON, integral CLR types, floating-point CLR types, and decimal without
            // allocating a canonical numeric string.
            hash.Add(number);
        }

        return hash.ToHashCode();
    }

    private static int HashJsonScalarKind(
        JsonValueKind valueKind
    )
    {
        var hash = new HashCode();
        hash.Add(valueKind);

        return hash.ToHashCode();
    }

    private static bool TryGetJsonNumberAsDouble(
        JsonNode node,
        out double number
    )
    {
        if (node is not JsonValue value)
        {
            number = 0;
            return false;
        }

        if (value.TryGetValue<JsonElement>(out var element))
        {
            return element.TryGetDouble(out number);
        }

        if (value.TryGetValue<double>(out number))
        {
            return true;
        }

        if (value.TryGetValue<decimal>(out var decimalValue))
        {
            number = (double)decimalValue;
            return true;
        }

        if (value.TryGetValue<float>(out var floatValue))
        {
            number = floatValue;
            return true;
        }

        if (value.TryGetValue<long>(out var longValue))
        {
            number = longValue;
            return true;
        }

        if (value.TryGetValue<ulong>(out var unsignedLongValue))
        {
            number = unsignedLongValue;
            return true;
        }

        if (value.TryGetValue<int>(out var integerValue))
        {
            number = integerValue;
            return true;
        }

        if (value.TryGetValue<uint>(out var unsignedIntegerValue))
        {
            number = unsignedIntegerValue;
            return true;
        }

        if (value.TryGetValue<short>(out var shortValue))
        {
            number = shortValue;
            return true;
        }

        if (value.TryGetValue<ushort>(out var unsignedShortValue))
        {
            number = unsignedShortValue;
            return true;
        }

        if (value.TryGetValue<sbyte>(out var signedByteValue))
        {
            number = signedByteValue;
            return true;
        }

        if (value.TryGetValue<byte>(out var byteValue))
        {
            number = byteValue;
            return true;
        }

        if (value.TryGetValue<Half>(out var halfValue))
        {
            number = (double)halfValue;
            return true;
        }

        if (value.TryGetValue<Int128>(out var signedInt128Value))
        {
            number = (double)signedInt128Value;
            return true;
        }

        if (value.TryGetValue<UInt128>(out var unsignedInt128Value))
        {
            number = (double)unsignedInt128Value;
            return true;
        }

        number = 0;
        return false;
    }

    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2026",
        Justification = "JsonNode.WriteTo uses the customized JsonValue's configured JSON metadata.")]
    [UnconditionalSuppressMessage(
        "AOT",
        "IL3050",
        Justification = "JsonNode.WriteTo uses metadata already carried by the customized JsonValue.")]
    private static int HashCustomizedJsonValue(
        JsonNode node
    )
    {
        using var buffer = new PooledByteBufferStream(ArrayPool<byte>.Shared, InitialHashBufferSize);
        WriteCanonicalJson(node, buffer);
        using var document = JsonDocument.Parse(buffer.WrittenMemory);

        return HashJsonElementShape(document.RootElement);
    }

    private static int HashJsonElementShape(
        JsonElement element
    )
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var count = 0;
            var unorderedPropertiesHash = 0;

            foreach (var property in element.EnumerateObject())
            {
                var propertyHash = new HashCode();
                propertyHash.Add(property.Name, StringComparer.OrdinalIgnoreCase);
                propertyHash.Add(HashJsonElementShape(property.Value));
                unorderedPropertiesHash = unchecked(unorderedPropertiesHash + propertyHash.ToHashCode());
                count++;
            }

            var objectHash = new HashCode();
            objectHash.Add(JsonValueKind.Object);
            objectHash.Add(count);
            objectHash.Add(unorderedPropertiesHash);
            return objectHash.ToHashCode();
        }

        if (element.ValueKind == JsonValueKind.Array)
        {
            var arrayHash = new HashCode();
            arrayHash.Add(JsonValueKind.Array);
            arrayHash.Add(element.GetArrayLength());

            foreach (var item in element.EnumerateArray())
            {
                arrayHash.Add(HashJsonElementShape(item));
            }

            return arrayHash.ToHashCode();
        }

        if (element.ValueKind == JsonValueKind.Number
            && element.TryGetDouble(out var number))
        {
            var numberHash = new HashCode();
            numberHash.Add(JsonValueKind.Number);
            numberHash.Add(number);
            return numberHash.ToHashCode();
        }

        return HashJsonScalarKind(element.ValueKind);
    }

    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2026",
        Justification = "JsonNode.DeepClone uses well-known JSON types that the trimmer preserves.")]
    [UnconditionalSuppressMessage(
        "AOT",
        "IL3050",
        Justification = "JsonNode.DeepClone does not trigger runtime code generation for the JSON primitives this comparer handles.")]
    private static JsonNode? CloneJsonNode(
        JsonNode? source
    ) => source?.DeepClone();

    private static JsonDocument? CloneJsonDocument(
        JsonDocument? source
    ) => CloneJsonDocument(source, ArrayPool<byte>.Shared);

    internal static JsonDocument? CloneJsonDocument(
        JsonDocument? source,
        ArrayPool<byte> pool
    )
    {
        ArgumentNullException.ThrowIfNull(pool);

        if (source is null)
        {
            return null;
        }

        using var buffer = new PooledByteBufferStream(pool, InitialHashBufferSize);
        WriteCanonicalJson(source.RootElement, buffer);
        var reader = new Utf8JsonReader(buffer.WrittenSpan);

        return JsonDocument.ParseValue(ref reader);
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
