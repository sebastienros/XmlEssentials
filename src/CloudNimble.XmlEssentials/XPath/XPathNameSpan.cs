using System;

namespace CloudNimble.Text.Xml.XPath
{

    /// <summary>
    /// An expanded name as two spans of UTF-8, to look a name up in <see cref="XPathNameIndex"/> without copying it.
    /// </summary>
    /// <param name="localName">The local name.</param>
    /// <param name="namespaceUri">The namespace URI, or empty.</param>
    internal readonly ref struct XPathNameSpan(ReadOnlySpan<byte> localName, ReadOnlySpan<byte> namespaceUri)
    {

        #region Properties

        /// <summary>
        /// Gets the local name.
        /// </summary>
        internal ReadOnlySpan<byte> LocalName { get; } = localName;

        /// <summary>
        /// Gets the namespace URI, or empty for no namespace.
        /// </summary>
        internal ReadOnlySpan<byte> NamespaceUri { get; } = namespaceUri;

        #endregion

    }

}
