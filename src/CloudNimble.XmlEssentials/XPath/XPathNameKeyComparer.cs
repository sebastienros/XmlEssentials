using System;
using System.Collections.Generic;

namespace CloudNimble.Text.Xml.XPath
{

    /// <summary>
    /// Compares expanded names byte for byte, as keys and as spans, so that <see cref="XPathNameIndex"/> is looked up
    /// without making a key.
    /// </summary>
    internal sealed class XPathNameKeyComparer : IEqualityComparer<XPathNameKey>, IAlternateEqualityComparer<XPathNameSpan, XPathNameKey>
    {

        #region Fields

        /// <summary>
        /// The one instance.
        /// </summary>
        internal static readonly XPathNameKeyComparer Instance = new();

        #endregion

        #region Public Methods

        /// <inheritdoc />
        public XPathNameKey Create(XPathNameSpan alternate)
        {
            return new XPathNameKey(alternate.LocalName.ToArray(), alternate.NamespaceUri.ToArray());
        }

        /// <inheritdoc />
        public bool Equals(XPathNameKey? x, XPathNameKey? y)
        {
            return x!.LocalName.AsSpan().SequenceEqual(y!.LocalName) && x.NamespaceUri.AsSpan().SequenceEqual(y.NamespaceUri);
        }

        /// <inheritdoc />
        public bool Equals(XPathNameSpan alternate, XPathNameKey other)
        {
            return alternate.LocalName.SequenceEqual(other.LocalName) && alternate.NamespaceUri.SequenceEqual(other.NamespaceUri);
        }

        /// <inheritdoc />
        public int GetHashCode(XPathNameKey obj)
        {
            return Hash(obj.LocalName, obj.NamespaceUri);
        }

        /// <inheritdoc />
        public int GetHashCode(XPathNameSpan alternate)
        {
            return Hash(alternate.LocalName, alternate.NamespaceUri);
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// Hashes an expanded name.
        /// </summary>
        private static int Hash(ReadOnlySpan<byte> localName, ReadOnlySpan<byte> namespaceUri)
        {
            var hash = new HashCode();
            hash.AddBytes(localName);
            hash.Add(namespaceUri.Length);
            hash.AddBytes(namespaceUri);
            return hash.ToHashCode();
        }

        #endregion

    }

}
