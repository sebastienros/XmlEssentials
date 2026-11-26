namespace CloudNimble.Text.Xml.XPath
{

    /// <summary>
    /// A compiled node test: its form, and the names it compares, as UTF-8.
    /// </summary>
    /// <remarks>
    /// The parser has resolved the prefix of a name test, so a test holds the namespace URI it matches, never a
    /// prefix. An unprefixed name test is in no namespace.
    /// </remarks>
    internal sealed class XPathNodeTest
    {

        #region Fields

        /// <summary>
        /// <c>node()</c>, which the abbreviations <c>.</c>, <c>..</c> and <c>//</c> stand for.
        /// </summary>
        internal static readonly XPathNodeTest AnyNode = new(XPathNodeTestKind.Node, [], [], null);

        #endregion

        #region Properties

        /// <summary>
        /// Gets the form of the test.
        /// </summary>
        internal XPathNodeTestKind Kind { get; }

        /// <summary>
        /// Gets the local name a <see cref="XPathNodeTestKind.Name"/> test compares, or empty.
        /// </summary>
        internal byte[] LocalName { get; }

        /// <summary>
        /// Gets whether the test can pass a text node.
        /// </summary>
        /// <remarks>
        /// A scan that cannot select a text node does not need to know where text runs begin, so it does not ask
        /// for the parent table.
        /// </remarks>
        internal bool MatchesText => Kind is XPathNodeTestKind.Node or XPathNodeTestKind.Text;

        /// <summary>
        /// Gets the namespace URI a <see cref="XPathNodeTestKind.Name"/> or
        /// <see cref="XPathNodeTestKind.NamespaceWildcard"/> test compares, or empty for no namespace.
        /// </summary>
        internal byte[] NamespaceUri { get; }

        /// <summary>
        /// Gets the target of <c>processing-instruction('t')</c>, or <see langword="null"/> for any target.
        /// </summary>
        internal byte[]? Target { get; }

        #endregion

        #region Constructors

        /// <summary>
        /// Creates a node test.
        /// </summary>
        /// <param name="kind">The form of the test.</param>
        /// <param name="localName">The local name of a name test, or empty.</param>
        /// <param name="namespaceUri">The namespace URI of a name test or a namespace wildcard, or empty.</param>
        /// <param name="target">The target of a processing-instruction test, or <see langword="null"/>.</param>
        internal XPathNodeTest(XPathNodeTestKind kind, byte[] localName, byte[] namespaceUri, byte[]? target)
        {
            Kind = kind;
            LocalName = localName;
            NamespaceUri = namespaceUri;
            Target = target;
        }

        #endregion

    }

}
