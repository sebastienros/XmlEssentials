using System;
using System.Text;

namespace CloudNimble.Text.Xml.XPath
{

    /// <summary>
    /// A node of an <see cref="XmlDocument"/> in the XPath 1.0 data model, as an XPath expression selects it.
    /// </summary>
    /// <example>
    /// <code>
    /// foreach (var node in XPathExpression.Compile("//@id").Select(document))
    /// {
    ///     Console.WriteLine($"{node.LocalName} = {node.GetString()}");
    /// }
    /// </code>
    /// </example>
    /// <remarks>
    /// <para>
    /// A node is a cursor into its document, as <see cref="XmlElement"/> is: it is used before the document is
    /// disposed, and using it afterwards throws <see cref="ObjectDisposedException"/>. Two nodes are equal when they
    /// are the same node of the same document. A default <see cref="XPathNode"/> refers to no node, and every member
    /// of it throws <see cref="InvalidOperationException"/>.
    /// </para>
    /// <para>
    /// The document node and the namespace nodes have no row in the document, so they have no
    /// <see cref="XmlElement"/>. A text node is a run of adjacent text and CDATA sections, and its
    /// <see cref="XmlElement"/> is on the first of them.
    /// </para>
    /// </remarks>
    public readonly struct XPathNode : IEquatable<XPathNode>
    {

        #region Fields

        private readonly XmlDocument? _document;
        private readonly long _id;

        #endregion

        #region Properties

        /// <summary>
        /// Gets the kind of the node.
        /// </summary>
        /// <exception cref="InvalidOperationException">This is a default <see cref="XPathNode"/>.</exception>
        /// <exception cref="ObjectDisposedException">The document has been disposed.</exception>
        public XPathNodeKind Kind => Navigator().KindOf(_id);

        /// <summary>
        /// Gets the local name of the node.
        /// </summary>
        /// <value>
        /// The local name of an element or an attribute, the target of a processing instruction, the prefix of a
        /// namespace node (empty for the default namespace), and an empty string for any other node.
        /// </value>
        /// <exception cref="InvalidOperationException">This is a default <see cref="XPathNode"/>.</exception>
        /// <exception cref="ObjectDisposedException">The document has been disposed.</exception>
        public string LocalName => Encoding.UTF8.GetString(Navigator().LocalName(_id));

        /// <summary>
        /// Gets the namespace URI of the node.
        /// </summary>
        /// <value>The namespace URI of an element or an attribute, and an empty string for any other node or for no namespace.</value>
        /// <exception cref="InvalidOperationException">This is a default <see cref="XPathNode"/>.</exception>
        /// <exception cref="ObjectDisposedException">The document has been disposed.</exception>
        public string NamespaceUri => Encoding.UTF8.GetString(Navigator().NamespaceUri(_id));

        /// <summary>
        /// Gets the prefix of the node.
        /// </summary>
        /// <value>The prefix of an element or an attribute as the document writes it, and an empty string for any other node.</value>
        /// <exception cref="InvalidOperationException">This is a default <see cref="XPathNode"/>.</exception>
        /// <exception cref="ObjectDisposedException">The document has been disposed.</exception>
        public string Prefix => Encoding.UTF8.GetString(Navigator().Prefix(_id));

        /// <summary>
        /// Gets the document of the node, or <see langword="null"/> for a default node.
        /// </summary>
        internal XmlDocument? Document => _document;

        /// <summary>
        /// Gets the id of the node.
        /// </summary>
        internal long Id => _id;

        #endregion

        #region Constructors

        /// <summary>
        /// Creates a node.
        /// </summary>
        /// <param name="document">The document.</param>
        /// <param name="id">The id of the node.</param>
        internal XPathNode(XmlDocument document, long id)
        {
            _document = document;
            _id = id;
        }

        #endregion

        #region Public Methods

        /// <inheritdoc />
        public bool Equals(XPathNode other)
        {
            return ReferenceEquals(_document, other._document) && _id == other._id;
        }

        /// <inheritdoc />
        public override bool Equals(object? obj)
        {
            return obj is XPathNode other && Equals(other);
        }

        /// <summary>
        /// Returns the cursor on the row of the node.
        /// </summary>
        /// <returns>The cursor. For a text node, it is on the first text or CDATA section of the run.</returns>
        /// <exception cref="InvalidOperationException">
        /// The node is the document node or a namespace node, which have no row.
        /// -or-
        /// This is a default <see cref="XPathNode"/>.
        /// </exception>
        /// <exception cref="ObjectDisposedException">The document has been disposed.</exception>
        public XmlElement GetElement()
        {
            if (!TryGetElement(out var element))
            {
                throw new InvalidOperationException($"A node of kind {Kind} has no {nameof(XmlElement)}.");
            }

            return element;
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return HashCode.Combine(_document, _id);
        }

        /// <summary>
        /// Returns the string-value of the node.
        /// </summary>
        /// <returns>
        /// The text of every text and CDATA section under the document node or an element, in document order; the value
        /// of an attribute, a comment or a text node; the data of a processing instruction; and the namespace URI of a
        /// namespace node.
        /// </returns>
        /// <exception cref="InvalidOperationException">This is a default <see cref="XPathNode"/>.</exception>
        /// <exception cref="ObjectDisposedException">The document has been disposed.</exception>
        public string GetString()
        {
            return Encoding.UTF8.GetString(Navigator().StringValue(_id).Span);
        }

        /// <summary>
        /// Gets the cursor on the row of the node, when it has one.
        /// </summary>
        /// <param name="value">When this method returns <see langword="true"/>, the cursor; for a text node, on the first text or CDATA section of the run.</param>
        /// <returns><see langword="true"/> for every kind of node but the document node and a namespace node.</returns>
        /// <exception cref="InvalidOperationException">This is a default <see cref="XPathNode"/>.</exception>
        /// <exception cref="ObjectDisposedException">The document has been disposed.</exception>
        public bool TryGetElement(out XmlElement value)
        {
            if (Kind is XPathNodeKind.Document or XPathNodeKind.Namespace)
            {
                value = default;
                return false;
            }

            value = new XmlElement(_document!, XPathNodeId.Row(_id));
            return true;
        }

        /// <summary>
        /// Returns whether two nodes are the same node of the same document.
        /// </summary>
        /// <param name="left">The first node.</param>
        /// <param name="right">The second node.</param>
        /// <returns><see langword="true"/> when they are equal.</returns>
        public static bool operator ==(XPathNode left, XPathNode right)
        {
            return left.Equals(right);
        }

        /// <summary>
        /// Returns whether two nodes are not the same node of the same document.
        /// </summary>
        /// <param name="left">The first node.</param>
        /// <param name="right">The second node.</param>
        /// <returns><see langword="true"/> when they are not equal.</returns>
        public static bool operator !=(XPathNode left, XPathNode right)
        {
            return !left.Equals(right);
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// Returns a navigator over the document, after checking that there is one and that it is not disposed.
        /// </summary>
        private XPathNavigator Navigator()
        {
            if (_document is null)
            {
                throw new InvalidOperationException("The XPath node is empty.");
            }

            _document.ThrowIfDisposed();
            return new XPathNavigator(_document);
        }

        #endregion

    }

}
