using System;
using System.Collections.Generic;

namespace CloudNimble.Text.Xml.XPath
{

    /// <summary>
    /// Compares UTF-8 strings byte for byte, so that a set of string-values can be held in a hash set.
    /// </summary>
    internal sealed class XPathBytesComparer : IEqualityComparer<ReadOnlyMemory<byte>>
    {

        #region Fields

        /// <summary>
        /// The one instance.
        /// </summary>
        internal static readonly XPathBytesComparer Instance = new();

        #endregion

        #region Public Methods

        /// <inheritdoc />
        public bool Equals(ReadOnlyMemory<byte> x, ReadOnlyMemory<byte> y)
        {
            return x.Span.SequenceEqual(y.Span);
        }

        /// <inheritdoc />
        public int GetHashCode(ReadOnlyMemory<byte> obj)
        {
            var hash = new HashCode();
            hash.AddBytes(obj.Span);
            return hash.ToHashCode();
        }

        #endregion

    }

}
