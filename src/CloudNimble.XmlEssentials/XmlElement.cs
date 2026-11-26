using System;
using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using CloudNimble.Text.Xml.Nodes;

namespace CloudNimble.Text.Xml
{

    /// <summary>
    /// Represents a specific XML node within an <see cref="XmlDocument"/>.
    /// </summary>
    /// <example>
    /// <code>
    /// using var document = XmlDocument.Parse("&lt;catalog&gt;&lt;item id=\"1\"&gt;&lt;qty&gt;3&lt;/qty&gt;&lt;/item&gt;&lt;/catalog&gt;"u8);
    /// foreach (var item in document.RootElement.EnumerateChildren())
    /// {
    ///     var id = item.GetAttribute("id").GetInt32();
    ///     var qty = item.GetChild("qty").GetInt32();
    /// }
    /// </code>
    /// </example>
    /// <remarks>
    /// This struct is the XML counterpart of <c>JsonElement</c> in System.Text.Json. A value can represent
    /// an element, an attribute, a text node, a CDATA section, a comment, or a processing instruction;
    /// <see cref="ValueKind"/> tells which. A value is usable only while its parent
    /// <see cref="XmlDocument"/> is not disposed.
    /// The value of an attribute, a text node, a CDATA section, a comment, or a processing instruction
    /// is its content. The value of an element is the text of all text nodes and CDATA sections inside
    /// it, in document order.
    /// The typed getters, such as <see cref="GetInt32"/>, ignore leading and trailing white space in the value.
    /// Name lookups, enumeration, the numeric and Boolean getters, and <see cref="WriteTo"/> do not allocate.
    /// Members that return a <see cref="string"/>, a <see cref="T:byte[]"/>, or an <see cref="XmlNode"/>
    /// allocate only that result, except <see cref="GetRawXml"/>, which also allocates the buffer and the writer it writes the markup with.
    /// </remarks>
    public readonly struct XmlElement : IEquatable<XmlElement>
    {

        #region Fields

        private readonly XmlDocument? _document;
        private readonly int _index;

        #endregion

        #region Public Properties

        /// <summary>
        /// Gets the document this element is a cursor into, or <see langword="null"/> for a default element.
        /// </summary>
        internal XmlDocument? Document => _document;

        /// <summary>
        /// Gets the row this element is a cursor on.
        /// </summary>
        internal int Index => _index;

        /// <summary>
        /// Gets the number of attributes on this element.
        /// </summary>
        /// <value>
        /// The number of attributes on this element, including namespace declarations, or 0 if this node is not an element.
        /// </value>
        /// <exception cref="InvalidOperationException">
        /// This value is a default <see cref="XmlElement"/>, which refers to no node.
        /// </exception>
        /// <exception cref="ObjectDisposedException">
        /// The parent <see cref="XmlDocument"/> has been disposed.
        /// </exception>
        public int AttributeCount
        {
            get
            {
                ref var row = ref EnsureDocument().Row(_index);
                return XmlDocument.IsElementKind(row.Kind) ? row.AttributeCount : 0;
            }
        }

        /// <summary>
        /// Gets a value that indicates whether this node is an element that is written as an empty-element tag (<c>&lt;e/&gt;</c>).
        /// </summary>
        /// <value>
        /// <see langword="true"/> if this node is an element that is written as an empty-element tag; otherwise,
        /// <see langword="false"/>. An element with a start tag and an end tag and no content
        /// (<c>&lt;e&gt;&lt;/e&gt;</c>) gives <see langword="false"/>.
        /// </value>
        /// <exception cref="InvalidOperationException">
        /// This value is a default <see cref="XmlElement"/>, which refers to no node.
        /// </exception>
        /// <exception cref="ObjectDisposedException">
        /// The parent <see cref="XmlDocument"/> has been disposed.
        /// </exception>
        public bool IsEmptyElement => EnsureDocument().Row(_index).IsEmpty;

        /// <summary>
        /// Gets a value that indicates whether this node is an element that is marked as nil with the <c>xsi:nil</c> attribute.
        /// </summary>
        /// <value>
        /// <see langword="true"/> if this node is an element whose <c>xsi:nil</c> attribute is <c>true</c> or
        /// <c>1</c>; otherwise, <see langword="false"/>.
        /// </value>
        /// <exception cref="InvalidOperationException">
        /// This value is a default <see cref="XmlElement"/>, which refers to no node.
        /// </exception>
        /// <exception cref="ObjectDisposedException">
        /// The parent <see cref="XmlDocument"/> has been disposed.
        /// </exception>
        public bool IsNil => EnsureDocument().Row(_index).Kind is XmlValueKind.Nil;

        /// <summary>
        /// Gets the local name of this node.
        /// </summary>
        /// <value>
        /// The local name of an element or an attribute, without its prefix, or the target of a processing
        /// instruction. The value is an empty string for a node that has no name.
        /// </value>
        /// <exception cref="InvalidOperationException">
        /// This value is a default <see cref="XmlElement"/>, which refers to no node.
        /// </exception>
        /// <exception cref="ObjectDisposedException">
        /// The parent <see cref="XmlDocument"/> has been disposed.
        /// </exception>
        public string LocalName
        {
            get
            {
                var document = EnsureDocument();
                return Encoding.UTF8.GetString(document.LocalName(in document.Row(_index)));
            }
        }

        /// <summary>
        /// Gets the namespace URI of this node.
        /// </summary>
        /// <value>
        /// The namespace URI of the element or attribute, or an empty string if the node is in no namespace.
        /// </value>
        /// <exception cref="InvalidOperationException">
        /// This value is a default <see cref="XmlElement"/>, which refers to no node.
        /// </exception>
        /// <exception cref="ObjectDisposedException">
        /// The parent <see cref="XmlDocument"/> has been disposed.
        /// </exception>
        public string NamespaceUri
        {
            get
            {
                var document = EnsureDocument();
                return Encoding.UTF8.GetString(document.NamespaceUri(in document.Row(_index)));
            }
        }

        /// <summary>
        /// Gets the namespace prefix of this node.
        /// </summary>
        /// <value>
        /// The prefix of the element or attribute name, or an empty string if the name has no prefix.
        /// </value>
        /// <exception cref="InvalidOperationException">
        /// This value is a default <see cref="XmlElement"/>, which refers to no node.
        /// </exception>
        /// <exception cref="ObjectDisposedException">
        /// The parent <see cref="XmlDocument"/> has been disposed.
        /// </exception>
        public string Prefix
        {
            get
            {
                var document = EnsureDocument();
                return Encoding.UTF8.GetString(document.Prefix(in document.Row(_index)));
            }
        }

        /// <summary>
        /// Gets the reader token type that corresponds to this node.
        /// </summary>
        /// <value>
        /// One of the enumeration values that indicates the token a <see cref="Utf8XmlReader"/> reports for
        /// this kind of node. An element gives <see cref="XmlTokenType.StartElement"/>.
        /// </value>
        /// <exception cref="InvalidOperationException">
        /// This value is a default <see cref="XmlElement"/>, which refers to no node.
        /// </exception>
        /// <exception cref="ObjectDisposedException">
        /// The parent <see cref="XmlDocument"/> has been disposed.
        /// </exception>
        public XmlTokenType TokenType
        {
            get
            {
                return EnsureDocument().Row(_index).Kind switch
                {
                    XmlValueKind.Element or XmlValueKind.EmptyElement or XmlValueKind.Nil => XmlTokenType.StartElement,
                    XmlValueKind.Attribute => XmlTokenType.Attribute,
                    XmlValueKind.Text => XmlTokenType.Text,
                    XmlValueKind.CData => XmlTokenType.CData,
                    XmlValueKind.Comment => XmlTokenType.Comment,
                    XmlValueKind.ProcessingInstruction => XmlTokenType.ProcessingInstruction,
                    _ => XmlTokenType.None,
                };
            }
        }

        /// <summary>
        /// Gets the kind of the current XML node.
        /// </summary>
        /// <value>
        /// One of the enumeration values that indicates the kind of this node. A default
        /// <see cref="XmlElement"/> gives <see cref="XmlValueKind.Undefined"/>.
        /// </value>
        /// <exception cref="ObjectDisposedException">
        /// The parent <see cref="XmlDocument"/> has been disposed.
        /// </exception>
        public XmlValueKind ValueKind
        {
            get
            {
                if (_document is null)
                {
                    return XmlValueKind.Undefined;
                }

                _document.ThrowIfDisposed();
                return _document.Row(_index).Kind;
            }
        }

        #endregion

        #region Constructors

        /// <summary>
        /// Creates a cursor into <paramref name="document"/> at <paramref name="index"/>.
        /// </summary>
        /// <param name="document">The owning document.</param>
        /// <param name="index">The row index.</param>
        internal XmlElement(XmlDocument document, int index)
        {
            _document = document;
            _index = index;
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Creates a mutable copy of this node and all of its attributes and descendants.
        /// </summary>
        /// <returns>
        /// A new <see cref="XmlNode"/> that has no parent. Its type matches the kind of this node, for
        /// example an <see cref="XmlElementNode"/> for an element.
        /// </returns>
        /// <exception cref="InvalidOperationException">
        /// This value is a default <see cref="XmlElement"/>, which refers to no node.
        /// </exception>
        /// <exception cref="ObjectDisposedException">
        /// The parent <see cref="XmlDocument"/> has been disposed.
        /// </exception>
        /// <remarks>
        /// The copy does not depend on the parent <see cref="XmlDocument"/> and stays valid after the document is disposed.
        /// </remarks>
        public XmlNode CloneNode()
        {
            return EnsureDocument().CloneRow(_index, new string?[64], null);
        }

        /// <summary>
        /// Makes the mutable tree of this element, which takes the document for its own.
        /// </summary>
        /// <returns>The node. An element under it makes its attributes and children when they are first asked for.</returns>
        /// <remarks>
        /// The caller parsed the document for this and does not dispose it: the tree keeps it, out of the pool,
        /// for the nodes that have not been made yet.
        /// </remarks>
        internal XmlNode ToOwnedNode()
        {
            var document = EnsureDocument();
            document.MoveOutOfPool();
            var source = new XmlNodeSource(document);
            return document.CloneRow(_index, source.Names, source);
        }

        /// <inheritdoc />
        public bool Equals(XmlElement other)
        {
            return ReferenceEquals(_document, other._document) && _index == other._index;
        }

        /// <inheritdoc />
        public override bool Equals(object? obj)
        {
            return obj is XmlElement other && Equals(other);
        }

        /// <summary>
        /// Gets an enumerator to enumerate the attributes of this element.
        /// </summary>
        /// <returns>
        /// An enumerator to enumerate the attributes of this element, in document order. Namespace
        /// declarations are included. The enumerator yields nothing if this node is not an element.
        /// </returns>
        /// <exception cref="InvalidOperationException">
        /// This value is a default <see cref="XmlElement"/>, which refers to no node.
        /// </exception>
        /// <exception cref="ObjectDisposedException">
        /// The parent <see cref="XmlDocument"/> has been disposed.
        /// </exception>
        public AttributeEnumerator EnumerateAttributes()
        {
            var document = EnsureDocument();
            ref var row = ref document.Row(_index);
            var count = XmlDocument.IsElementKind(row.Kind) ? row.AttributeCount : 0;
            return new AttributeEnumerator(document, _index + 1, _index + 1 + count);
        }

        /// <summary>
        /// Gets an enumerator to enumerate the child nodes of this element.
        /// </summary>
        /// <returns>
        /// An enumerator to enumerate the child nodes of this element, in document order. Child nodes are
        /// elements, text nodes, CDATA sections, comments, and processing instructions. Attributes are not
        /// included. The enumerator yields nothing if this node is not an element.
        /// </returns>
        /// <exception cref="InvalidOperationException">
        /// This value is a default <see cref="XmlElement"/>, which refers to no node.
        /// </exception>
        /// <exception cref="ObjectDisposedException">
        /// The parent <see cref="XmlDocument"/> has been disposed.
        /// </exception>
        public Enumerator EnumerateChildren()
        {
            var document = EnsureDocument();
            ChildRange(document, out var first, out var end);
            return new Enumerator(document, first, end);
        }

        /// <summary>
        /// Gets the first attribute of this element that has the specified local name, in any namespace.
        /// </summary>
        /// <param name="localName">The local name of the attribute to find, without a prefix.</param>
        /// <returns>
        /// An <see cref="XmlElement"/> that represents the attribute.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="localName"/> is <see langword="null"/>.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// This value is a default <see cref="XmlElement"/>, which refers to no node.
        /// </exception>
        /// <exception cref="ObjectDisposedException">
        /// The parent <see cref="XmlDocument"/> has been disposed.
        /// </exception>
        /// <exception cref="XmlException">
        /// This element has no attribute with the specified local name.
        /// </exception>
        public XmlElement GetAttribute(string localName)
        {
            if (!TryGetAttribute(localName, out var value))
            {
                throw XmlException.Create($"Attribute '{localName}' was not found.");
            }

            return value;
        }

        /// <summary>
        /// Gets the attribute of this element that has the specified local name and namespace URI.
        /// </summary>
        /// <param name="localName">The local name of the attribute to find, without a prefix.</param>
        /// <param name="namespaceUri">The namespace URI of the attribute, or an empty string for an attribute in no namespace.</param>
        /// <returns>
        /// An <see cref="XmlElement"/> that represents the attribute.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="localName"/> or <paramref name="namespaceUri"/> is <see langword="null"/>.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// This value is a default <see cref="XmlElement"/>, which refers to no node.
        /// </exception>
        /// <exception cref="ObjectDisposedException">
        /// The parent <see cref="XmlDocument"/> has been disposed.
        /// </exception>
        /// <exception cref="XmlException">
        /// This element has no attribute with the specified local name and namespace URI.
        /// </exception>
        public XmlElement GetAttribute(string localName, string namespaceUri)
        {
            ArgumentNullException.ThrowIfNull(localName);
            ArgumentNullException.ThrowIfNull(namespaceUri);
            if (!TryFindAttribute(localName, namespaceUri, out var value))
            {
                throw XmlException.Create($"Attribute '{localName}' was not found.");
            }

            return value;
        }

        /// <summary>
        /// Gets the first attribute of this element that has the local name specified as a read-only span of characters, in any namespace.
        /// </summary>
        /// <param name="localName">The local name of the attribute to find, without a prefix.</param>
        /// <returns>
        /// An <see cref="XmlElement"/> that represents the attribute.
        /// </returns>
        /// <exception cref="InvalidOperationException">
        /// This value is a default <see cref="XmlElement"/>, which refers to no node.
        /// </exception>
        /// <exception cref="ObjectDisposedException">
        /// The parent <see cref="XmlDocument"/> has been disposed.
        /// </exception>
        /// <exception cref="XmlException">
        /// This element has no attribute with the specified local name.
        /// </exception>
        public XmlElement GetAttribute(ReadOnlySpan<char> localName)
        {
            if (!TryFindAttribute(localName, null, out var value))
            {
                throw XmlException.Create($"Attribute '{localName.ToString()}' was not found.");
            }

            return value;
        }

        /// <summary>
        /// Gets the first attribute of this element that has the local name specified as a read-only span of UTF-8 bytes, in any namespace.
        /// </summary>
        /// <param name="utf8LocalName">The UTF-8 encoded local name of the attribute to find, without a prefix.</param>
        /// <returns>
        /// An <see cref="XmlElement"/> that represents the attribute.
        /// </returns>
        /// <exception cref="InvalidOperationException">
        /// This value is a default <see cref="XmlElement"/>, which refers to no node.
        /// </exception>
        /// <exception cref="ObjectDisposedException">
        /// The parent <see cref="XmlDocument"/> has been disposed.
        /// </exception>
        /// <exception cref="XmlException">
        /// This element has no attribute with the specified local name.
        /// </exception>
        public XmlElement GetAttribute(ReadOnlySpan<byte> utf8LocalName)
        {
            if (!TryFindAttributeUtf8(utf8LocalName, out var value))
            {
                throw XmlException.Create($"Attribute '{Encoding.UTF8.GetString(utf8LocalName)}' was not found.");
            }

            return value;
        }

        /// <summary>
        /// Gets the value of this node as a <see cref="bool"/>.
        /// </summary>
        /// <returns>
        /// The value of this node as a <see cref="bool"/>.
        /// </returns>
        /// <exception cref="InvalidOperationException">
        /// This value is a default <see cref="XmlElement"/>, which refers to no node.
        /// </exception>
        /// <exception cref="ObjectDisposedException">
        /// The parent <see cref="XmlDocument"/> has been disposed.
        /// </exception>
        /// <exception cref="XmlException">
        /// The value is not an <c>xs:boolean</c>, which is <c>true</c>, <c>false</c>, <c>1</c>, or <c>0</c>.
        /// -or-
        /// This node is an element that is marked as nil.
        /// </exception>
        public bool GetBoolean()
        {
            var utf8 = TypedValueUtf8(out var rented);
            var parsed = XmlLexical.TryParseBoolean(utf8, out var value);
            XmlDocument.ReturnScratch(rented);
            if (!parsed)
            {
                throw XmlException.Create("The value is not a boolean.");
            }

            return value;
        }

        /// <summary>
        /// Gets the value of this node as a <see cref="byte"/>.
        /// </summary>
        /// <returns>
        /// The value of this node as a <see cref="byte"/>.
        /// </returns>
        /// <exception cref="InvalidOperationException">
        /// This value is a default <see cref="XmlElement"/>, which refers to no node.
        /// </exception>
        /// <exception cref="ObjectDisposedException">
        /// The parent <see cref="XmlDocument"/> has been disposed.
        /// </exception>
        /// <exception cref="XmlException">
        /// The value is not an <c>xs:unsignedByte</c>, or it is outside the range of a <see cref="byte"/>.
        /// -or-
        /// This node is an element that is marked as nil.
        /// </exception>
        public byte GetByte()
        {
            if (!TryGetByte(out var value))
            {
                throw XmlException.Create("The value is not a valid integer.");
            }

            return value;
        }

        /// <summary>
        /// Gets the value of this node as a byte array, decoded from Base64 text.
        /// </summary>
        /// <returns>
        /// The value decoded as a byte array.
        /// </returns>
        /// <exception cref="InvalidOperationException">
        /// This value is a default <see cref="XmlElement"/>, which refers to no node.
        /// </exception>
        /// <exception cref="ObjectDisposedException">
        /// The parent <see cref="XmlDocument"/> has been disposed.
        /// </exception>
        /// <exception cref="XmlException">
        /// The value is not valid Base64 text (<c>xs:base64Binary</c>).
        /// -or-
        /// This node is an element that is marked as nil.
        /// </exception>
        public byte[] GetBytesFromBase64()
        {
            var utf8 = TypedValueUtf8(out var rented);
            var parsed = XmlLexical.TryDecodeBase64(utf8, out var value);
            XmlDocument.ReturnScratch(rented);
            if (!parsed)
            {
                throw XmlException.Create("The value is not valid base64.");
            }

            return value!;
        }

        /// <summary>
        /// Gets the first child element of this element that has the specified local name, in any namespace.
        /// </summary>
        /// <param name="localName">The local name of the child element to find, without a prefix.</param>
        /// <returns>
        /// An <see cref="XmlElement"/> that represents the child element.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="localName"/> is <see langword="null"/>.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// This value is a default <see cref="XmlElement"/>, which refers to no node.
        /// </exception>
        /// <exception cref="ObjectDisposedException">
        /// The parent <see cref="XmlDocument"/> has been disposed.
        /// </exception>
        /// <exception cref="XmlException">
        /// This element has no child element with the specified local name.
        /// </exception>
        /// <remarks>
        /// Only direct children are searched, not deeper descendants.
        /// </remarks>
        public XmlElement GetChild(string localName)
        {
            if (!TryGetChild(localName, out var value))
            {
                throw XmlException.Create($"Child element '{localName}' was not found.");
            }

            return value;
        }

        /// <summary>
        /// Gets the first child element of this element that has the specified local name and namespace URI.
        /// </summary>
        /// <param name="localName">The local name of the child element to find, without a prefix.</param>
        /// <param name="namespaceUri">The namespace URI of the child element, or an empty string for an element in no namespace.</param>
        /// <returns>
        /// An <see cref="XmlElement"/> that represents the child element.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="localName"/> or <paramref name="namespaceUri"/> is <see langword="null"/>.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// This value is a default <see cref="XmlElement"/>, which refers to no node.
        /// </exception>
        /// <exception cref="ObjectDisposedException">
        /// The parent <see cref="XmlDocument"/> has been disposed.
        /// </exception>
        /// <exception cref="XmlException">
        /// This element has no child element with the specified local name and namespace URI.
        /// </exception>
        /// <remarks>
        /// Only direct children are searched, not deeper descendants.
        /// </remarks>
        public XmlElement GetChild(string localName, string namespaceUri)
        {
            if (!TryGetChild(localName, namespaceUri, out var value))
            {
                throw XmlException.Create($"Child element '{localName}' was not found.");
            }

            return value;
        }

        /// <summary>
        /// Gets the first child element of this element that has the local name specified as a read-only span of characters, in any namespace.
        /// </summary>
        /// <param name="localName">The local name of the child element to find, without a prefix.</param>
        /// <returns>
        /// An <see cref="XmlElement"/> that represents the child element.
        /// </returns>
        /// <exception cref="InvalidOperationException">
        /// This value is a default <see cref="XmlElement"/>, which refers to no node.
        /// </exception>
        /// <exception cref="ObjectDisposedException">
        /// The parent <see cref="XmlDocument"/> has been disposed.
        /// </exception>
        /// <exception cref="XmlException">
        /// This element has no child element with the specified local name.
        /// </exception>
        /// <remarks>
        /// Only direct children are searched, not deeper descendants.
        /// </remarks>
        public XmlElement GetChild(ReadOnlySpan<char> localName)
        {
            if (!TryFindChild(localName, null, out var value))
            {
                throw XmlException.Create($"Child element '{localName.ToString()}' was not found.");
            }

            return value;
        }

        /// <summary>
        /// Gets the first child element of this element that has the local name specified as a read-only span of UTF-8 bytes, in any namespace.
        /// </summary>
        /// <param name="utf8LocalName">The UTF-8 encoded local name of the child element to find, without a prefix.</param>
        /// <returns>
        /// An <see cref="XmlElement"/> that represents the child element.
        /// </returns>
        /// <exception cref="InvalidOperationException">
        /// This value is a default <see cref="XmlElement"/>, which refers to no node.
        /// </exception>
        /// <exception cref="ObjectDisposedException">
        /// The parent <see cref="XmlDocument"/> has been disposed.
        /// </exception>
        /// <exception cref="XmlException">
        /// This element has no child element with the specified local name.
        /// </exception>
        /// <remarks>
        /// Only direct children are searched, not deeper descendants.
        /// </remarks>
        public XmlElement GetChild(ReadOnlySpan<byte> utf8LocalName)
        {
            if (!TryFindChildUtf8(utf8LocalName, out var value))
            {
                throw XmlException.Create($"Child element '{Encoding.UTF8.GetString(utf8LocalName)}' was not found.");
            }

            return value;
        }

        /// <summary>
        /// Gets the value of this node as a <see cref="DateTime"/>.
        /// </summary>
        /// <returns>
        /// The value of this node as a <see cref="DateTime"/>.
        /// </returns>
        /// <exception cref="InvalidOperationException">
        /// This value is a default <see cref="XmlElement"/>, which refers to no node.
        /// </exception>
        /// <exception cref="ObjectDisposedException">
        /// The parent <see cref="XmlDocument"/> has been disposed.
        /// </exception>
        /// <exception cref="XmlException">
        /// The value is not an <c>xs:dateTime</c> that a <see cref="DateTime"/> can represent.
        /// -or-
        /// This node is an element that is marked as nil.
        /// </exception>
        /// <remarks>
        /// A value with no time zone gives <see cref="DateTimeKind.Unspecified"/>. A value with <c>Z</c> or
        /// a zero offset gives <see cref="DateTimeKind.Utc"/>. A value with a non-zero offset is converted
        /// to local time and gives <see cref="DateTimeKind.Local"/>.
        /// </remarks>
        public DateTime GetDateTime()
        {
            var utf8 = TypedValueUtf8(out var rented);
            var parsed = XmlLexical.TryParseDateTime(utf8, out var value);
            XmlDocument.ReturnScratch(rented);
            if (!parsed)
            {
                throw XmlException.Create("The value is not a valid dateTime.");
            }

            return value;
        }

        /// <summary>
        /// Gets the value of this node as a <see cref="DateTimeOffset"/>.
        /// </summary>
        /// <returns>
        /// The value of this node as a <see cref="DateTimeOffset"/>.
        /// </returns>
        /// <exception cref="InvalidOperationException">
        /// This value is a default <see cref="XmlElement"/>, which refers to no node.
        /// </exception>
        /// <exception cref="ObjectDisposedException">
        /// The parent <see cref="XmlDocument"/> has been disposed.
        /// </exception>
        /// <exception cref="XmlException">
        /// The value is not an <c>xs:dateTime</c> that a <see cref="DateTimeOffset"/> can represent.
        /// -or-
        /// This node is an element that is marked as nil.
        /// </exception>
        /// <remarks>
        /// A value with no time zone takes the local offset that is in force at that date and time.
        /// </remarks>
        public DateTimeOffset GetDateTimeOffset()
        {
            var utf8 = TypedValueUtf8(out var rented);
            var parsed = XmlLexical.TryParseDateTimeOffset(utf8, out var value);
            XmlDocument.ReturnScratch(rented);
            if (!parsed)
            {
                throw XmlException.Create("The value is not a valid dateTime.");
            }

            return value;
        }

        /// <summary>
        /// Gets the value of this node as a <see cref="decimal"/>.
        /// </summary>
        /// <returns>
        /// The value of this node as a <see cref="decimal"/>.
        /// </returns>
        /// <exception cref="InvalidOperationException">
        /// This value is a default <see cref="XmlElement"/>, which refers to no node.
        /// </exception>
        /// <exception cref="ObjectDisposedException">
        /// The parent <see cref="XmlDocument"/> has been disposed.
        /// </exception>
        /// <exception cref="XmlException">
        /// The value is not an <c>xs:decimal</c> that a <see cref="decimal"/> can represent.
        /// -or-
        /// This node is an element that is marked as nil.
        /// </exception>
        public decimal GetDecimal()
        {
            var utf8 = TypedValueUtf8(out var rented);
            var parsed = XmlLexical.TryParseDecimal(utf8, out var value);
            XmlDocument.ReturnScratch(rented);
            if (!parsed)
            {
                throw XmlException.Create("The value is not a valid decimal.");
            }

            return value;
        }

        /// <summary>
        /// Gets the value of this node as a <see cref="double"/>.
        /// </summary>
        /// <returns>
        /// The value of this node as a <see cref="double"/>.
        /// </returns>
        /// <exception cref="InvalidOperationException">
        /// This value is a default <see cref="XmlElement"/>, which refers to no node.
        /// </exception>
        /// <exception cref="ObjectDisposedException">
        /// The parent <see cref="XmlDocument"/> has been disposed.
        /// </exception>
        /// <exception cref="XmlException">
        /// The value is not an <c>xs:double</c>.
        /// -or-
        /// This node is an element that is marked as nil.
        /// </exception>
        /// <remarks>
        /// The literals <c>INF</c>, <c>-INF</c>, and <c>NaN</c> are accepted in any letter case.
        /// </remarks>
        public double GetDouble()
        {
            var utf8 = TypedValueUtf8(out var rented);
            var parsed = XmlLexical.TryParseDouble(utf8, out var value);
            XmlDocument.ReturnScratch(rented);
            if (!parsed)
            {
                throw XmlException.Create("The value is not a valid double.");
            }

            return value;
        }

        /// <summary>
        /// Gets the value of this node as a <see cref="Guid"/>.
        /// </summary>
        /// <returns>
        /// The value of this node as a <see cref="Guid"/>.
        /// </returns>
        /// <exception cref="InvalidOperationException">
        /// This value is a default <see cref="XmlElement"/>, which refers to no node.
        /// </exception>
        /// <exception cref="ObjectDisposedException">
        /// The parent <see cref="XmlDocument"/> has been disposed.
        /// </exception>
        /// <exception cref="XmlException">
        /// The value is not a GUID in the <c>D</c>, <c>N</c>, <c>B</c>, or <c>P</c> format.
        /// -or-
        /// This node is an element that is marked as nil.
        /// </exception>
        public Guid GetGuid()
        {
            var utf8 = TypedValueUtf8(out var rented);
            var parsed = XmlLexical.TryParseGuid(utf8, out var value);
            XmlDocument.ReturnScratch(rented);
            if (!parsed)
            {
                throw XmlException.Create("The value is not a valid GUID.");
            }

            return value;
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return HashCode.Combine(_document, _index);
        }

        /// <summary>
        /// Gets the text content of this node.
        /// </summary>
        /// <returns>
        /// For an element, the text of all text nodes and CDATA sections inside it, in document order. For an
        /// attribute, a text node, or a CDATA section, its value. For a comment or a processing instruction,
        /// an empty string.
        /// </returns>
        /// <exception cref="InvalidOperationException">
        /// This value is a default <see cref="XmlElement"/>, which refers to no node.
        /// </exception>
        /// <exception cref="ObjectDisposedException">
        /// The parent <see cref="XmlDocument"/> has been disposed.
        /// </exception>
        /// <remarks>
        /// Unlike <see cref="GetString"/>, this method does not return <see langword="null"/> for an element that is marked as nil.
        /// </remarks>
        public string GetInnerText()
        {
            if (EnsureDocument().Row(_index).Kind is XmlValueKind.Comment or XmlValueKind.ProcessingInstruction)
            {
                return string.Empty;
            }

            return ValueString();
        }

        /// <summary>
        /// Gets the value of this node as a <see cref="short"/>.
        /// </summary>
        /// <returns>
        /// The value of this node as a <see cref="short"/>.
        /// </returns>
        /// <exception cref="InvalidOperationException">
        /// This value is a default <see cref="XmlElement"/>, which refers to no node.
        /// </exception>
        /// <exception cref="ObjectDisposedException">
        /// The parent <see cref="XmlDocument"/> has been disposed.
        /// </exception>
        /// <exception cref="XmlException">
        /// The value is not an <c>xs:short</c>, or it is outside the range of a <see cref="short"/>.
        /// -or-
        /// This node is an element that is marked as nil.
        /// </exception>
        public short GetInt16()
        {
            if (!TryGetInt16(out var value))
            {
                throw XmlException.Create("The value is not a valid integer.");
            }

            return value;
        }

        /// <summary>
        /// Gets the value of this node as an <see cref="int"/>.
        /// </summary>
        /// <returns>
        /// The value of this node as an <see cref="int"/>.
        /// </returns>
        /// <exception cref="InvalidOperationException">
        /// This value is a default <see cref="XmlElement"/>, which refers to no node.
        /// </exception>
        /// <exception cref="ObjectDisposedException">
        /// The parent <see cref="XmlDocument"/> has been disposed.
        /// </exception>
        /// <exception cref="XmlException">
        /// The value is not an <c>xs:int</c>, or it is outside the range of an <see cref="int"/>.
        /// -or-
        /// This node is an element that is marked as nil.
        /// </exception>
        public int GetInt32()
        {
            var utf8 = TypedValueUtf8(out var rented);
            var parsed = XmlLexical.TryParseInt32(utf8, out var value);
            XmlDocument.ReturnScratch(rented);
            if (!parsed)
            {
                throw XmlException.Create("The value is not a valid integer.");
            }

            return value;
        }

        /// <summary>
        /// Gets the value of this node as a <see cref="long"/>.
        /// </summary>
        /// <returns>
        /// The value of this node as a <see cref="long"/>.
        /// </returns>
        /// <exception cref="InvalidOperationException">
        /// This value is a default <see cref="XmlElement"/>, which refers to no node.
        /// </exception>
        /// <exception cref="ObjectDisposedException">
        /// The parent <see cref="XmlDocument"/> has been disposed.
        /// </exception>
        /// <exception cref="XmlException">
        /// The value is not an <c>xs:long</c>, or it is outside the range of a <see cref="long"/>.
        /// -or-
        /// This node is an element that is marked as nil.
        /// </exception>
        public long GetInt64()
        {
            var utf8 = TypedValueUtf8(out var rented);
            var parsed = XmlLexical.TryParseInt64(utf8, out var value);
            XmlDocument.ReturnScratch(rented);
            if (!parsed)
            {
                throw XmlException.Create("The value is not a valid integer.");
            }

            return value;
        }

        /// <summary>
        /// Gets a string that holds this node written as XML.
        /// </summary>
        /// <returns>
        /// The XML markup of this node, including its attributes and descendants. The result is an empty
        /// string for an attribute, because an attribute has no markup outside its element.
        /// </returns>
        /// <exception cref="InvalidOperationException">
        /// This value is a default <see cref="XmlElement"/>, which refers to no node.
        /// </exception>
        /// <exception cref="ObjectDisposedException">
        /// The parent <see cref="XmlDocument"/> has been disposed.
        /// </exception>
        /// <remarks>
        /// The markup is written again from the parsed node with <see cref="WriteTo"/>, without an XML
        /// declaration. It is not a copy of the original input.
        /// </remarks>
        public string GetRawXml()
        {
            var buffer = new ArrayBufferWriter<byte>();
            using var writer = new Utf8XmlWriter(buffer, new XmlWriterOptions { NewLine = "\n", OmitXmlDeclaration = true });
            WriteTo(writer);
            writer.Flush();
            return Encoding.UTF8.GetString(buffer.WrittenSpan);
        }

        /// <summary>
        /// Gets the value of this node as an <see cref="sbyte"/>.
        /// </summary>
        /// <returns>
        /// The value of this node as an <see cref="sbyte"/>.
        /// </returns>
        /// <exception cref="InvalidOperationException">
        /// This value is a default <see cref="XmlElement"/>, which refers to no node.
        /// </exception>
        /// <exception cref="ObjectDisposedException">
        /// The parent <see cref="XmlDocument"/> has been disposed.
        /// </exception>
        /// <exception cref="XmlException">
        /// The value is not an <c>xs:byte</c>, or it is outside the range of an <see cref="sbyte"/>.
        /// -or-
        /// This node is an element that is marked as nil.
        /// </exception>
        [CLSCompliant(false)]
        public sbyte GetSByte()
        {
            if (!TryGetSByte(out var value))
            {
                throw XmlException.Create("The value is not a valid integer.");
            }

            return value;
        }

        /// <summary>
        /// Gets the value of this node as a <see cref="float"/>.
        /// </summary>
        /// <returns>
        /// The value of this node as a <see cref="float"/>.
        /// </returns>
        /// <exception cref="InvalidOperationException">
        /// This value is a default <see cref="XmlElement"/>, which refers to no node.
        /// </exception>
        /// <exception cref="ObjectDisposedException">
        /// The parent <see cref="XmlDocument"/> has been disposed.
        /// </exception>
        /// <exception cref="XmlException">
        /// The value is not an <c>xs:float</c>.
        /// -or-
        /// This node is an element that is marked as nil.
        /// </exception>
        /// <remarks>
        /// The literals <c>INF</c>, <c>-INF</c>, and <c>NaN</c> are accepted in any letter case.
        /// </remarks>
        public float GetSingle()
        {
            if (!TryGetSingle(out var value))
            {
                throw XmlException.Create("The value is not a valid float.");
            }

            return value;
        }

        /// <summary>
        /// Gets the value of this node as a <see cref="string"/>.
        /// </summary>
        /// <returns>
        /// The value of this node as a <see cref="string"/>, or <see langword="null"/> if this node is an
        /// element that is marked as nil.
        /// </returns>
        /// <exception cref="InvalidOperationException">
        /// This value is a default <see cref="XmlElement"/>, which refers to no node.
        /// </exception>
        /// <exception cref="ObjectDisposedException">
        /// The parent <see cref="XmlDocument"/> has been disposed.
        /// </exception>
        /// <remarks>
        /// Entity and character references in the value are expanded. Leading and trailing white space is kept.
        /// </remarks>
        public string? GetString()
        {
            return IsNil ? null : ValueString();
        }

        /// <summary>
        /// Gets the value of this node as a <see cref="ushort"/>.
        /// </summary>
        /// <returns>
        /// The value of this node as a <see cref="ushort"/>.
        /// </returns>
        /// <exception cref="InvalidOperationException">
        /// This value is a default <see cref="XmlElement"/>, which refers to no node.
        /// </exception>
        /// <exception cref="ObjectDisposedException">
        /// The parent <see cref="XmlDocument"/> has been disposed.
        /// </exception>
        /// <exception cref="XmlException">
        /// The value is not an <c>xs:unsignedShort</c>, or it is outside the range of a <see cref="ushort"/>.
        /// -or-
        /// This node is an element that is marked as nil.
        /// </exception>
        [CLSCompliant(false)]
        public ushort GetUInt16()
        {
            if (!TryGetUInt16(out var value))
            {
                throw XmlException.Create("The value is not a valid integer.");
            }

            return value;
        }

        /// <summary>
        /// Gets the value of this node as a <see cref="uint"/>.
        /// </summary>
        /// <returns>
        /// The value of this node as a <see cref="uint"/>.
        /// </returns>
        /// <exception cref="InvalidOperationException">
        /// This value is a default <see cref="XmlElement"/>, which refers to no node.
        /// </exception>
        /// <exception cref="ObjectDisposedException">
        /// The parent <see cref="XmlDocument"/> has been disposed.
        /// </exception>
        /// <exception cref="XmlException">
        /// The value is not an <c>xs:unsignedInt</c>, or it is outside the range of a <see cref="uint"/>.
        /// -or-
        /// This node is an element that is marked as nil.
        /// </exception>
        [CLSCompliant(false)]
        public uint GetUInt32()
        {
            if (!TryGetUInt32(out var value))
            {
                throw XmlException.Create("The value is not a valid integer.");
            }

            return value;
        }

        /// <summary>
        /// Gets the value of this node as a <see cref="ulong"/>.
        /// </summary>
        /// <returns>
        /// The value of this node as a <see cref="ulong"/>.
        /// </returns>
        /// <exception cref="InvalidOperationException">
        /// This value is a default <see cref="XmlElement"/>, which refers to no node.
        /// </exception>
        /// <exception cref="ObjectDisposedException">
        /// The parent <see cref="XmlDocument"/> has been disposed.
        /// </exception>
        /// <exception cref="XmlException">
        /// The value is not an <c>xs:unsignedLong</c>, or it is outside the range of a <see cref="ulong"/>.
        /// -or-
        /// This node is an element that is marked as nil.
        /// </exception>
        [CLSCompliant(false)]
        public ulong GetUInt64()
        {
            if (!TryGetUInt64(out var value))
            {
                throw XmlException.Create("The value is not a valid integer.");
            }

            return value;
        }

        /// <summary>
        /// Looks for the first attribute of this element that has the specified local name, in any namespace.
        /// </summary>
        /// <param name="localName">The local name of the attribute to find, without a prefix.</param>
        /// <param name="value">
        /// When this method returns, contains the attribute if it was found, or a default
        /// <see cref="XmlElement"/> if it was not. This parameter is treated as uninitialized.
        /// </param>
        /// <returns>
        /// <see langword="true"/> if the attribute was found; otherwise, <see langword="false"/>.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="localName"/> is <see langword="null"/>.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// This value is a default <see cref="XmlElement"/>, which refers to no node.
        /// </exception>
        /// <exception cref="ObjectDisposedException">
        /// The parent <see cref="XmlDocument"/> has been disposed.
        /// </exception>
        /// <remarks>
        /// This method returns <see langword="false"/> if this node is not an element.
        /// </remarks>
        public bool TryGetAttribute(string localName, out XmlElement value)
        {
            ArgumentNullException.ThrowIfNull(localName);
            return TryFindAttribute(localName, null, out value);
        }

        /// <summary>
        /// Looks for the first attribute of this element that has the local name specified as a read-only span of characters, in any namespace.
        /// </summary>
        /// <param name="localName">The local name of the attribute to find, without a prefix.</param>
        /// <param name="value">
        /// When this method returns, contains the attribute if it was found, or a default
        /// <see cref="XmlElement"/> if it was not. This parameter is treated as uninitialized.
        /// </param>
        /// <returns>
        /// <see langword="true"/> if the attribute was found; otherwise, <see langword="false"/>.
        /// </returns>
        /// <exception cref="InvalidOperationException">
        /// This value is a default <see cref="XmlElement"/>, which refers to no node.
        /// </exception>
        /// <exception cref="ObjectDisposedException">
        /// The parent <see cref="XmlDocument"/> has been disposed.
        /// </exception>
        /// <remarks>
        /// This method returns <see langword="false"/> if this node is not an element.
        /// </remarks>
        public bool TryGetAttribute(ReadOnlySpan<char> localName, out XmlElement value)
        {
            return TryFindAttribute(localName, null, out value);
        }

        /// <summary>
        /// Looks for the first attribute of this element that has the local name specified as a read-only span of UTF-8 bytes, in any namespace.
        /// </summary>
        /// <param name="utf8LocalName">The UTF-8 encoded local name of the attribute to find, without a prefix.</param>
        /// <param name="value">
        /// When this method returns, contains the attribute if it was found, or a default
        /// <see cref="XmlElement"/> if it was not. This parameter is treated as uninitialized.
        /// </param>
        /// <returns>
        /// <see langword="true"/> if the attribute was found; otherwise, <see langword="false"/>.
        /// </returns>
        /// <exception cref="InvalidOperationException">
        /// This value is a default <see cref="XmlElement"/>, which refers to no node.
        /// </exception>
        /// <exception cref="ObjectDisposedException">
        /// The parent <see cref="XmlDocument"/> has been disposed.
        /// </exception>
        /// <remarks>
        /// This method returns <see langword="false"/> if this node is not an element.
        /// </remarks>
        public bool TryGetAttribute(ReadOnlySpan<byte> utf8LocalName, out XmlElement value)
        {
            return TryFindAttributeUtf8(utf8LocalName, out value);
        }

        /// <summary>
        /// Attempts to get the value of this node as a <see cref="byte"/>.
        /// </summary>
        /// <param name="value">When this method returns, contains the value if it could be read, or the default value if it could not. This parameter is treated as uninitialized.</param>
        /// <returns>
        /// <see langword="true"/> if the whole value is an <c>xs:unsignedByte</c>; otherwise, <see langword="false"/>.
        /// </returns>
        /// <exception cref="InvalidOperationException">
        /// This value is a default <see cref="XmlElement"/>, which refers to no node.
        /// </exception>
        /// <exception cref="ObjectDisposedException">
        /// The parent <see cref="XmlDocument"/> has been disposed.
        /// </exception>
        /// <exception cref="XmlException">
        /// This node is an element that is marked as nil.
        /// </exception>
        public bool TryGetByte(out byte value)
        {
            var utf8 = TypedValueUtf8(out var rented);
            var parsed = XmlLexical.TryParseByte(utf8, out value);
            XmlDocument.ReturnScratch(rented);
            return parsed;
        }

        /// <summary>
        /// Attempts to get the value of this node as a byte array, decoded from Base64 text.
        /// </summary>
        /// <param name="value">When this method returns, contains the decoded bytes, or <see langword="null"/> if the value is not valid Base64. This parameter is treated as uninitialized.</param>
        /// <returns>
        /// <see langword="true"/> if the whole value is valid Base64 text (<c>xs:base64Binary</c>); otherwise, <see langword="false"/>.
        /// </returns>
        /// <exception cref="InvalidOperationException">
        /// This value is a default <see cref="XmlElement"/>, which refers to no node.
        /// </exception>
        /// <exception cref="ObjectDisposedException">
        /// The parent <see cref="XmlDocument"/> has been disposed.
        /// </exception>
        /// <exception cref="XmlException">
        /// This node is an element that is marked as nil.
        /// </exception>
        public bool TryGetBytesFromBase64([NotNullWhen(true)] out byte[]? value)
        {
            var utf8 = TypedValueUtf8(out var rented);
            var parsed = XmlLexical.TryDecodeBase64(utf8, out value);
            XmlDocument.ReturnScratch(rented);
            return parsed;
        }

        /// <summary>
        /// Looks for the first child element of this element that has the specified local name, in any namespace.
        /// </summary>
        /// <param name="localName">The local name of the child element to find, without a prefix.</param>
        /// <param name="value">
        /// When this method returns, contains the child element if it was found, or a default
        /// <see cref="XmlElement"/> if it was not. This parameter is treated as uninitialized.
        /// </param>
        /// <returns>
        /// <see langword="true"/> if the child element was found; otherwise, <see langword="false"/>.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="localName"/> is <see langword="null"/>.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// This value is a default <see cref="XmlElement"/>, which refers to no node.
        /// </exception>
        /// <exception cref="ObjectDisposedException">
        /// The parent <see cref="XmlDocument"/> has been disposed.
        /// </exception>
        /// <remarks>
        /// Only direct children are searched, not deeper descendants. This method returns
        /// <see langword="false"/> if this node is not an element.
        /// </remarks>
        public bool TryGetChild(string localName, out XmlElement value)
        {
            ArgumentNullException.ThrowIfNull(localName);
            return TryFindChild(localName, null, out value);
        }

        /// <summary>
        /// Looks for the first child element of this element that has the specified local name and namespace URI.
        /// </summary>
        /// <param name="localName">The local name of the child element to find, without a prefix.</param>
        /// <param name="namespaceUri">The namespace URI of the child element, or an empty string for an element in no namespace.</param>
        /// <param name="value">
        /// When this method returns, contains the child element if it was found, or a default
        /// <see cref="XmlElement"/> if it was not. This parameter is treated as uninitialized.
        /// </param>
        /// <returns>
        /// <see langword="true"/> if the child element was found; otherwise, <see langword="false"/>.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="localName"/> or <paramref name="namespaceUri"/> is <see langword="null"/>.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// This value is a default <see cref="XmlElement"/>, which refers to no node.
        /// </exception>
        /// <exception cref="ObjectDisposedException">
        /// The parent <see cref="XmlDocument"/> has been disposed.
        /// </exception>
        /// <remarks>
        /// Only direct children are searched, not deeper descendants. This method returns
        /// <see langword="false"/> if this node is not an element.
        /// </remarks>
        public bool TryGetChild(string localName, string namespaceUri, out XmlElement value)
        {
            ArgumentNullException.ThrowIfNull(localName);
            ArgumentNullException.ThrowIfNull(namespaceUri);
            return TryFindChild(localName, namespaceUri, out value);
        }

        /// <summary>
        /// Looks for the first child element of this element that has the local name specified as a read-only span of characters, in any namespace.
        /// </summary>
        /// <param name="localName">The local name of the child element to find, without a prefix.</param>
        /// <param name="value">
        /// When this method returns, contains the child element if it was found, or a default
        /// <see cref="XmlElement"/> if it was not. This parameter is treated as uninitialized.
        /// </param>
        /// <returns>
        /// <see langword="true"/> if the child element was found; otherwise, <see langword="false"/>.
        /// </returns>
        /// <exception cref="InvalidOperationException">
        /// This value is a default <see cref="XmlElement"/>, which refers to no node.
        /// </exception>
        /// <exception cref="ObjectDisposedException">
        /// The parent <see cref="XmlDocument"/> has been disposed.
        /// </exception>
        /// <remarks>
        /// Only direct children are searched, not deeper descendants. This method returns <see langword="false"/> if this node is not an element.
        /// </remarks>
        public bool TryGetChild(ReadOnlySpan<char> localName, out XmlElement value)
        {
            return TryFindChild(localName, null, out value);
        }

        /// <summary>
        /// Looks for the first child element of this element that has the local name specified as a read-only span of UTF-8 bytes, in any namespace.
        /// </summary>
        /// <param name="utf8LocalName">The UTF-8 encoded local name of the child element to find, without a prefix.</param>
        /// <param name="value">
        /// When this method returns, contains the child element if it was found, or a default
        /// <see cref="XmlElement"/> if it was not. This parameter is treated as uninitialized.
        /// </param>
        /// <returns>
        /// <see langword="true"/> if the child element was found; otherwise, <see langword="false"/>.
        /// </returns>
        /// <exception cref="InvalidOperationException">
        /// This value is a default <see cref="XmlElement"/>, which refers to no node.
        /// </exception>
        /// <exception cref="ObjectDisposedException">
        /// The parent <see cref="XmlDocument"/> has been disposed.
        /// </exception>
        /// <remarks>
        /// Only direct children are searched, not deeper descendants. This method returns <see langword="false"/> if this node is not an element.
        /// </remarks>
        public bool TryGetChild(ReadOnlySpan<byte> utf8LocalName, out XmlElement value)
        {
            return TryFindChildUtf8(utf8LocalName, out value);
        }

        /// <summary>
        /// Attempts to get the value of this node as a <see cref="DateTime"/>.
        /// </summary>
        /// <param name="value">When this method returns, contains the value if it could be read, or the default value if it could not. This parameter is treated as uninitialized.</param>
        /// <returns>
        /// <see langword="true"/> if the whole value is an <c>xs:dateTime</c> that a <see cref="DateTime"/> can represent; otherwise, <see langword="false"/>.
        /// </returns>
        /// <exception cref="InvalidOperationException">
        /// This value is a default <see cref="XmlElement"/>, which refers to no node.
        /// </exception>
        /// <exception cref="ObjectDisposedException">
        /// The parent <see cref="XmlDocument"/> has been disposed.
        /// </exception>
        /// <exception cref="XmlException">
        /// This node is an element that is marked as nil.
        /// </exception>
        /// <remarks>
        /// The value is read as it is by <see cref="GetDateTime"/>.
        /// </remarks>
        public bool TryGetDateTime(out DateTime value)
        {
            var utf8 = TypedValueUtf8(out var rented);
            var parsed = XmlLexical.TryParseDateTime(utf8, out value);
            XmlDocument.ReturnScratch(rented);
            return parsed;
        }

        /// <summary>
        /// Attempts to get the value of this node as a <see cref="DateTimeOffset"/>.
        /// </summary>
        /// <param name="value">When this method returns, contains the value if it could be read, or the default value if it could not. This parameter is treated as uninitialized.</param>
        /// <returns>
        /// <see langword="true"/> if the whole value is an <c>xs:dateTime</c> that a <see cref="DateTimeOffset"/> can represent; otherwise, <see langword="false"/>.
        /// </returns>
        /// <exception cref="InvalidOperationException">
        /// This value is a default <see cref="XmlElement"/>, which refers to no node.
        /// </exception>
        /// <exception cref="ObjectDisposedException">
        /// The parent <see cref="XmlDocument"/> has been disposed.
        /// </exception>
        /// <exception cref="XmlException">
        /// This node is an element that is marked as nil.
        /// </exception>
        /// <remarks>
        /// The value is read as it is by <see cref="GetDateTimeOffset"/>.
        /// </remarks>
        public bool TryGetDateTimeOffset(out DateTimeOffset value)
        {
            var utf8 = TypedValueUtf8(out var rented);
            var parsed = XmlLexical.TryParseDateTimeOffset(utf8, out value);
            XmlDocument.ReturnScratch(rented);
            return parsed;
        }

        /// <summary>
        /// Attempts to get the value of this node as a <see cref="decimal"/>.
        /// </summary>
        /// <param name="value">When this method returns, contains the value if it could be read, or the default value if it could not. This parameter is treated as uninitialized.</param>
        /// <returns>
        /// <see langword="true"/> if the whole value is an <c>xs:decimal</c> that a <see cref="decimal"/> can represent; otherwise, <see langword="false"/>.
        /// </returns>
        /// <exception cref="InvalidOperationException">
        /// This value is a default <see cref="XmlElement"/>, which refers to no node.
        /// </exception>
        /// <exception cref="ObjectDisposedException">
        /// The parent <see cref="XmlDocument"/> has been disposed.
        /// </exception>
        /// <exception cref="XmlException">
        /// This node is an element that is marked as nil.
        /// </exception>
        public bool TryGetDecimal(out decimal value)
        {
            var utf8 = TypedValueUtf8(out var rented);
            var parsed = XmlLexical.TryParseDecimal(utf8, out value);
            XmlDocument.ReturnScratch(rented);
            return parsed;
        }

        /// <summary>
        /// Attempts to get the value of this node as a <see cref="double"/>.
        /// </summary>
        /// <param name="value">When this method returns, contains the value if it could be read, or the default value if it could not. This parameter is treated as uninitialized.</param>
        /// <returns>
        /// <see langword="true"/> if the whole value is an <c>xs:double</c>; otherwise, <see langword="false"/>.
        /// </returns>
        /// <exception cref="InvalidOperationException">
        /// This value is a default <see cref="XmlElement"/>, which refers to no node.
        /// </exception>
        /// <exception cref="ObjectDisposedException">
        /// The parent <see cref="XmlDocument"/> has been disposed.
        /// </exception>
        /// <exception cref="XmlException">
        /// This node is an element that is marked as nil.
        /// </exception>
        /// <remarks>
        /// The literals <c>INF</c>, <c>-INF</c>, and <c>NaN</c> are accepted in any letter case.
        /// </remarks>
        public bool TryGetDouble(out double value)
        {
            var utf8 = TypedValueUtf8(out var rented);
            var parsed = XmlLexical.TryParseDouble(utf8, out value);
            XmlDocument.ReturnScratch(rented);
            return parsed;
        }

        /// <summary>
        /// Attempts to get the value of this node as a <see cref="Guid"/>.
        /// </summary>
        /// <param name="value">When this method returns, contains the value if it could be read, or the default value if it could not. This parameter is treated as uninitialized.</param>
        /// <returns>
        /// <see langword="true"/> if the whole value is a GUID in the <c>D</c>, <c>N</c>, <c>B</c>, or <c>P</c> format; otherwise, <see langword="false"/>.
        /// </returns>
        /// <exception cref="InvalidOperationException">
        /// This value is a default <see cref="XmlElement"/>, which refers to no node.
        /// </exception>
        /// <exception cref="ObjectDisposedException">
        /// The parent <see cref="XmlDocument"/> has been disposed.
        /// </exception>
        /// <exception cref="XmlException">
        /// This node is an element that is marked as nil.
        /// </exception>
        public bool TryGetGuid(out Guid value)
        {
            var utf8 = TypedValueUtf8(out var rented);
            var parsed = XmlLexical.TryParseGuid(utf8, out value);
            XmlDocument.ReturnScratch(rented);
            return parsed;
        }

        /// <summary>
        /// Attempts to get the value of this node as a <see cref="short"/>.
        /// </summary>
        /// <param name="value">When this method returns, contains the value if it could be read, or the default value if it could not. This parameter is treated as uninitialized.</param>
        /// <returns>
        /// <see langword="true"/> if the whole value is an <c>xs:short</c>; otherwise, <see langword="false"/>.
        /// </returns>
        /// <exception cref="InvalidOperationException">
        /// This value is a default <see cref="XmlElement"/>, which refers to no node.
        /// </exception>
        /// <exception cref="ObjectDisposedException">
        /// The parent <see cref="XmlDocument"/> has been disposed.
        /// </exception>
        /// <exception cref="XmlException">
        /// This node is an element that is marked as nil.
        /// </exception>
        public bool TryGetInt16(out short value)
        {
            var utf8 = TypedValueUtf8(out var rented);
            var parsed = XmlLexical.TryParseInt16(utf8, out value);
            XmlDocument.ReturnScratch(rented);
            return parsed;
        }

        /// <summary>
        /// Attempts to get the value of this node as an <see cref="int"/>.
        /// </summary>
        /// <param name="value">When this method returns, contains the value if it could be read, or the default value if it could not. This parameter is treated as uninitialized.</param>
        /// <returns>
        /// <see langword="true"/> if the whole value is an <c>xs:int</c>; otherwise, <see langword="false"/>.
        /// </returns>
        /// <exception cref="InvalidOperationException">
        /// This value is a default <see cref="XmlElement"/>, which refers to no node.
        /// </exception>
        /// <exception cref="ObjectDisposedException">
        /// The parent <see cref="XmlDocument"/> has been disposed.
        /// </exception>
        /// <exception cref="XmlException">
        /// This node is an element that is marked as nil.
        /// </exception>
        public bool TryGetInt32(out int value)
        {
            var utf8 = TypedValueUtf8(out var rented);
            var parsed = XmlLexical.TryParseInt32(utf8, out value);
            XmlDocument.ReturnScratch(rented);
            return parsed;
        }

        /// <summary>
        /// Attempts to get the value of this node as a <see cref="long"/>.
        /// </summary>
        /// <param name="value">When this method returns, contains the value if it could be read, or the default value if it could not. This parameter is treated as uninitialized.</param>
        /// <returns>
        /// <see langword="true"/> if the whole value is an <c>xs:long</c>; otherwise, <see langword="false"/>.
        /// </returns>
        /// <exception cref="InvalidOperationException">
        /// This value is a default <see cref="XmlElement"/>, which refers to no node.
        /// </exception>
        /// <exception cref="ObjectDisposedException">
        /// The parent <see cref="XmlDocument"/> has been disposed.
        /// </exception>
        /// <exception cref="XmlException">
        /// This node is an element that is marked as nil.
        /// </exception>
        public bool TryGetInt64(out long value)
        {
            var utf8 = TypedValueUtf8(out var rented);
            var parsed = XmlLexical.TryParseInt64(utf8, out value);
            XmlDocument.ReturnScratch(rented);
            return parsed;
        }

        /// <summary>
        /// Attempts to get the value of this node as an <see cref="sbyte"/>.
        /// </summary>
        /// <param name="value">When this method returns, contains the value if it could be read, or the default value if it could not. This parameter is treated as uninitialized.</param>
        /// <returns>
        /// <see langword="true"/> if the whole value is an <c>xs:byte</c>; otherwise, <see langword="false"/>.
        /// </returns>
        /// <exception cref="InvalidOperationException">
        /// This value is a default <see cref="XmlElement"/>, which refers to no node.
        /// </exception>
        /// <exception cref="ObjectDisposedException">
        /// The parent <see cref="XmlDocument"/> has been disposed.
        /// </exception>
        /// <exception cref="XmlException">
        /// This node is an element that is marked as nil.
        /// </exception>
        [CLSCompliant(false)]
        public bool TryGetSByte(out sbyte value)
        {
            var utf8 = TypedValueUtf8(out var rented);
            var parsed = XmlLexical.TryParseSByte(utf8, out value);
            XmlDocument.ReturnScratch(rented);
            return parsed;
        }

        /// <summary>
        /// Attempts to get the value of this node as a <see cref="float"/>.
        /// </summary>
        /// <param name="value">When this method returns, contains the value if it could be read, or the default value if it could not. This parameter is treated as uninitialized.</param>
        /// <returns>
        /// <see langword="true"/> if the whole value is an <c>xs:float</c>; otherwise, <see langword="false"/>.
        /// </returns>
        /// <exception cref="InvalidOperationException">
        /// This value is a default <see cref="XmlElement"/>, which refers to no node.
        /// </exception>
        /// <exception cref="ObjectDisposedException">
        /// The parent <see cref="XmlDocument"/> has been disposed.
        /// </exception>
        /// <exception cref="XmlException">
        /// This node is an element that is marked as nil.
        /// </exception>
        /// <remarks>
        /// The literals <c>INF</c>, <c>-INF</c>, and <c>NaN</c> are accepted in any letter case.
        /// </remarks>
        public bool TryGetSingle(out float value)
        {
            var utf8 = TypedValueUtf8(out var rented);
            var parsed = XmlLexical.TryParseSingle(utf8, out value);
            XmlDocument.ReturnScratch(rented);
            return parsed;
        }

        /// <summary>
        /// Attempts to get the value of this node as a <see cref="ushort"/>.
        /// </summary>
        /// <param name="value">When this method returns, contains the value if it could be read, or the default value if it could not. This parameter is treated as uninitialized.</param>
        /// <returns>
        /// <see langword="true"/> if the whole value is an <c>xs:unsignedShort</c>; otherwise, <see langword="false"/>.
        /// </returns>
        /// <exception cref="InvalidOperationException">
        /// This value is a default <see cref="XmlElement"/>, which refers to no node.
        /// </exception>
        /// <exception cref="ObjectDisposedException">
        /// The parent <see cref="XmlDocument"/> has been disposed.
        /// </exception>
        /// <exception cref="XmlException">
        /// This node is an element that is marked as nil.
        /// </exception>
        [CLSCompliant(false)]
        public bool TryGetUInt16(out ushort value)
        {
            var utf8 = TypedValueUtf8(out var rented);
            var parsed = XmlLexical.TryParseUInt16(utf8, out value);
            XmlDocument.ReturnScratch(rented);
            return parsed;
        }

        /// <summary>
        /// Attempts to get the value of this node as a <see cref="uint"/>.
        /// </summary>
        /// <param name="value">When this method returns, contains the value if it could be read, or the default value if it could not. This parameter is treated as uninitialized.</param>
        /// <returns>
        /// <see langword="true"/> if the whole value is an <c>xs:unsignedInt</c>; otherwise, <see langword="false"/>.
        /// </returns>
        /// <exception cref="InvalidOperationException">
        /// This value is a default <see cref="XmlElement"/>, which refers to no node.
        /// </exception>
        /// <exception cref="ObjectDisposedException">
        /// The parent <see cref="XmlDocument"/> has been disposed.
        /// </exception>
        /// <exception cref="XmlException">
        /// This node is an element that is marked as nil.
        /// </exception>
        [CLSCompliant(false)]
        public bool TryGetUInt32(out uint value)
        {
            var utf8 = TypedValueUtf8(out var rented);
            var parsed = XmlLexical.TryParseUInt32(utf8, out value);
            XmlDocument.ReturnScratch(rented);
            return parsed;
        }

        /// <summary>
        /// Attempts to get the value of this node as a <see cref="ulong"/>.
        /// </summary>
        /// <param name="value">When this method returns, contains the value if it could be read, or the default value if it could not. This parameter is treated as uninitialized.</param>
        /// <returns>
        /// <see langword="true"/> if the whole value is an <c>xs:unsignedLong</c>; otherwise, <see langword="false"/>.
        /// </returns>
        /// <exception cref="InvalidOperationException">
        /// This value is a default <see cref="XmlElement"/>, which refers to no node.
        /// </exception>
        /// <exception cref="ObjectDisposedException">
        /// The parent <see cref="XmlDocument"/> has been disposed.
        /// </exception>
        /// <exception cref="XmlException">
        /// This node is an element that is marked as nil.
        /// </exception>
        [CLSCompliant(false)]
        public bool TryGetUInt64(out ulong value)
        {
            var utf8 = TypedValueUtf8(out var rented);
            var parsed = XmlLexical.TryParseUInt64(utf8, out value);
            XmlDocument.ReturnScratch(rented);
            return parsed;
        }

        /// <summary>
        /// Writes this node to the specified writer as XML.
        /// </summary>
        /// <param name="writer">The writer to which to write the node.</param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="writer"/> is <see langword="null"/>.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// This value is a default <see cref="XmlElement"/>, which refers to no node.
        /// </exception>
        /// <exception cref="ObjectDisposedException">
        /// The parent <see cref="XmlDocument"/> has been disposed.
        /// </exception>
        /// <remarks>
        /// An element is written with its attributes and descendants. Nothing is written for an attribute,
        /// because an attribute has no markup outside its element.
        /// </remarks>
        public void WriteTo(Utf8XmlWriter writer)
        {
            ArgumentNullException.ThrowIfNull(writer);
            EnsureDocument().WriteRow(_index, writer);
        }

        /// <summary>
        /// Writes the attributes and the children of this element into the element that
        /// <paramref name="writer"/> has open, without this element's own tags.
        /// </summary>
        /// <param name="writer">The destination, with a start tag open.</param>
        /// <remarks>
        /// A node that is not an element is written as <see cref="WriteTo"/> writes it.
        /// </remarks>
        internal void WriteContentTo(Utf8XmlWriter writer)
        {
            EnsureDocument().WriteContent(_index, writer);
        }

        /// <summary>
        /// Determines whether two <see cref="XmlElement"/> values refer to the same node of the same <see cref="XmlDocument"/>.
        /// </summary>
        /// <param name="left">The first value to compare.</param>
        /// <param name="right">The second value to compare.</param>
        /// <returns>
        /// <see langword="true"/> if <paramref name="left"/> and <paramref name="right"/> refer to the same
        /// node of the same document; otherwise, <see langword="false"/>.
        /// </returns>
        /// <remarks>
        /// Two nodes with the same content at different positions, or in different documents, are not equal.
        /// </remarks>
        public static bool operator ==(XmlElement left, XmlElement right)
        {
            return left.Equals(right);
        }

        /// <summary>
        /// Determines whether two <see cref="XmlElement"/> values refer to different nodes.
        /// </summary>
        /// <param name="left">The first value to compare.</param>
        /// <param name="right">The second value to compare.</param>
        /// <returns>
        /// <see langword="true"/> if <paramref name="left"/> and <paramref name="right"/> refer to different
        /// nodes or to nodes of different documents; otherwise, <see langword="false"/>.
        /// </returns>
        public static bool operator !=(XmlElement left, XmlElement right)
        {
            return !left.Equals(right);
        }

        #endregion

        #region Internal Methods

        /// <summary>
        /// Returns the child row range of this cursor.
        /// </summary>
        /// <param name="document">The owning document.</param>
        /// <param name="first">When this method returns, the first child row.</param>
        /// <param name="end">When this method returns, the row after the last descendant.</param>
        /// <remarks>
        /// Children start after the attribute rows. The range is empty for anything that is not an element.
        /// </remarks>
        internal void ChildRange(XmlDocument document, out int first, out int end)
        {
            ref var row = ref document.Row(_index);
            if (!XmlDocument.IsElementKind(row.Kind))
            {
                first = 0;
                end = 0;
                return;
            }

            first = _index + 1 + row.AttributeCount;
            end = row.EndIndex;
        }

        /// <summary>
        /// Returns the owning document, throwing when this cursor is empty or the document was disposed.
        /// </summary>
        /// <returns>The live document.</returns>
        internal XmlDocument EnsureDocument()
        {
            if (_document is null)
            {
                throw new InvalidOperationException("The XML element is empty.");
            }

            _document.ThrowIfDisposed();
            return _document;
        }

        /// <summary>
        /// Finds an attribute by local name and, optionally, namespace.
        /// </summary>
        /// <param name="localName">The local name.</param>
        /// <param name="namespaceUri">The namespace URI, or <see langword="null"/> to match any.</param>
        /// <param name="value">When this method returns true, the attribute cursor.</param>
        /// <returns><see langword="true"/> if the attribute exists.</returns>
        internal bool TryFindAttribute(ReadOnlySpan<char> localName, string? namespaceUri, out XmlElement value)
        {
            var document = EnsureDocument();
            ref var row = ref document.Row(_index);
            if (XmlDocument.IsElementKind(row.Kind))
            {
                var end = _index + 1 + row.AttributeCount;
                for (var i = _index + 1; i < end; i++)
                {
                    ref var attribute = ref document.Row(i);
                    if (XmlDocument.Utf8Equals(document.LocalName(in attribute), localName)
                        && (namespaceUri is null || XmlDocument.Utf8Equals(document.NamespaceUri(in attribute), namespaceUri)))
                    {
                        value = new XmlElement(document, i);
                        return true;
                    }
                }
            }

            value = default;
            return false;
        }

        /// <summary>
        /// Finds an attribute, in any namespace, by a local name that is given as UTF-8.
        /// </summary>
        /// <param name="utf8LocalName">The UTF-8 local name.</param>
        /// <param name="value">When this method returns true, the attribute cursor.</param>
        /// <returns><see langword="true"/> if the attribute exists.</returns>
        internal bool TryFindAttributeUtf8(ReadOnlySpan<byte> utf8LocalName, out XmlElement value)
        {
            var document = EnsureDocument();
            ref var row = ref document.Row(_index);
            if (XmlDocument.IsElementKind(row.Kind))
            {
                var end = _index + 1 + row.AttributeCount;
                for (var i = _index + 1; i < end; i++)
                {
                    if (document.LocalName(in document.Row(i)).SequenceEqual(utf8LocalName))
                    {
                        value = new XmlElement(document, i);
                        return true;
                    }
                }
            }

            value = default;
            return false;
        }

        /// <summary>
        /// Finds a child element by local name and, optionally, namespace.
        /// </summary>
        /// <param name="localName">The local name.</param>
        /// <param name="namespaceUri">The namespace URI, or <see langword="null"/> to match any.</param>
        /// <param name="value">When this method returns true, the child cursor.</param>
        /// <returns><see langword="true"/> if the child exists.</returns>
        internal bool TryFindChild(ReadOnlySpan<char> localName, string? namespaceUri, out XmlElement value)
        {
            var document = EnsureDocument();
            ChildRange(document, out var first, out var end);
            for (var i = first; i < end; i = document.NextSiblingIndex(i))
            {
                ref var child = ref document.Row(i);
                if (XmlDocument.IsElementKind(child.Kind)
                    && XmlDocument.Utf8Equals(document.LocalName(in child), localName)
                    && (namespaceUri is null || XmlDocument.Utf8Equals(document.NamespaceUri(in child), namespaceUri)))
                {
                    value = new XmlElement(document, i);
                    return true;
                }
            }

            value = default;
            return false;
        }

        /// <summary>
        /// Finds a child element, in any namespace, by a local name that is given as UTF-8.
        /// </summary>
        /// <param name="utf8LocalName">The UTF-8 local name.</param>
        /// <param name="value">When this method returns true, the child cursor.</param>
        /// <returns><see langword="true"/> if the child exists.</returns>
        internal bool TryFindChildUtf8(ReadOnlySpan<byte> utf8LocalName, out XmlElement value)
        {
            var document = EnsureDocument();
            ChildRange(document, out var first, out var end);
            for (var i = first; i < end; i = document.NextSiblingIndex(i))
            {
                ref var child = ref document.Row(i);
                if (XmlDocument.IsElementKind(child.Kind) && document.LocalName(in child).SequenceEqual(utf8LocalName))
                {
                    value = new XmlElement(document, i);
                    return true;
                }
            }

            value = default;
            return false;
        }

        /// <summary>
        /// Returns the UTF-8 value a typed getter should parse.
        /// </summary>
        /// <param name="rented">A pooled array to return with <see cref="XmlDocument.ReturnScratch"/>, or <see langword="null"/>.</param>
        /// <returns>The value bytes.</returns>
        /// <exception cref="XmlException">This element carries <c>xsi:nil="true"</c> and therefore has no value.</exception>
        internal ReadOnlySpan<byte> TypedValueUtf8(out byte[]? rented)
        {
            return ValueUtf8(out rented, typed: true);
        }

        /// <summary>
        /// Returns the value of this node as a string.
        /// </summary>
        /// <returns>The attribute or leaf value, or concatenated descendant text for elements.</returns>
        internal string ValueString()
        {
            var utf8 = ValueUtf8(out var rented);
            var value = Encoding.UTF8.GetString(utf8);
            XmlDocument.ReturnScratch(rented);
            return value;
        }

        /// <summary>
        /// Returns the UTF-8 value of this node, or concatenated descendant text for elements.
        /// </summary>
        /// <param name="rented">A pooled array to return with <see cref="XmlDocument.ReturnScratch"/>, or <see langword="null"/>.</param>
        /// <param name="typed">True when a typed getter is asking, in which case a nil element throws because it has no value.</param>
        /// <returns>The value bytes.</returns>
        /// <remarks>
        /// An element with a single text or CDATA child, which is the usual typed-value shape, returns that
        /// child's bytes directly. Because rows are in document order, mixed content is every text and CDATA
        /// row inside the element's row range, concatenated into a pooled buffer.
        /// </remarks>
        internal ReadOnlySpan<byte> ValueUtf8(out byte[]? rented, bool typed = false)
        {
            rented = null;
            var document = EnsureDocument();
            ref var row = ref document.Row(_index);
            if (!XmlDocument.IsElementKind(row.Kind))
            {
                return document.Value(in row);
            }

            if (typed && row.Kind is XmlValueKind.Nil)
            {
                throw XmlException.Create("The element is nil and has no value.");
            }

            var first = _index + 1 + row.AttributeCount;
            var end = row.EndIndex;
            if (end - first == 1)
            {
                ref var only = ref document.Row(first);
                return only.Kind is XmlValueKind.Text or XmlValueKind.CData ? document.Value(in only) : [];
            }

            var length = 0;
            for (var i = first; i < end; i++)
            {
                ref var descendant = ref document.Row(i);
                if (descendant.Kind is XmlValueKind.Text or XmlValueKind.CData)
                {
                    length += descendant.ValueLength;
                }
            }

            if (length == 0)
            {
                return [];
            }

            rented = ArrayPool<byte>.Shared.Rent(length);
            var written = 0;
            for (var i = first; i < end; i++)
            {
                ref var descendant = ref document.Row(i);
                if (descendant.Kind is XmlValueKind.Text or XmlValueKind.CData)
                {
                    document.Value(in descendant).CopyTo(rented.AsSpan(written));
                    written += descendant.ValueLength;
                }
            }

            return rented.AsSpan(0, written);
        }

        #endregion

        /// <summary>
        /// Represents an enumerator for the child nodes of an <see cref="XmlElement"/>.
        /// </summary>
        /// <remarks>
        /// <see cref="XmlElement.EnumerateChildren"/> returns this enumerator. It can be used directly in a
        /// <see langword="foreach"/> statement and does not allocate.
        /// </remarks>
        public struct Enumerator
        {

            #region Fields

            private XmlElement _current;
            private readonly XmlDocument _document;
            private readonly int _end;
            private int _next;

            #endregion

            #region Public Properties

            /// <summary>
            /// Gets the child node at the current position of the enumerator.
            /// </summary>
            /// <value>
            /// The child node at the current position, or a default <see cref="XmlElement"/> before the first
            /// call to <see cref="MoveNext"/>.
            /// </value>
            public readonly XmlElement Current => _current;

            #endregion

            #region Constructors

            /// <summary>
            /// Creates an enumerator over the child rows from <paramref name="first"/> up to <paramref name="end"/>.
            /// </summary>
            /// <param name="document">The owning document.</param>
            /// <param name="first">The first child row.</param>
            /// <param name="end">The row after the parent's last descendant.</param>
            internal Enumerator(XmlDocument document, int first, int end)
            {
                _document = document;
                _next = first;
                _end = end;
                _current = default;
            }

            #endregion

            #region Public Methods

            /// <summary>
            /// Returns an enumerator that iterates through the child nodes.
            /// </summary>
            /// <returns>
            /// A copy of this enumerator.
            /// </returns>
            public readonly Enumerator GetEnumerator()
            {
                return this;
            }

            /// <summary>
            /// Advances the enumerator to the next child node.
            /// </summary>
            /// <returns>
            /// <see langword="true"/> if the enumerator was advanced to the next child node; otherwise,
            /// <see langword="false"/>, because the enumerator has passed the last child node.
            /// </returns>
            /// <exception cref="ObjectDisposedException">
            /// The parent <see cref="XmlDocument"/> has been disposed.
            /// </exception>
            public bool MoveNext()
            {
                if (_next >= _end)
                {
                    return false;
                }

                _document.ThrowIfDisposed();
                _current = new XmlElement(_document, _next);
                _next = _document.NextSiblingIndex(_next);
                return true;
            }

            #endregion

        }

        /// <summary>
        /// Represents an enumerator for the attributes of an <see cref="XmlElement"/>.
        /// </summary>
        /// <remarks>
        /// <see cref="XmlElement.EnumerateAttributes"/> returns this enumerator. It can be used directly in a
        /// <see langword="foreach"/> statement and does not allocate.
        /// </remarks>
        public struct AttributeEnumerator
        {

            #region Fields

            private XmlElement _current;
            private readonly XmlDocument _document;
            private readonly int _end;
            private int _next;

            #endregion

            #region Public Properties

            /// <summary>
            /// Gets the attribute at the current position of the enumerator.
            /// </summary>
            /// <value>
            /// The attribute at the current position, or a default <see cref="XmlElement"/> before the first
            /// call to <see cref="MoveNext"/>.
            /// </value>
            public readonly XmlElement Current => _current;

            #endregion

            #region Constructors

            /// <summary>
            /// Creates an enumerator over the attribute rows from <paramref name="first"/> up to <paramref name="end"/>.
            /// </summary>
            /// <param name="document">The owning document.</param>
            /// <param name="first">The first attribute row.</param>
            /// <param name="end">The row after the last attribute.</param>
            internal AttributeEnumerator(XmlDocument document, int first, int end)
            {
                _document = document;
                _next = first;
                _end = end;
                _current = default;
            }

            #endregion

            #region Public Methods

            /// <summary>
            /// Returns an enumerator that iterates through the attributes.
            /// </summary>
            /// <returns>
            /// A copy of this enumerator.
            /// </returns>
            public readonly AttributeEnumerator GetEnumerator()
            {
                return this;
            }

            /// <summary>
            /// Advances the enumerator to the next attribute.
            /// </summary>
            /// <returns>
            /// <see langword="true"/> if the enumerator was advanced to the next attribute; otherwise,
            /// <see langword="false"/>, because the enumerator has passed the last attribute.
            /// </returns>
            /// <exception cref="ObjectDisposedException">
            /// The parent <see cref="XmlDocument"/> has been disposed.
            /// </exception>
            public bool MoveNext()
            {
                if (_next >= _end)
                {
                    return false;
                }

                _document.ThrowIfDisposed();
                _current = new XmlElement(_document, _next);
                _next++;
                return true;
            }

            #endregion

        }

    }

}
