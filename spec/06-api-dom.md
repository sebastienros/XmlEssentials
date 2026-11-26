# DOM

Part of the [XmlEssentials spec](README.md).

## XmlValueKind

```csharp
namespace CloudNimble.Text.Xml;

public enum XmlValueKind : byte
{
    Undefined = 0,
    Element = 2,
    EmptyElement = 3,
    Attribute = 4,
    Text = 5,
    CData = 6,
    Comment = 7,
    ProcessingInstruction = 8,
    XmlDeclaration = 9,
    Nil = 10,
}
```

## DOM — immutable (JsonDocument / JsonElement)

Name collision with `System.Xml.XmlDocument` / `XmlElement` is accepted.

**Pooled** means the parse buffer is rented from `ArrayPool<byte>` (STJ `JsonDocument`). `Dispose` returns the rental. Use after dispose throws `ObjectDisposedException`. There is **no** document-instance cache.

```csharp
namespace CloudNimble.Text.Xml;

public readonly struct XmlDeclarationInfo
{
    public string Version { get; }    // "1.0"
    public string Encoding { get; }   // "utf-8"
    public bool? Standalone { get; }  // null = omitted
}

public struct XmlDocumentOptions
{
    public XmlCommentHandling CommentHandling { get; set; }         // default Allow, the zero value: a parsed document keeps its comments so that WriteTo round-trips them. Set Skip to leave them out.
    public XmlWhitespaceHandling WhitespaceHandling { get; set; }   // default Significant, the zero value: indentation between elements is kept so that WriteTo does not change the layout. Set Insignificant to leave it out.
    public int MaxDepth { get; set; }
    public int MaxAttributeCount { get; set; }
    public int MaxNameByteCount { get; set; }
    public int MaxCommentByteCount { get; set; }
    public int MaxTextByteCount { get; set; }
    public int MaxDocumentByteCount { get; set; }
}

public sealed class XmlDocument : IDisposable
{
    public static XmlDocument Parse(string xml, XmlDocumentOptions options = default);
    public static XmlDocument Parse(ReadOnlySpan<char> xml, XmlDocumentOptions options = default);
    public static XmlDocument Parse(ReadOnlySpan<byte> xml, XmlDocumentOptions options = default);
    public static XmlDocument Parse(ReadOnlySequence<byte> xml, XmlDocumentOptions options = default);
    public static XmlDocument Parse(Stream xml, XmlDocumentOptions options = default);
    public static Task<XmlDocument> ParseAsync(Stream xml, XmlDocumentOptions options = default, CancellationToken cancellationToken = default);
    public static XmlDocument ParseValue(ref Utf8XmlReader reader);
    public static bool TryParseValue(ref Utf8XmlReader reader, [NotNullWhen(true)] out XmlDocument? document);

    // The namespaces a document declares, by prefix ("" is the default namespace), in the order each prefix is
    // first declared. Every start tag is read for prefixes; a prefix bound twice keeps its first namespace. A
    // default namespace is listed only when the document element declares it; xmlns="" is not listed. No XmlDocument is built. Fails as Parse fails. The result fits XmlSerializerOptions.Namespaces.
    public static OrderedDictionary<string, string> DiscoverNamespaces(ReadOnlySpan<byte> xml, XmlDocumentOptions options = default);
    public static OrderedDictionary<string, string> DiscoverNamespaces(string xml, XmlDocumentOptions options = default);
    public static OrderedDictionary<string, string> DiscoverNamespaces(Stream xml, XmlDocumentOptions options = default);

    public static bool TryParse(ReadOnlySpan<byte> xml, XmlDocumentOptions options, out XmlDocument? document);

    public XmlElement RootElement { get; }
    /// <summary>Immutable view. Not <see cref="Nodes.XmlDeclarationNode"/> (mutable tree only).</summary>
    public XmlDeclarationInfo? Declaration { get; }
    public void WriteTo(Utf8XmlWriter writer);
    public void Dispose();
}

public readonly struct XmlElement : IEquatable<XmlElement>
{
    public XmlValueKind ValueKind { get; }
    public XmlTokenType TokenType { get; }
    public string LocalName { get; }
    public string NamespaceUri { get; }
    public string Prefix { get; }
    public bool IsEmptyElement { get; }
    public bool IsNil { get; }
    public int AttributeCount { get; }

    public XmlElement GetChild(string localName);
    public XmlElement GetChild(string localName, string namespaceUri);
    public bool TryGetChild(string localName, out XmlElement value);
    // GetChild, TryGetChild, GetAttribute and TryGetAttribute also take the local name as ReadOnlySpan<char>
    // and as ReadOnlySpan<byte> of UTF-8, in any namespace, as JsonElement.GetProperty and TryGetProperty do.
    public bool TryGetChild(string localName, string namespaceUri, out XmlElement value);

    public XmlElement GetAttribute(string localName);
    public XmlElement GetAttribute(string localName, string namespaceUri);
    public bool TryGetAttribute(string localName, out XmlElement value);

    public string GetRawXml();
    public string GetInnerText();
    public string? GetString();
    public bool GetBoolean();
    public int GetInt32();
    public long GetInt64();
    public double GetDouble();
    public decimal GetDecimal();
    public DateTime GetDateTime();
    public DateTimeOffset GetDateTimeOffset();
    public Guid GetGuid();
    public byte[] GetBytesFromBase64();
    public byte GetByte();
    public short GetInt16();
    public float GetSingle();
    [CLSCompliant(false)] public sbyte GetSByte();
    [CLSCompliant(false)] public ushort GetUInt16();
    [CLSCompliant(false)] public uint GetUInt32();
    [CLSCompliant(false)] public ulong GetUInt64();

    // One for each getter of JsonElement: Byte, BytesFromBase64, DateTime, DateTimeOffset, Decimal, Double, Guid,
    // Int16, Int32, Int64, SByte, Single, UInt16, UInt32, UInt64.
    public bool TryGetInt32(out int value);

    public Enumerator EnumerateChildren();
    public AttributeEnumerator EnumerateAttributes();
    public void WriteTo(Utf8XmlWriter writer);
    public XmlNode CloneNode();

    public struct Enumerator
    {
        public XmlElement Current { get; }
        public bool MoveNext();
        public Enumerator GetEnumerator();
    }

    public struct AttributeEnumerator
    {
        public XmlElement Current { get; }
        public bool MoveNext();
        public AttributeEnumerator GetEnumerator();
    }
}
```

`XmlElement` is a **struct cursor** into `XmlDocument` (STJ `JsonElement`). Enumerators are **nested** (`XmlElement.Enumerator`), matching STJ `JsonElement.ArrayEnumerator` — not namespace-level types named `Enumerator`.

`Declaration` is `XmlDeclarationInfo?` (readonly struct). Mutating `Nodes.XmlDeclarationNode` must not be possible through `XmlDocument`. `WriteTo` uses the infoset captured at parse.

Prolog/epilog comments and PIs are **preserved** in the parse buffer and round-tripped by `XmlDocument.WriteTo`. They are **not** enumerable as children of `RootElement`.

### Values

- `GetString()` returns the text exactly as parsed (after the reader's line-end and attribute-value normalization), whitespace included. For an element it is the concatenated text and CDATA of its descendants. For a nil element it is `null`.
- `GetInt32()`, `GetDouble()`, `GetDateTime()`, and the other typed getters apply the lexical rules in [XML fidelity](02-xml-fidelity.md#lexical-rules-xsd-10-invariant) through the same code as `Utf8XmlReader`. `<n> 42 </n>` is the string `" 42 "` and the integer `42`. A `TryGet` method returns `false`, with the default value, for a value the type cannot hold; it throws what the getter throws for a nil element (`XmlException`) and for a default `XmlElement` (`InvalidOperationException`), as `JsonElement.TryGetInt32` throws for a value of the wrong kind. `ParseAsync` reads the stream with asynchronous reads and then parses it as `Parse(Stream)` does; a `null` stream is refused before a task is created, as `JsonDocument.ParseAsync` refuses it. `TryParseValue` returns false, with the reader where it was, when the reader's block ends before the element does and the block is not the last, and when the reader is on an end tag; in every other case it does what `ParseValue` does, and the reader has not moved when an exception leaves it, as with `JsonDocument.TryParseValue`. The element is read twice, the first time by a reader that shares nothing with the caller's.
- A typed getter on a nil element throws: the element has no value.
- `IsEmptyElement` is true for `<e/>` and false for `<e></e>`, including when the element is nil.
- Name lookups compare `{local, namespace URI}`. The overloads without a namespace match the local name in any namespace. `XmlElementNode.SetAttribute` is not a lookup: without a namespace it sets the attribute in no namespace.
- `SerializeToElement` returns the root element of a document that holds no pooled memory, because the caller has no document to dispose. `XmlDocument.ParseValue` throws when the reader has no element left to read.

### WriteTo fidelity

`WriteTo` writes the infoset that was parsed. The exact guarantees are listed under [round-trip guarantees](02-xml-fidelity.md#round-trip-guarantees): namespace declarations as parsed and in place, `xmlns=""` kept (and added for a sub-tree that needs it), attribute order kept, `<e/>` versus `<e></e>` kept. A document that is already in the form the writer's options ask for is reproduced byte for byte; the form of an empty-element tag, the layout of attributes and the line ending follow `EmptyElementStyle`, `AttributeStyle` and `NewLine`. `XmlElementNode.IsEmptyElement` keeps `<e/>` versus `<e></e>` in the mutable tree.

Writing a sub-tree (`XmlElement.WriteTo`, `GetRawXml`, `ParseValue`) declares any namespace its own name or attributes need that an ancestor declared.

The writer's `XmlWriterOptions.Version` governs what can be written. A 1.1 document that undeclares a prefix (`xmlns:p=""`) or holds a RestrictedChar cannot be written by a 1.0 writer and throws.

### Storage

Rows are stored in document order (STJ `JsonDocument.MetadataDb`): an element, its attributes, then its descendants. The document owns one pooled copy of the UTF-8 payload; names and unescaped values are offsets into it. Details and measurements: [optimization](23-optimization.md).

Two internal members serve [XPath](27-xpath.md) and change nothing for a document that is never queried:

- `GetParentTable()` returns one `int` for each row: the row of its parent element, or `-1` for a row no element is above (an attribute row holds its element). It is built the first time it is asked for, in one pass with no stack, and published with `Interlocked.CompareExchange`, so threads that race all get the same table. It is a plain array, not a pooled one, and `Dispose` lets it go. Only the XPath axes that walk up (and telling whether two adjacent text rows are siblings) ask for it.
- `SliceMemory(start, length)` is `Slice` as `ReadOnlyMemory<byte>` over the document's own buffer, so that a string-value of one row is not copied. Nothing that holds such memory leaves an evaluation.

## DOM — mutable (JsonNode family)

XML is not a property bag. The mutable tree is a real XML node list, not `JsonObject`.

```csharp
namespace CloudNimble.Text.Xml.Nodes;

public abstract class XmlNode
{
    public abstract XmlValueKind ValueKind { get; }
    public XmlNode? Parent { get; }
    public abstract string? NamespaceUri { get; }

    public static XmlNode Parse(string xml); // document → root XmlElementNode; prolog comments/PIs discarded; attributes and children are made when first asked for
    public static XmlNode Parse(ReadOnlySpan<byte> xml);
    public static XmlNode ParseValue(ref Utf8XmlReader reader);

    public abstract void WriteTo(Utf8XmlWriter writer);
    public override string ToString();
    public XmlNode DeepClone();
}

public sealed class XmlNodeList : IList<XmlNode>
{
    public int Count { get; }
    public XmlNode this[int index] { get; set; }
    public void Add(XmlNode node);
    public void Insert(int index, XmlNode node);
    public bool Remove(XmlNode node);
    public void RemoveAt(int index);
    public void Clear();
    public IEnumerator<XmlNode> GetEnumerator();
}

public sealed class XmlAttributeNodeList : IList<XmlAttributeNode>
{
    public int Count { get; }
    public XmlAttributeNode this[int index] { get; set; }
    public XmlAttributeNode? this[string localName] { get; }
    public XmlAttributeNode? this[string localName, string namespaceUri] { get; }
    public void Add(XmlAttributeNode node);
    public bool Remove(XmlAttributeNode node);
    public void Clear();
    public IEnumerator<XmlAttributeNode> GetEnumerator();
}

public sealed class XmlElementNode : XmlNode, IEnumerable<XmlNode>
{
    public XmlElementNode(string localName, string? namespaceUri = null, string? prefix = null);

    public string LocalName { get; set; }
    public string Prefix { get; set; }
    public override string NamespaceUri { get; set; }

    public XmlAttributeNodeList Attributes { get; }
    public XmlNodeList Children { get; }
    public string InnerText { get; set; }

    public XmlElementNode? this[string localName] { get; }
    public XmlElementNode? this[string localName, string namespaceUri] { get; }

    public XmlElementNode AddElement(string localName, string? namespaceUri = null);
    public XmlAttributeNode SetAttribute(string localName, string? value, string? namespaceUri = null);
    public void Add(XmlNode node);
}

public sealed class XmlAttributeNode : XmlNode
{
    public string LocalName { get; set; }
    public string Prefix { get; set; }
    public override string NamespaceUri { get; set; }
    public string Value { get; set; }
}

public sealed class XmlTextNode : XmlNode { public string Value { get; set; } }
public sealed class XmlCDataNode : XmlNode { public string Value { get; set; } }
public sealed class XmlCommentNode : XmlNode { public string Value { get; set; } }
public sealed class XmlProcessingInstructionNode : XmlNode
{
    public string Target { get; set; }
    public string? Data { get; set; }
}

/// <summary>Mutable tree only. Immutable documents use <see cref="XmlDeclarationInfo"/>.</summary>
public sealed class XmlDeclarationNode : XmlNode
{
    public string Version { get; set; }
    public string Encoding { get; set; }
    public bool? Standalone { get; set; }
}
```

`XmlNodeList` / `XmlAttributeNodeList` are our types in `CloudNimble.Text.Xml.Nodes`, not BCL.

**Enumeration and threads follow `JsonNode`** (decision 86 in [doc findings](24-doc-findings-resolution.md)).

- A list that changes while it is enumerated ends the enumeration: the next `MoveNext` throws `InvalidOperationException` with the message of `List<T>`, "Collection was modified; enumeration operation may not execute." `Add`, `Insert`, `RemoveAt`, a `Remove` that finds its node, `Clear`, and an index assignment of a different node are changes. A `Remove` that finds nothing and the assignment of the node already in place are not. The version is taken when `GetEnumerator` is called.
- A tree that no thread is changing may be read from several threads at once, including a parsed tree whose elements have not been looked at. An element's lists are built aside and put in place with a compare-and-exchange, the parsed source is let go of last, and the list getters make an empty list the same way, so every thread gets the same lists and nodes. Changes are not synchronized.

Serializer integration: a property of type `XmlNode` / `XmlElementNode` is an XML island.

---

[Index](README.md) · [Previous: Utf8XmlWriter](05-api-writer.md) · [Next: XmlSerializer](07-api-serializer.md)
