namespace CloudNimble.Text.Xml.XPath
{

    /// <summary>
    /// An expanded name held as UTF-8, a key of <see cref="XPathNameIndex"/>.
    /// </summary>
    /// <param name="localName">The local name.</param>
    /// <param name="namespaceUri">The namespace URI, or empty.</param>
    internal sealed class XPathNameKey(byte[] localName, byte[] namespaceUri)
    {

        #region Properties

        /// <summary>
        /// Gets the local name.
        /// </summary>
        internal byte[] LocalName { get; } = localName;

        /// <summary>
        /// Gets the namespace URI, or empty for no namespace.
        /// </summary>
        internal byte[] NamespaceUri { get; } = namespaceUri;

        #endregion

    }

}
